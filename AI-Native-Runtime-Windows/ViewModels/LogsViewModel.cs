using System.Collections.ObjectModel;
using System.Text.Json;
using AI_Native_Runtime_Windows.Services;
using AI_Native_Runtime_Windows.Shared;

namespace AI_Native_Runtime_Windows.ViewModels
{
    /// <summary>
    /// Logs - renders `runtime.logs`'s own bounded, redacted tail
    /// verbatim. This surface must not add any client-side logging of
    /// request payloads (master §6.4's "no credential, token, private
    /// key, signature, or nonce in any log at any level" applies to this
    /// desktop's own logs too, not only the daemon's).
    /// </summary>
    public sealed partial class LogsViewModel : SurfaceViewModelBase
    {
        private readonly RuntimeService _runtime;

        public ObservableCollection<string> Lines { get; } = new();

        public LogsViewModel(RuntimeService runtime)
        {
            _runtime = runtime;
        }

        protected override async Task LoadCoreAsync(CancellationToken ct)
        {
            var result = await _runtime.GetLogsAsync(ct: ct).ConfigureAwait(true);
            Lines.Clear();
            if (result.TryGetProperty("lines", out var list) && list.ValueKind == JsonValueKind.Array)
            {
                foreach (var line in list.EnumerateArray())
                {
                    Lines.Add(line.GetString() ?? "");
                }
            }
            State = Lines.Count == 0 ? SurfaceState.Empty : SurfaceState.Data;
        }
    }
}
