using System.Collections.Concurrent;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using AI_Native_Runtime_Windows.Models.Protocol;
using Microsoft.Extensions.Logging;

namespace AI_Native_Runtime_Windows.Services.Transport
{
    /// <summary>
    /// The RPC half of `desktop-shell-conventions.md`'s two-connection
    /// model (§2): sends any number of requests over its lifetime, each
    /// getting exactly one response line back, demultiplexed by
    /// `requestId` so a caller need not wait for one response before
    /// sending the next. **This connection must never call
    /// `events.subscribe`** — once a connection does that, CORE's own
    /// connection loop switches it into streaming-only mode for the rest
    /// of its life (verified against `transport/connection.rs`, cited in
    /// the conventions doc) — that is exactly what
    /// <see cref="EventStreamConnection"/> is for, on its own separate
    /// pipe handle.
    ///
    /// Reconnects with exponential backoff (0.5s doubling, capped at
    /// 8s — conventions §3) independently of the event-stream connection.
    /// A client is not required to wait for one response before sending
    /// the next request — concurrent in-flight requests are supported via
    /// <see cref="_pending"/>.
    /// </summary>
    public sealed class RpcConnection : IAsyncDisposable
    {
        private static readonly HashSet<string> PreSessionMethods = new()
        {
            "bootstrap.enroll", "application.bind", "session.challenge", "session.establish",
        };

        private static readonly TimeSpan InitialBackoff = TimeSpan.FromMilliseconds(500);
        private static readonly TimeSpan MaxBackoff = TimeSpan.FromSeconds(8);
        private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(3);

        private readonly string _pipeName;
        private readonly Func<string?> _sessionTokenProvider;
        private readonly ILogger _logger;

        private readonly SemaphoreSlim _connectLock = new(1, 1);
        private readonly SemaphoreSlim _writeLock = new(1, 1);
        private readonly ConcurrentDictionary<string, TaskCompletionSource<ResponseEnvelope>> _pending = new();

        private NamedPipeClientStream? _pipe;
        private StreamWriter? _writer;
        private CancellationTokenSource? _lifetimeCts;
        private Task? _readLoop;
        private volatile bool _connected;

        /// <summary>Raised whenever this connection's own up/down state changes — the
        /// RPC connection being down does not imply the event-stream connection is (§3).</summary>
        public event Action<bool>? ConnectionStateChanged;

        public bool IsConnected => _connected;

        public RpcConnection(string pipeName, Func<string?> sessionTokenProvider, ILogger logger)
        {
            _pipeName = pipeName;
            _sessionTokenProvider = sessionTokenProvider;
            _logger = logger;
        }

        /// <summary>Connects (or reuses an existing connection) and starts the background
        /// reconnect-with-backoff loop for the lifetime of this object.</summary>
        public async Task StartAsync(CancellationToken ct = default)
        {
            _lifetimeCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            await ConnectOnceAsync(_lifetimeCts.Token).ConfigureAwait(false);
        }

