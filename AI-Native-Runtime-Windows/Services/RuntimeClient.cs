using System;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using NSec.Cryptography;

namespace AI_Native_Runtime_Windows.Services
{
    /// <summary>CORE's named pipe is not reachable at all (daemon not running, pipe name mismatch, connect timeout). Distinct from a reachable-but-rejecting CORE (see <see cref="RuntimeRpcException"/>).</summary>
    public sealed class RuntimeUnavailableException : Exception
    {
        public RuntimeUnavailableException(string message, Exception? inner = null) : base(message, inner) { }
    }

    /// <summary>CORE answered over the pipe with a <c>RuntimeError</c> envelope (`api/framing.rs`'s `ResponseEnvelope.error`). <see cref="Code"/> is CORE's `SCREAMING_SNAKE_CASE` error code (`ainativeruntime_protocol::error::codes`).</summary>
    public sealed class RuntimeRpcException : Exception
    {
        public string Code { get; }
        public RuntimeRpcException(string code, string message) : base(message) { Code = code; }

        /// <summary>True for the codes CORE returns when IT could not reach RTAPI (`installation.rs`'s `HttpInstallationBindingBackend::bind` and `registration.rs`'s network/HTTP-status error paths both use `NODE_UNAVAILABLE` for this). From the desktop's point of view this is the same user-facing situation as Firebase being unreachable: the substrate is offline, not "your credentials/keys are wrong."</summary>
        public bool IndicatesSubstrateOffline => Code == "NODE_UNAVAILABLE";

        /// <summary>True for the codes CORE returns when RTAPI (or CORE's own local trust check) affirmatively refused the request - wrong key for a claimed applicationId, no matching organization/first-party claim, an already-bound key conflict, an unknown/expired challenge, etc.</summary>
        public bool IndicatesRegistrationRejected => Code is "PERMISSION_DENIED" or "UNAUTHENTICATED" or "VALIDATION_FAILED" or "SESSION_EXPIRED";
    }

    public sealed record DeviceRegistrationOutcome(
        string NodeId,
        string DeviceId,
        string OrganizationId,
        string State,
        string IssuedAt,
        string ExpiresAt);

    /// <summary>
    /// The local client that talks to CORE's daemon over its Windows named
    /// pipe transport (`transport/named_pipe.rs`, pipe name
    /// <c>\\.\pipe\ainativeruntime-runtime</c> - `main.rs` passes the fixed
    /// pipe id <c>"runtime"</c>). Framing is newline-delimited JSON, one
    /// <c>RequestEnvelope</c>/<c>ResponseEnvelope</c> object per line
    /// (`api/framing.rs`).
    ///
    /// <para>
    /// <b>Two separate keypairs - do not conflate them.</b> This class
    /// generates and holds the APPLICATION's installation Ed25519 keypair
    /// (this process, C# side, stored via <see cref="CredentialStore"/>)
    /// and uses it only to prove possession during
    /// <c>application.bind</c>/<c>session.challenge</c>/<c>session.establish</c>.
    /// The NODE's own Ed25519 keypair is generated and held entirely by
    /// CORE (Rust side, `identity::node_identity`) - this class never
    /// generates, sees, or stores it. <see cref="RegisterDeviceAsync"/>
    /// only relays the already-registered outcome fields
    /// <c>device.register</c>'s response carries back
    /// (<c>nodeId</c>/<c>deviceId</c>/<c>organizationId</c>/<c>state</c>/
    /// <c>issuedAt</c>/<c>expiresAt</c>) - never a key.
    /// </para>
    /// </summary>
    public sealed class RuntimeClient : IDisposable
    {
        private readonly string _pipeName;
        private readonly string _applicationId;
        private readonly CredentialStore _credentialStore;

        private NamedPipeClientStream? _pipe;
        private StreamWriter? _writer;
        private StreamReader? _reader;
        private string? _sessionToken;

        private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(3);

        public RuntimeClient(CredentialStore credentialStore, string applicationId, string pipeName = "ainativeruntime-runtime")
        {
            _credentialStore = credentialStore;
            _applicationId = applicationId;
            _pipeName = pipeName;
        }

        public bool HasSession => _sessionToken is not null;

