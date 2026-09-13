using System.Collections.ObjectModel;
using System.Text.Json;
using AI_Native_Runtime_Windows.Models;
using AI_Native_Runtime_Windows.Services;
using AI_Native_Runtime_Windows.Shared;
using CommunityToolkit.Mvvm.ComponentModel;

namespace AI_Native_Runtime_Windows.ViewModels
{
    /// <summary>
    /// Capabilities - criterion 3: the real registry contents including
    /// `filesystem.read@1` and, with a Python worker configured,
    /// `python.run@1`, with provider attribution. The no-worker case
    /// (`worker.status` reporting `{"configured": false, "state":
    /// "NOT_CONFIGURED"}`) is rendered as a real state, not an error or a
    /// spinner (plan §4.9) - a fresh install actually starts in exactly
    /// this configuration.
    /// </summary>
    public sealed partial class CapabilitiesViewModel : SurfaceViewModelBase
    {
        private readonly RuntimeService _runtime;

        public ObservableCollection<CapabilityItem> Capabilities { get; } = new();

        [ObservableProperty] private bool workerConfigured;
        [ObservableProperty] private string workerState = "NOT_CONFIGURED";

        public CapabilitiesViewModel(RuntimeService runtime)
        {
            _runtime = runtime;
        }

        protected override async Task LoadCoreAsync(CancellationToken ct)
        {
            var result = await _runtime.ListCapabilitiesAsync(ct).ConfigureAwait(true);
            Capabilities.Clear();
            if (result.TryGetProperty("capabilities", out var list) && list.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in list.EnumerateArray())
                {
                    Capabilities.Add(CapabilityItem.FromJson(item));
                }
            }

            var worker = await _runtime.GetWorkerStatusAsync(ct).ConfigureAwait(true);
            WorkerConfigured = worker.TryGetProperty("configured", out var c) && c.GetBoolean();
            WorkerState = worker.TryGetProperty("state", out var s) ? s.GetString() ?? "NOT_CONFIGURED" : "NOT_CONFIGURED";

            State = Capabilities.Count == 0 ? SurfaceState.Empty : SurfaceState.Data;
        }
    }
}
