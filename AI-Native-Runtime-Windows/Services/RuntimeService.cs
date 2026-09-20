using System.Text;
using System.Text.Json;
using AI_Native_Runtime_Windows.Models;
using AI_Native_Runtime_Windows.Services.Transport;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSec.Cryptography;

namespace AI_Native_Runtime_Windows.Services
{
    public sealed record DeviceRegistrationOutcome(
        string NodeId,
        string DeviceId,
        string OrganizationId,
        string State,
        string IssuedAt,
        string ExpiresAt);

    /// <summary>
    /// The single service every feature surface goes through to reach
    /// CORE — owns the two-connection transport
    /// (<see cref="RpcConnection"/> + <see cref="EventStreamConnection"/>,
    /// `desktop-shell-conventions.md` §2), the installation ceremony
    /// (`application.bind` → `session.challenge` → `session.establish` →
    /// `device.register`), and every RPC method call this application
    /// makes. Supersedes the Foundation-wave `RuntimeClient` draft, which
    /// opened one ad hoc connection per call and had no event-stream
    /// support at all.
    ///
    /// <para>
    /// <b>Two separate keypairs — do not conflate them.</b> This class
    /// generates and holds the APPLICATION's installation Ed25519 keypair
    /// (this process, stored via <see cref="CredentialStore"/>) and uses
    /// it only to prove possession during
    /// `application.bind`/`session.challenge`/`session.establish`. The
    /// NODE's own Ed25519 keypair is generated and held entirely by CORE
    /// — this class never generates, sees, or stores it.
    /// </para>
    /// </summary>
    public sealed class RuntimeService : IAsyncDisposable
    {
        private readonly CredentialStore _credentialStore;
        private readonly string _applicationId;
        private string? _sessionToken;

        public RpcConnection Rpc { get; }
        public EventStreamConnection Events { get; }

        public bool HasSession => _sessionToken is not null;

        public RuntimeService(
            IOptions<RuntimeOptions> options,
            CredentialStore credentialStore,
            CursorStore cursorStore,
            ILoggerFactory loggerFactory)
        {
            _credentialStore = credentialStore;
            _applicationId = options.Value.ApplicationId;
            Rpc = new RpcConnection(options.Value.PipeName, () => _sessionToken, loggerFactory.CreateLogger<RpcConnection>());
            Events = new EventStreamConnection(options.Value.PipeName, () => _sessionToken, cursorStore, loggerFactory.CreateLogger<EventStreamConnection>());
        }

        /// <summary>Opens the RPC connection. The event-stream connection is started
        /// separately, once a session exists (`events.subscribe` requires one, §4.6).</summary>
        public Task StartAsync(CancellationToken ct = default) => Rpc.StartAsync(ct);

        public void StartEventStream(CancellationToken ct = default) => Events.Start(ct);

        // ---- Installation ceremony -------------------------------------------------