        /// <summary>
        /// Opens the named pipe connection to CORE. Wraps every failure
        /// mode (daemon not running -> immediate connect refusal; daemon
        /// present but not accepting -> timeout) into
        /// <see cref="RuntimeUnavailableException"/>, the "runtime
        /// unavailable" UI state.
        /// </summary>
        public async Task ConnectAsync(CancellationToken ct = default)
        {
            try
            {
                var pipe = new NamedPipeClientStream(".", _pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
                using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                timeoutCts.CancelAfter(ConnectTimeout);
                await pipe.ConnectAsync(timeoutCts.Token).ConfigureAwait(false);

                _pipe = pipe;
                _writer = new StreamWriter(pipe, new UTF8Encoding(false)) { AutoFlush = false, NewLine = "\n" };
                _reader = new StreamReader(pipe, Encoding.UTF8);
            }
            catch (OperationCanceledException ex) when (!ct.IsCancellationRequested)
            {
                throw new RuntimeUnavailableException(
                    $"Timed out connecting to the AI Native Runtime Core daemon on pipe '{_pipeName}'. Is the Core running?", ex);
            }
            catch (Exception ex) when (ex is IOException or TimeoutException or UnauthorizedAccessException)
            {
                throw new RuntimeUnavailableException(
                    $"Could not connect to the AI Native Runtime Core daemon on pipe '{_pipeName}'. Is the Core running?", ex);
            }
        }

        /// <summary>
        /// Ensures this installation has its own Ed25519 keypair, generating
        /// and persisting one via <see cref="CredentialStore"/> on first
        /// use. Idempotent - a second call reuses the stored key.
        /// </summary>
        public string EnsureInstallationKey()
        {
            if (_credentialStore.TryGet(CredentialStore.Keys.InstallationPublicKey, out var existingPublicHex) && !string.IsNullOrEmpty(existingPublicHex))
            {
                return existingPublicHex!;
            }

            var algorithm = SignatureAlgorithm.Ed25519;
            using var key = Key.Create(algorithm, new KeyCreationParameters { ExportPolicy = KeyExportPolicies.AllowPlaintextExport });

            var privateSeed = key.Export(KeyBlobFormat.RawPrivateKey);
            var publicKey = key.PublicKey.Export(KeyBlobFormat.RawPublicKey);

            var privateHex = ToLowerHex(privateSeed);
            var publicHex = ToLowerHex(publicKey);

            // Private key first: if the process dies between these two
            // writes, the next launch finds a private key with no cached
            // public key and simply re-derives + rewrites the public key
            // below, rather than silently generating a second, orphaned
            // keypair.
            _credentialStore.Put(CredentialStore.Keys.InstallationPrivateKey, privateHex);
            _credentialStore.Put(CredentialStore.Keys.InstallationPublicKey, publicHex);
            return publicHex;
        }

        private Key LoadInstallationSigningKey()
        {
            if (!_credentialStore.TryGet(CredentialStore.Keys.InstallationPrivateKey, out var privateHex) || string.IsNullOrEmpty(privateHex))
            {
                throw new InvalidOperationException("No installation private key stored - call EnsureInstallationKey() first.");
            }
            var seed = Convert.FromHexString(privateHex!);
            return Key.Import(SignatureAlgorithm.Ed25519, seed, KeyBlobFormat.RawPrivateKey,
                new KeyCreationParameters { ExportPolicy = KeyExportPolicies.AllowPlaintextExport });
        }

        /// <summary>`application.bind` (`api/installation.rs::bind`) - callable without a session. Idempotent: rebinding the same already-bound key succeeds; a different key already bound to this applicationId is refused (`PERMISSION_DENIED`, surfaces as "registration rejected").</summary>
        public async Task BindApplicationAsync(string firebaseIdToken, CancellationToken ct = default)
        {
            var publicKeyHex = EnsureInstallationKey();
            await SendRequestAsync("application.bind", new
            {
                applicationId = _applicationId,
                publicKey = publicKeyHex,
                firebaseIdToken,
            }, sessionToken: null, ct).ConfigureAwait(false);
        }

        /// <summary>`session.challenge` + `session.establish` (`api/installation.rs`). Signs the challenge nonce with the installation's own private key over the canonical payload `"runtime-session-challenge.v1." + nonce` (`installation.rs::challenge_signing_payload`). On success, the session token is held in memory for subsequent authenticated calls (e.g. `device.register`) - it is never persisted; a fresh session is established on every launch.</summary>
        public async Task EstablishSessionAsync(CancellationToken ct = default)
        {
            var challengeResult = await SendRequestAsync("session.challenge", new { applicationId = _applicationId }, sessionToken: null, ct)
                .ConfigureAwait(false);
            var nonce = challengeResult.GetProperty("nonce").GetString()
                ?? throw new RuntimeUnavailableException("session.challenge response was missing 'nonce'.");

            using var signingKey = LoadInstallationSigningKey();
            var payload = Encoding.UTF8.GetBytes("runtime-session-challenge.v1." + nonce);
            var signature = SignatureAlgorithm.Ed25519.Sign(signingKey, payload);
            var signatureHex = ToLowerHex(signature);

            var establishResult = await SendRequestAsync("session.establish", new
            {
                applicationId = _applicationId,
                nonce,
                signature = signatureHex,
            }, sessionToken: null, ct).ConfigureAwait(false);

            _sessionToken = establishResult.GetProperty("sessionToken").GetString()
                ?? throw new RuntimeUnavailableException("session.establish response was missing 'sessionToken'.");
        }

        /// <summary>
        /// `device.register` (`api/device_registration.rs`) - requires an
        /// already-established session (<see cref="EstablishSessionAsync"/>
        /// must be called first). Passes the caller's freshly refreshed
        /// Firebase ID token as the sole RPC param; CORE relays it to RTAPI
        /// as part of the node registration ceremony and never persists the
        /// token itself. This call never sees or generates the node's own
        /// keypair - only relays the outcome fields CORE returns.
        /// </summary>
        public async Task<DeviceRegistrationOutcome> RegisterDeviceAsync(string firebaseIdToken, CancellationToken ct = default)
        {
            if (_sessionToken is null)
            {
                throw new InvalidOperationException("No established session - call EstablishSessionAsync() first.");
            }

            var result = await SendRequestAsync("device.register", new { firebaseIdToken }, _sessionToken, ct).ConfigureAwait(false);
            return new DeviceRegistrationOutcome(
                NodeId: result.GetProperty("nodeId").GetString() ?? "",
                DeviceId: result.GetProperty("deviceId").GetString() ?? "",
                OrganizationId: result.GetProperty("organizationId").GetString() ?? "",
                State: result.GetProperty("state").GetString() ?? "",
                IssuedAt: result.GetProperty("issuedAt").GetString() ?? "",
                ExpiresAt: result.GetProperty("expiresAt").GetString() ?? "");
        }

        /// <summary>Full first-launch/every-launch ceremony: bind (idempotent), challenge, establish, then register the device against RTAPI through CORE. Convenience wrapper around the four calls above for <c>SignInPage</c>.</summary>
        public async Task<DeviceRegistrationOutcome> SignInAndRegisterAsync(string freshFirebaseIdToken, CancellationToken ct = default)
        {
            await BindApplicationAsync(freshFirebaseIdToken, ct).ConfigureAwait(false);
            await EstablishSessionAsync(ct).ConfigureAwait(false);
            return await RegisterDeviceAsync(freshFirebaseIdToken, ct).ConfigureAwait(false);
        }

        private async Task<JsonElement> SendRequestAsync(string method, object? paramsObj, string? sessionToken, CancellationToken ct)
        {
            if (_writer is null || _reader is null)
            {
                throw new RuntimeUnavailableException("Not connected to the Runtime Core - call ConnectAsync() first.");
            }

            var envelope = new RequestEnvelope
            {
                ProtocolVersion = new ProtocolVersionDto { Major = 0, Minor = 6 },
                RequestId = Guid.NewGuid().ToString("n"),
                SessionToken = sessionToken,
                Method = method,
                Params = paramsObj is null ? JsonSerializer.SerializeToElement((object?)null) : JsonSerializer.SerializeToElement(paramsObj, JsonOptions),
            };

            string requestLine = JsonSerializer.Serialize(envelope, JsonOptions);

            try
            {
                await _writer.WriteLineAsync(requestLine.AsMemory(), ct).ConfigureAwait(false);
                await _writer.FlushAsync(ct).ConfigureAwait(false);

                var responseLine = await _reader.ReadLineAsync(ct).ConfigureAwait(false)
                    ?? throw new RuntimeUnavailableException("The Runtime Core closed the connection without a response.");

                var response = JsonSerializer.Deserialize<ResponseEnvelope>(responseLine, JsonOptions)
                    ?? throw new RuntimeUnavailableException("The Runtime Core returned an unparseable response.");

                if (response.Error is not null)
                {
                    throw new RuntimeRpcException(response.Error.Code, response.Error.Message);
                }

                return response.Result ?? JsonSerializer.SerializeToElement((object?)null);
            }
            catch (IOException ex)
            {
                throw new RuntimeUnavailableException("Lost connection to the Runtime Core mid-request.", ex);
            }
        }

        public void Dispose()
        {
            _writer?.Dispose();
            _reader?.Dispose();
            _pipe?.Dispose();
        }

        private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

        /// <summary>net8.0 has <c>Convert.ToHexString</c> (uppercase) but not <c>Convert.ToHexStringLower</c> (.NET 9+); every hex field in CORE's wire contract must be lowercase, so this normalizes explicitly rather than relying on a newer API this target framework does not have.</summary>
        private static string ToLowerHex(byte[] bytes) => Convert.ToHexString(bytes).ToLowerInvariant();

        // Wire shapes mirroring `api/framing.rs` exactly (camelCase on the
        // wire; `RequestEnvelope`'s `params` defaults to `null`/absent when
        // not supplied, matching `#[serde(default)]` there).
        private sealed class RequestEnvelope
        {
            [JsonPropertyName("protocolVersion")] public required ProtocolVersionDto ProtocolVersion { get; init; }
            [JsonPropertyName("requestId")] public required string RequestId { get; init; }
            [JsonPropertyName("sessionToken")] public string? SessionToken { get; init; }
            [JsonPropertyName("method")] public required string Method { get; init; }
            [JsonPropertyName("params")] public JsonElement Params { get; init; }
        }

        private sealed class ProtocolVersionDto
        {
            [JsonPropertyName("major")] public required int Major { get; init; }
            [JsonPropertyName("minor")] public required int Minor { get; init; }
        }

        private sealed class ResponseEnvelope
        {
            [JsonPropertyName("requestId")] public string RequestId { get; init; } = "";
            [JsonPropertyName("result")] public JsonElement? Result { get; init; }
            [JsonPropertyName("error")] public RuntimeErrorDto? Error { get; init; }
        }

        private sealed class RuntimeErrorDto
        {
            [JsonPropertyName("code")] public string Code { get; init; } = "UNKNOWN";
            [JsonPropertyName("message")] public string Message { get; init; } = "";
        }
    }
}
