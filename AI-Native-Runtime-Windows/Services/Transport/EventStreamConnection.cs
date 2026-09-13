using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using AI_Native_Runtime_Windows.Models.Protocol;
using Microsoft.Extensions.Logging;

namespace AI_Native_Runtime_Windows.Services.Transport
{
    /// <summary>
    /// The event-stream half of the two-connection model
    /// (`desktop-shell-conventions.md` §2). Owns its own pipe handle,
    /// entirely separate from <see cref="RpcConnection"/> — once this
    /// connection sends `events.subscribe`, CORE's connection loop
    /// switches it into streaming-only mode for the rest of its life, so
    /// this class never sends another RPC on the same handle.
    ///
    /// Implements §4's cursor persistence and retention recovery: a fresh
    /// subscription passes <c>fromNow: true</c> rather than an
    /// omitted/zero cursor (which would deterministically return
    /// `CURSOR_OUT_OF_RETENTION` against an already-pruned, long-running
    /// daemon); on `CURSOR_OUT_OF_RETENTION` the stored cursor is cleared,
    /// the connection resubscribes with `fromNow: true`, and
    /// <see cref="NeedsFullReload"/> is raised so every feature surface
    /// holding incrementally-synced state reloads from its own list/get
    /// RPC rather than trusting stale in-memory state; on `STREAM_LAGGED`
    /// the connection reconnects and resumes from the **last acknowledged
    /// cursor** (never cleared) — the durable event record is unaffected
    /// by a lag, so a resume from the same cursor replays gap-free.
    /// </summary>
    public sealed class EventStreamConnection : IAsyncDisposable
    {
        private static readonly TimeSpan InitialBackoff = TimeSpan.FromMilliseconds(500);
        private static readonly TimeSpan MaxBackoff = TimeSpan.FromSeconds(8);
        private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(3);

        private readonly string _pipeName;
        private readonly Func<string?> _sessionTokenProvider;
        private readonly CursorStore _cursorStore;
        private readonly ILogger _logger;

        private CancellationTokenSource? _lifetimeCts;
        private Task? _runLoop;

        /// <summary>Raised for every event pushed by CORE (the raw `event` JSON element).</summary>
        public event Action<JsonElement>? EventReceived;

        /// <summary>Raised on a forced `CURSOR_OUT_OF_RETENTION` resume — every feature
        /// surface with incrementally-synced state must fully reload from its list/get RPC.</summary>
        public event Action? NeedsFullReload;

        public event Action<bool>? ConnectionStateChanged;

        public bool IsConnected { get; private set; }

        public EventStreamConnection(string pipeName, Func<string?> sessionTokenProvider, CursorStore cursorStore, ILogger logger)
        {
            _pipeName = pipeName;
            _sessionTokenProvider = sessionTokenProvider;
            _cursorStore = cursorStore;
            _logger = logger;
        }

        public void Start(CancellationToken ct = default)
        {
            _lifetimeCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            _runLoop = Task.Run(() => RunAsync(_lifetimeCts.Token), _lifetimeCts.Token);
        }

        private async Task RunAsync(CancellationToken ct)
        {
            var backoff = InitialBackoff;
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    await ConnectAndStreamAsync(ct).ConfigureAwait(false);
                    backoff = InitialBackoff; // a clean disconnect resets backoff
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Event stream connection failed; reconnecting");
                }
                finally
                {
                    if (IsConnected)
                    {
                        IsConnected = false;
                        ConnectionStateChanged?.Invoke(false);
                    }
                }

                if (ct.IsCancellationRequested) return;
                try
                {
                    await Task.Delay(backoff, ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                backoff = TimeSpan.FromTicks(Math.Min(backoff.Ticks * 2, MaxBackoff.Ticks));
            }
        }

        private async Task ConnectAndStreamAsync(CancellationToken ct)
        {
            var pipe = new NamedPipeClientStream(".", _pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(ConnectTimeout);
            await using var _ = pipe.ConfigureAwait(false);
            await pipe.ConnectAsync(timeoutCts.Token).ConfigureAwait(false);

            await using var writer = new StreamWriter(pipe, new UTF8Encoding(false)) { AutoFlush = false, NewLine = "\n" };
            using var reader = new StreamReader(pipe, Encoding.UTF8);

            var stored = _cursorStore.Load();
            var requestId = Guid.NewGuid().ToString("n");
            object subscribeParams = stored is null
                ? new { fromNow = true }
                : new { cursor = stored.Sequence };

            var envelope = new RequestEnvelope
            {
                ProtocolVersion = ProtocolVersionDto.Current,
                RequestId = requestId,
                SessionToken = _sessionTokenProvider(),
                Method = "events.subscribe",
                Params = JsonSerializer.SerializeToElement(subscribeParams, ProtocolJson.Options),
            };
            await writer.WriteLineAsync(JsonSerializer.Serialize(envelope, ProtocolJson.Options).AsMemory(), ct).ConfigureAwait(false);
            await writer.FlushAsync(ct).ConfigureAwait(false);

            IsConnected = true;
            ConnectionStateChanged?.Invoke(true);

            while (!ct.IsCancellationRequested)
            {
                var line = await reader.ReadLineAsync(ct).ConfigureAwait(false);
                if (line is null) return; // daemon closed the connection — reconnect from caller's loop

                using var doc = JsonDocument.Parse(line);
                var root = doc.RootElement;

                if (root.TryGetProperty("error", out var errorElement) && errorElement.ValueKind != JsonValueKind.Null)
                {
                    var code = errorElement.TryGetProperty("code", out var c) ? c.GetString() : null;
                    if (code == "CURSOR_OUT_OF_RETENTION")
                    {
                        _logger.LogInformation("Event cursor out of retention — clearing and resubscribing from now");
                        _cursorStore.Clear();
                        NeedsFullReload?.Invoke();
                        return; // caller reconnects; Load() will find no cursor and pass fromNow: true
                    }
                    if (code == "STREAM_LAGGED")
                    {
                        _logger.LogInformation("Event stream lagged — reconnecting from last acknowledged cursor");
                        return; // caller reconnects; the stored cursor is NOT cleared
                    }
                    // Any other rejection (e.g. UNAUTHENTICATED) — let the caller's
                    // backoff loop retry; the session-token provider may resolve a
                    // fresh token by the next attempt.
                    _logger.LogWarning("events.subscribe rejected: {Code}", code);
                    return;
                }

                if (root.TryGetProperty("event", out var eventElement))
                {
                    if (eventElement.TryGetProperty("sequence", out var seqElement) && seqElement.TryGetInt64(out var sequence))
                    {
                        _cursorStore.Save(new EventCursor("default", sequence));
                    }
                    EventReceived?.Invoke(eventElement.Clone());
                }
            }
        }

        public async ValueTask DisposeAsync()
        {
            _lifetimeCts?.Cancel();
            if (_runLoop is not null)
            {
                try { await _runLoop.ConfigureAwait(false); } catch { /* expected on cancel */ }
            }
        }
    }
}