        public async Task<JsonElement> SendAsync(string method, object? paramsObj, CancellationToken ct = default)
        {
            if (!_connected)
            {
                throw new RuntimeUnavailableException($"Not connected to the Runtime Core on pipe '{_pipeName}'.");
            }

            var requestId = Guid.NewGuid().ToString("n");
            var paramsElement = paramsObj is null
                ? JsonSerializer.SerializeToElement(new object())
                : JsonSerializer.SerializeToElement(paramsObj, ProtocolJson.Options);

            var envelope = new RequestEnvelope
            {
                ProtocolVersion = ProtocolVersionDto.Current,
                RequestId = requestId,
                SessionToken = PreSessionMethods.Contains(method) ? null : _sessionTokenProvider(),
                Method = method,
                Params = paramsElement,
            };

            var tcs = new TaskCompletionSource<ResponseEnvelope>(TaskCreationOptions.RunContinuationsAsynchronously);
            _pending[requestId] = tcs;

            try
            {
                var line = JsonSerializer.Serialize(envelope, ProtocolJson.Options);
                await _writeLock.WaitAsync(ct).ConfigureAwait(false);
                try
                {
                    if (_writer is null)
                    {
                        throw new RuntimeUnavailableException($"Not connected to the Runtime Core on pipe '{_pipeName}'.");
                    }
                    await _writer.WriteLineAsync(line.AsMemory(), ct).ConfigureAwait(false);
                    await _writer.FlushAsync(ct).ConfigureAwait(false);
                }
                finally
                {
                    _writeLock.Release();
                }

                await using var reg = ct.Register(() => tcs.TrySetCanceled(ct));
                var response = await tcs.Task.ConfigureAwait(false);

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
            finally
            {
                _pending.TryRemove(requestId, out _);
            }
        }

        private async Task ConnectOnceAsync(CancellationToken ct)
        {
            await _connectLock.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                if (_connected) return;

                var pipe = new NamedPipeClientStream(".", _pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
                using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                timeoutCts.CancelAfter(ConnectTimeout);
                try
                {
                    await pipe.ConnectAsync(timeoutCts.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (!ct.IsCancellationRequested)
                {
                    pipe.Dispose();
                    ScheduleReconnect();
                    throw new RuntimeUnavailableException(
                        $"Timed out connecting to the Runtime Core on pipe '{_pipeName}'. Is the Core running?");
                }
                catch (Exception ex) when (ex is IOException or TimeoutException or UnauthorizedAccessException)
                {
                    pipe.Dispose();
                    ScheduleReconnect();
                    throw new RuntimeUnavailableException(
                        $"Could not connect to the Runtime Core on pipe '{_pipeName}'. Is the Core running?", ex);
                }

                _pipe = pipe;
                _writer = new StreamWriter(pipe, new UTF8Encoding(false)) { AutoFlush = false, NewLine = "\n" };
                var reader = new StreamReader(pipe, Encoding.UTF8);
                _connected = true;
                ConnectionStateChanged?.Invoke(true);
                _readLoop = Task.Run(() => ReadLoopAsync(reader, ct), ct);
            }
            finally
            {
                _connectLock.Release();
            }
        }

        private async Task ReadLoopAsync(StreamReader reader, CancellationToken ct)
        {
            try
            {
                while (!ct.IsCancellationRequested)
                {
                    var line = await reader.ReadLineAsync(ct).ConfigureAwait(false);
                    if (line is null) break; // daemon closed the connection

                    ResponseEnvelope? response;
                    try
                    {
                        response = JsonSerializer.Deserialize<ResponseEnvelope>(line, ProtocolJson.Options);
                    }
                    catch (JsonException ex)
                    {
                        _logger.LogWarning(ex, "Unparseable response line from Runtime Core RPC connection");
                        continue;
                    }
                    if (response is null) continue;

                    if (_pending.TryRemove(response.RequestId, out var tcs))
                    {
                        tcs.TrySetResult(response);
                    }
                }
            }
            catch (Exception ex) when (ex is IOException or ObjectDisposedException)
            {
                // Falls through to the disconnect handling below.
            }

            HandleDisconnect();
        }

        private void HandleDisconnect()
        {
            _connected = false;
            ConnectionStateChanged?.Invoke(false);
            foreach (var kvp in _pending)
            {
                if (_pending.TryRemove(kvp.Key, out var tcs))
                {
                    tcs.TrySetException(new RuntimeUnavailableException("Lost connection to the Runtime Core."));
                }
            }
            _writer?.Dispose();
            _pipe?.Dispose();
            _writer = null;
            _pipe = null;
            ScheduleReconnect();
        }

        private void ScheduleReconnect()
        {
            if (_lifetimeCts is null || _lifetimeCts.IsCancellationRequested) return;
            var ct = _lifetimeCts.Token;
            _ = Task.Run(async () =>
            {
                var backoff = InitialBackoff;
                while (!ct.IsCancellationRequested && !_connected)
                {
                    try
                    {
                        await Task.Delay(backoff, ct).ConfigureAwait(false);
                        await ConnectOnceAsync(ct).ConfigureAwait(false);
                        return;
                    }
                    catch (RuntimeUnavailableException)
                    {
                        backoff = TimeSpan.FromTicks(Math.Min(backoff.Ticks * 2, MaxBackoff.Ticks));
                    }
                    catch (OperationCanceledException)
                    {
                        return;
                    }
                }
            }, ct);
        }

        public async ValueTask DisposeAsync()
        {
            _lifetimeCts?.Cancel();
            _writer?.Dispose();
            _pipe?.Dispose();
            if (_readLoop is not null)
            {
                try { await _readLoop.ConfigureAwait(false); } catch { /* expected on cancel */ }
            }
        }
    }
}