        /// <summary>Ensures this installation has its own Ed25519 keypair, generating
        /// and persisting one via <see cref="CredentialStore"/> on first use. Idempotent.</summary>
        public string EnsureInstallationKey()
        {
            if (_credentialStore.TryGet(CredentialStore.Keys.InstallationPublicKey, out var existingPublicHex) && !string.IsNullOrEmpty(existingPublicHex))
            {
                return existingPublicHex!;
            }

            using var key = Key.Create(SignatureAlgorithm.Ed25519, new KeyCreationParameters { ExportPolicy = KeyExportPolicies.AllowPlaintextExport });
            var privateHex = ToLowerHex(key.Export(KeyBlobFormat.RawPrivateKey));
            var publicHex = ToLowerHex(key.PublicKey.Export(KeyBlobFormat.RawPublicKey));

            // Private key first: if the process dies between these two writes, the next
            // launch finds a private key with no cached public key and simply re-derives
            // + rewrites the public key, rather than generating a second, orphaned keypair.
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

        /// <summary>`application.bind` - callable without a session. Idempotent.</summary>
        public async Task BindApplicationAsync(string firebaseIdToken, CancellationToken ct = default)
        {
            var publicKeyHex = EnsureInstallationKey();
            await Rpc.SendAsync("application.bind", new
            {
                applicationId = _applicationId,
                publicKey = publicKeyHex,
                firebaseIdToken,
            }, ct).ConfigureAwait(false);
        }

        /// <summary>`session.challenge` + `session.establish`. On success, the session
        /// token is held in memory only - never persisted; a fresh session is
        /// established on every launch.</summary>
        public async Task EstablishSessionAsync(CancellationToken ct = default)
        {
            var challengeResult = await Rpc.SendAsync("session.challenge", new { applicationId = _applicationId }, ct).ConfigureAwait(false);
            var nonce = challengeResult.GetProperty("nonce").GetString()
                ?? throw new RuntimeUnavailableException("session.challenge response was missing 'nonce'.");

            using var signingKey = LoadInstallationSigningKey();
            var payload = Encoding.UTF8.GetBytes("runtime-session-challenge.v1." + nonce);
            var signatureHex = ToLowerHex(SignatureAlgorithm.Ed25519.Sign(signingKey, payload));

            var establishResult = await Rpc.SendAsync("session.establish", new
            {
                applicationId = _applicationId,
                nonce,
                signature = signatureHex,
            }, ct).ConfigureAwait(false);

            _sessionToken = establishResult.GetProperty("sessionToken").GetString()
                ?? throw new RuntimeUnavailableException("session.establish response was missing 'sessionToken'.");
        }

        /// <summary>`device.register` - requires an already-established session.</summary>
        public async Task<DeviceRegistrationOutcome> RegisterDeviceAsync(string firebaseIdToken, CancellationToken ct = default)
        {
            if (_sessionToken is null)
            {
                throw new InvalidOperationException("No established session - call EstablishSessionAsync() first.");
            }
            var result = await Rpc.SendAsync("device.register", new { firebaseIdToken }, ct).ConfigureAwait(false);
            return new DeviceRegistrationOutcome(
                NodeId: result.GetProperty("nodeId").GetString() ?? "",
                DeviceId: result.GetProperty("deviceId").GetString() ?? "",
                OrganizationId: result.GetProperty("organizationId").GetString() ?? "",
                State: result.GetProperty("state").GetString() ?? "",
                IssuedAt: result.GetProperty("issuedAt").GetString() ?? "",
                ExpiresAt: result.GetProperty("expiresAt").GetString() ?? "");
        }

        /// <summary>Full first-launch/every-launch ceremony: bind, establish, register.</summary>
        public async Task<DeviceRegistrationOutcome> SignInAndRegisterAsync(string freshFirebaseIdToken, CancellationToken ct = default)
        {
            await BindApplicationAsync(freshFirebaseIdToken, ct).ConfigureAwait(false);
            await EstablishSessionAsync(ct).ConfigureAwait(false);
            return await RegisterDeviceAsync(freshFirebaseIdToken, ct).ConfigureAwait(false);
        }

        // ---- Status / identity ------------------------------------------------------

        public Task<JsonElement> GetStatusAsync(CancellationToken ct = default) => Rpc.SendAsync("runtime.status", null, ct);
        public Task<JsonElement> GetHealthAsync(CancellationToken ct = default) => Rpc.SendAsync("runtime.health", null, ct);
        public Task<JsonElement> GetNodeAsync(CancellationToken ct = default) => Rpc.SendAsync("node.get", null, ct);
        public Task<JsonElement> GetConnectionStatusAsync(CancellationToken ct = default) => Rpc.SendAsync("node.connection", null, ct);
        public Task<JsonElement> GetLogsAsync(object? paramsObj = null, CancellationToken ct = default) => Rpc.SendAsync("runtime.logs", paramsObj, ct);

        // ---- Capabilities / worker ---------------------------------------------------

        public Task<JsonElement> ListCapabilitiesAsync(CancellationToken ct = default) => Rpc.SendAsync("capability.list", null, ct);
        public Task<JsonElement> GetWorkerStatusAsync(CancellationToken ct = default) => Rpc.SendAsync("worker.status", null, ct);

        // ---- Execution ---------------------------------------------------------------

        public Task<JsonElement> ListExecutionsAsync(CancellationToken ct = default) => Rpc.SendAsync("execution.list", null, ct);
        public Task<JsonElement> GetExecutionAsync(string executionId, CancellationToken ct = default) =>
            Rpc.SendAsync("execution.get", new { executionId }, ct);
        public Task<JsonElement> RequestExecutionAsync(object paramsObj, CancellationToken ct = default) =>
            Rpc.SendAsync("execution.request", paramsObj, ct);
        public Task<JsonElement> CancelExecutionAsync(string executionId, CancellationToken ct = default) =>
            Rpc.SendAsync("execution.cancel", new { executionId }, ct);

        // ---- Permission grants (INV-02 plan §4.8/§10, Checkpoint INV-02C) ---------------

        /// <summary>`permission.list` — omitting `applicationId` self-scopes to this
        /// installation's own application (§4.8 rule 1's "self-service introspection"
        /// case). This desktop shell has no "all applications" surface yet
        /// (`Applications` is still `NotYetAvailablePage`, plan §4.18), so there is no
        /// existing notion of "all locally-known applications" to reuse here.</summary>
        public Task<JsonElement> ListPermissionGrantsAsync(CancellationToken ct = default) =>
            Rpc.SendAsync("permission.list", null, ct);

        /// <summary>`permission.revoke` — CORE returns the same non-disclosing "not
        /// found" rejection whether the grant genuinely doesn't exist or is
        /// `source = org_admin` (§4.8 rule 5); that is expected, not a bug.</summary>
        public Task<JsonElement> RevokePermissionGrantAsync(string grantId, CancellationToken ct = default) =>
            Rpc.SendAsync("permission.revoke", new { grantId }, ct);

        // ---- Approvals -----------------------------------------------------------------

        public Task<JsonElement> ListApprovalsAsync(CancellationToken ct = default) => Rpc.SendAsync("approval.list", null, ct);

        /// <summary>`approval.decide` - `allow: true` is "Allow once," `allow: false` is
        /// "Deny." There is no session/persistent-allow parameter to pass (`RT-HITL-003`/
        /// `004` are out of scope, plan §8) - every decision is allow-once or deny.</summary>
        public Task<JsonElement> DecideApprovalAsync(string approvalId, bool allow, CancellationToken ct = default) =>
            Rpc.SendAsync("approval.decide", new { approvalId, allow }, ct);

        // ---- Emergency controls (FD-Wave 08 Checkpoint F8A, §4.2) -----------------------

        public Task<JsonElement> PauseRuntimeAsync(CancellationToken ct = default) => Rpc.SendAsync("runtime.pause", null, ct);
        public Task<JsonElement> ResumeRuntimeAsync(CancellationToken ct = default) => Rpc.SendAsync("runtime.resume", null, ct);
        public Task<JsonElement> StopRuntimeAsync(CancellationToken ct = default) => Rpc.SendAsync("runtime.stop", null, ct);
        public Task<JsonElement> DisconnectCloudAsync(CancellationToken ct = default) => Rpc.SendAsync("cloud.disconnect", null, ct);
        public Task<JsonElement> ConnectCloudAsync(CancellationToken ct = default) => Rpc.SendAsync("cloud.connect", null, ct);

        private static string ToLowerHex(byte[] bytes) => Convert.ToHexString(bytes).ToLowerInvariant();

        public async ValueTask DisposeAsync()
        {
            await Rpc.DisposeAsync().ConfigureAwait(false);
            await Events.DisposeAsync().ConfigureAwait(false);
        }
    }
}
