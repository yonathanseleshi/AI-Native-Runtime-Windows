using System.Text.Json;
using AI_Native_Runtime_Windows.Services;
using AI_Native_Runtime_Windows.Services.Transport;
using AI_Native_Runtime_Windows.Shared;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AI_Native_Runtime_Windows.ViewModels
{
    /// <summary>
    /// Home/Runtime Status - the one surface with genuine safety
    /// significance (criterion 8): the emergency controls are thin
    /// callers of F8A's real CORE operations, never a client-side
    /// simulation, and this view model reflects the resulting state
    /// rather than assuming it.
    ///
    /// <b>Honesty notes, per the conventions doc and plan §4.15:</b>
    /// <see cref="Liveness"/> renders `runtime.health`'s `live` field as a
    /// liveness indication ("the Runtime is responding"), never as a
    /// computed health assessment - CORE's own `health` field is a
    /// hardcoded literal, not computed from anything, and this view model
    /// does not invent a client-side computation to fill that gap.
    /// <see cref="IsPaused"/> is a client-side last-known toggle, not a
    /// value re-queried from CORE - CORE exposes no "is the runtime
    /// currently paused" query, only the pause/resume actions' own return
    /// values, so a fresh launch shows "accepting new work" regardless of
    /// the daemon's actual state until the next explicit pause/resume
    /// action (mirroring MAC's own stated limitation, Phase 1 report §11).
    /// </summary>
    public sealed partial class HomeViewModel : SurfaceViewModelBase
    {
        private readonly RuntimeService _runtime;

        [ObservableProperty] private string nodeId = "";
        [ObservableProperty] private string nodeStatus = "";
        [ObservableProperty] private string protocolVersion = "";
        [ObservableProperty] private bool liveness;
        [ObservableProperty] private long uptimeMs;
        [ObservableProperty] private string connectionState = "UNKNOWN";
        [ObservableProperty] private bool isPaused;
        [ObservableProperty] private bool isCloudConnected = true;
        [ObservableProperty] private string controlActionMessage = "";

        public HomeViewModel(RuntimeService runtime)
        {
            _runtime = runtime;
        }

        protected override async Task LoadCoreAsync(CancellationToken ct)
        {
            var status = await _runtime.GetStatusAsync(ct).ConfigureAwait(true);
            var node = status.TryGetProperty("node", out var n) ? n : default;
            NodeId = node.ValueKind == JsonValueKind.Object && node.TryGetProperty("id", out var id) ? id.GetString() ?? "" : "";
            NodeStatus = node.ValueKind == JsonValueKind.Object && node.TryGetProperty("status", out var st) ? st.GetString() ?? "" : "";
            ProtocolVersion = status.TryGetProperty("protocolVersion", out var pv)
                ? $"{pv.GetProperty("major").GetInt32()}.{pv.GetProperty("minor").GetInt32()}"
                : "";
            UptimeMs = status.TryGetProperty("uptimeMs", out var up) ? up.GetInt64() : 0;

            var health = await _runtime.GetHealthAsync(ct).ConfigureAwait(true);
            Liveness = health.TryGetProperty("live", out var live) && live.GetBoolean();

            var connection = await _runtime.GetConnectionStatusAsync(ct).ConfigureAwait(true);
            ConnectionState = connection.TryGetProperty("state", out var cs) ? cs.GetString() ?? "UNKNOWN" : "UNKNOWN";

            State = SurfaceState.Data;
        }

        [RelayCommand]
        private async Task PauseAsync()
        {
            try
            {
                await _runtime.PauseRuntimeAsync();
                IsPaused = true;
                ControlActionMessage = "Paused - new work is refused; running work finishes.";
            }
            catch (Exception ex)
            {
                ControlActionMessage = RuntimeAppError.FromException(ex).Message;
            }
        }

        [RelayCommand]
        private async Task ResumeAsync()
        {
            try
            {
                await _runtime.ResumeRuntimeAsync();
                IsPaused = false;
                ControlActionMessage = "Resumed - accepting new work again.";
            }
            catch (Exception ex)
            {
                ControlActionMessage = RuntimeAppError.FromException(ex).Message;
            }
        }

        [RelayCommand]
        private async Task StopRuntimeAsync()
        {
            try
            {
                await _runtime.StopRuntimeAsync();
                ControlActionMessage = "Stop requested - the Runtime is shutting down.";
            }
            catch (Exception ex)
            {
                ControlActionMessage = RuntimeAppError.FromException(ex).Message;
            }
        }

        [RelayCommand]
        private async Task DisconnectCloudAsync()
        {
            try
            {
                await _runtime.DisconnectCloudAsync();
                IsCloudConnected = false;
                ControlActionMessage = "Cloud disconnected - local execution is unaffected.";
            }
            catch (Exception ex)
            {
                ControlActionMessage = RuntimeAppError.FromException(ex).Message;
            }
        }

        [RelayCommand]
        private async Task ConnectCloudAsync()
        {
            try
            {
                await _runtime.ConnectCloudAsync();
                IsCloudConnected = true;
                ControlActionMessage = "Cloud reconnected.";
            }
            catch (Exception ex)
            {
                ControlActionMessage = RuntimeAppError.FromException(ex).Message;
            }
        }
    }
}
