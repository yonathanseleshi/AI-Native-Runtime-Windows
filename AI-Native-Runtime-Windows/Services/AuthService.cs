using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace AI_Native_Runtime_Windows.Services
{
    /// <summary>Thrown when the substrate (Firebase, or - by extension - RTAPI behind it) cannot be reached at all: DNS/connect/timeout failures. Distinct from a rejected sign-in (bad password), which is a normal, reachable response from Firebase.</summary>
    public sealed class SubstrateOfflineException : Exception
    {
        public SubstrateOfflineException(string message, Exception? inner = null) : base(message, inner) { }
    }

    /// <summary>Thrown when Firebase reached and responded, but refused the credentials (wrong password, unknown account, disabled user, etc).</summary>
    public sealed class SignInRejectedException : Exception
    {
        public string FirebaseErrorCode { get; }
        public SignInRejectedException(string firebaseErrorCode, string message) : base(message)
        {
            FirebaseErrorCode = firebaseErrorCode;
        }
    }

    public sealed record FirebaseSession(string IdToken, string RefreshToken, DateTimeOffset ExpiresAtUtc, string LocalId);

    /// <summary>
    /// Firebase email/password sign-in via the Firebase Auth REST API,
    /// called directly over <see cref="HttpClient"/> - deliberately not
    /// through a Firebase SDK, since there is no first-party .NET Firebase
    /// SDK (the community ones wrap the same REST surface with extra
    /// dependency weight this project does not need for one sign-in
    /// screen).
    ///
    /// Email/password only. SUBAPI - the reference product this desktop
    /// client authenticates against - has no Google/social sign-in wired
    /// up anywhere; there is deliberately no Google button here.
    ///
    /// <para>
    /// <b>Scope boundary (mirrors MAC's <c>AuthService.swift</c>):</b> this
    /// class knows nothing about the local CORE daemon, the named pipe, the
    /// application's installation keypair, or any node/control-channel
    /// state. It authenticates a human against Firebase and stores/clears
    /// that outcome, full stop. <see cref="RuntimeClient"/> is the only
    /// class that reads a fresh <see cref="FirebaseSession.IdToken"/> from
    /// here and hands it to CORE - <see cref="AuthService"/> never reaches
    /// into <see cref="RuntimeClient"/> or vice versa. In particular,
    /// <see cref="SignOutAsync"/> only discards the locally stored Firebase
    /// token; it does not, and must not, touch any node/control-channel
    /// state.
    /// </para>
    /// </summary>
    public sealed class AuthService
    {
        private readonly HttpClient _http;
        private readonly CredentialStore _credentialStore;
        private readonly string _apiKey;

        private FirebaseSession? _session;
        private readonly SemaphoreSlim _refreshLock = new(1, 1);

        /// <summary>Refresh proactively once less than this much validity remains, rather than waiting for outright expiry.</summary>
        private static readonly TimeSpan RefreshMargin = TimeSpan.FromMinutes(5);

        public AuthService(HttpClient http, CredentialStore credentialStore, string firebaseApiKey)
        {
            _http = http;
            _credentialStore = credentialStore;
            if (string.IsNullOrWhiteSpace(firebaseApiKey))
            {
                throw new InvalidOperationException(
                    "Firebase:ApiKey is not configured. Set it in appsettings.json before signing in - see README.md 'Configuration'.");
            }
            _apiKey = firebaseApiKey;
        }

        public bool IsSignedIn => _session is not null;

        /// <summary>Restores a previously stored session (if any) from Windows Credential Manager, without contacting Firebase. Callers should still call <see cref="GetFreshIdTokenAsync"/> before using the token, since it may need a proactive refresh.</summary>
        public bool TryRestoreSession()
        {
            if (!_credentialStore.TryGet(CredentialStore.Keys.FirebaseCredential, out var json) || json is null)
            {
                return false;
            }

            try
            {
                var stored = JsonSerializer.Deserialize<StoredCredential>(json);
                if (stored is null) return false;
                _session = new FirebaseSession(stored.IdToken, stored.RefreshToken, stored.ExpiresAtUtc, stored.LocalId);
                return true;
            }
            catch (JsonException)
            {
                // Corrupt/unreadable entry - treat as "no session", never throw out of a restore path.
                return false;
            }
        }

        /// <summary>
        /// POST https://identitytoolkit.googleapis.com/v1/accounts:signInWithPassword
        /// </summary>
        public async Task<FirebaseSession> SignInAsync(string email, string password, CancellationToken ct = default)
        {
            var url = $"https://identitytoolkit.googleapis.com/v1/accounts:signInWithPassword?key={Uri.EscapeDataString(_apiKey)}";
            var body = new SignInRequest(email, password, true);

            HttpResponseMessage response;
            try
            {
                response = await _http.PostAsJsonAsync(url, body, JsonOptions, ct).ConfigureAwait(false);
            }
            catch (HttpRequestException ex)
            {
                throw new SubstrateOfflineException("Could not reach Firebase (network error).", ex);
            }
            catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
            {
                throw new SubstrateOfflineException("Firebase sign-in timed out.", ex);
            }

            if (!response.IsSuccessStatusCode)
            {
                var errorCode = await ExtractFirebaseErrorCodeAsync(response, ct).ConfigureAwait(false);
                throw new SignInRejectedException(errorCode, $"Firebase sign-in was rejected ({errorCode}).");
            }

            var payload = await response.Content.ReadFromJsonAsync<SignInResponse>(JsonOptions, ct).ConfigureAwait(false)
                ?? throw new SubstrateOfflineException("Firebase returned an empty sign-in response.");

            var session = new FirebaseSession(
                payload.IdToken,
                payload.RefreshToken,
                DateTimeOffset.UtcNow.AddSeconds(double.Parse(payload.ExpiresIn)),
                payload.LocalId);

            _session = session;
            Persist(session);
            return session;
        }

        /// <summary>
        /// Explicit refresh-token exchange:
        /// POST https://securetoken.googleapis.com/v1/token
        /// Called proactively (before the current token is within
        /// <see cref="RefreshMargin"/> of expiry) rather than reactively,
        /// since there is no SDK doing this implicitly for us.
        /// </summary>
        public async Task<FirebaseSession> RefreshAsync(CancellationToken ct = default)
        {
            await _refreshLock.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                var current = _session ?? throw new InvalidOperationException("No session to refresh - sign in first.");

                var url = $"https://securetoken.googleapis.com/v1/token?key={Uri.EscapeDataString(_apiKey)}";
                var form = new FormUrlEncodedContent(new[]
                {
                    new KeyValuePair<string, string>("grant_type", "refresh_token"),
                    new KeyValuePair<string, string>("refresh_token", current.RefreshToken),
                });

                HttpResponseMessage response;
                try
                {
                    response = await _http.PostAsync(url, form, ct).ConfigureAwait(false);
                }
                catch (HttpRequestException ex)
                {
                    throw new SubstrateOfflineException("Could not reach Firebase to refresh the session (network error).", ex);
                }

                if (!response.IsSuccessStatusCode)
                {
                    var errorCode = await ExtractFirebaseErrorCodeAsync(response, ct).ConfigureAwait(false);
                    // A refresh token can be rejected (revoked/expired) just like a
                    // password can - surface it the same way so callers route back
                    // to the sign-in screen rather than treating it as a network
                    // outage.
                    throw new SignInRejectedException(errorCode, $"Firebase session refresh was rejected ({errorCode}).");
                }

                var payload = await response.Content.ReadFromJsonAsync<RefreshResponse>(cancellationToken: ct).ConfigureAwait(false)
                    ?? throw new SubstrateOfflineException("Firebase returned an empty refresh response.");

                var session = new FirebaseSession(
                    payload.IdToken,
                    payload.RefreshToken,
                    DateTimeOffset.UtcNow.AddSeconds(double.Parse(payload.ExpiresIn)),
                    payload.UserId);

                _session = session;
                Persist(session);
                return session;
            }
            finally
            {
                _refreshLock.Release();
            }
        }

        /// <summary>Returns a Firebase ID token guaranteed to be valid for at least <see cref="RefreshMargin"/> longer, refreshing first if needed. This is what <see cref="RuntimeClient"/> should call immediately before <c>application.bind</c>/<c>device.register</c> - both CORE and RTAPI require a "freshly refreshed token" (plan §4.31).</summary>
        public async Task<string> GetFreshIdTokenAsync(CancellationToken ct = default)
        {
            var current = _session ?? throw new InvalidOperationException("No session - sign in first.");
            if (current.ExpiresAtUtc - DateTimeOffset.UtcNow > RefreshMargin)
            {
                return current.IdToken;
            }
            var refreshed = await RefreshAsync(ct).ConfigureAwait(false);
            return refreshed.IdToken;
        }

        /// <summary>
        /// Discards the locally stored Firebase credential. Firebase's REST
        /// surface has no real server-side "invalidate this ID token" call
        /// (that is normal for short-lived JWTs, not a gap here) - signing
        /// out is simulated by dropping local state, which is sufficient
        /// since nothing else on this machine will present the token again.
        ///
        /// Deliberately touches nothing about the node or control-channel:
        /// no pipe call, no session-token invalidation against CORE. That
        /// boundary is intentional (plan §4.23) and mirrors MAC's
        /// <c>AuthService.swift</c>.
        /// </summary>
        public void SignOut()
        {
            _session = null;
            _credentialStore.Delete(CredentialStore.Keys.FirebaseCredential);
        }

        private void Persist(FirebaseSession session)
        {
            var stored = new StoredCredential(session.IdToken, session.RefreshToken, session.ExpiresAtUtc, session.LocalId);
            _credentialStore.Put(CredentialStore.Keys.FirebaseCredential, JsonSerializer.Serialize(stored));
        }

        private static async Task<string> ExtractFirebaseErrorCodeAsync(HttpResponseMessage response, CancellationToken ct)
        {
            try
            {
                var errorBody = await response.Content.ReadFromJsonAsync<FirebaseErrorEnvelope>(cancellationToken: ct).ConfigureAwait(false);
                return errorBody?.Error?.Message ?? $"HTTP_{(int)response.StatusCode}";
            }
            catch
            {
                return $"HTTP_{(int)response.StatusCode}";
            }
        }

        private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

        private sealed record SignInRequest(
            [property: JsonPropertyName("email")] string Email,
            [property: JsonPropertyName("password")] string Password,
            [property: JsonPropertyName("returnSecureToken")] bool ReturnSecureToken);

        private sealed class SignInResponse
        {
            [JsonPropertyName("idToken")] public string IdToken { get; set; } = "";
            [JsonPropertyName("refreshToken")] public string RefreshToken { get; set; } = "";
            [JsonPropertyName("expiresIn")] public string ExpiresIn { get; set; } = "3600";
            [JsonPropertyName("localId")] public string LocalId { get; set; } = "";
        }

        private sealed class RefreshResponse
        {
            [JsonPropertyName("id_token")] public string IdToken { get; set; } = "";
            [JsonPropertyName("refresh_token")] public string RefreshToken { get; set; } = "";
            [JsonPropertyName("expires_in")] public string ExpiresIn { get; set; } = "3600";
            [JsonPropertyName("user_id")] public string UserId { get; set; } = "";
        }

        private sealed class FirebaseErrorEnvelope
        {
            [JsonPropertyName("error")] public FirebaseErrorBody? Error { get; set; }
        }

        private sealed class FirebaseErrorBody
        {
            [JsonPropertyName("message")] public string? Message { get; set; }
        }

        private sealed record StoredCredential(string IdToken, string RefreshToken, DateTimeOffset ExpiresAtUtc, string LocalId);
    }
}
