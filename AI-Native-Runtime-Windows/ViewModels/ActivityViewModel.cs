using System.Collections.ObjectModel;
using System.Text.Json;
using AI_Native_Runtime_Windows.Models;
using AI_Native_Runtime_Windows.Services;
using AI_Native_Runtime_Windows.Shared;

namespace AI_Native_Runtime_Windows.ViewModels
{
    /// <summary>
    /// Activity - criterion 4/5a: real executions from CORE with status
    /// and timestamps, the 100-record limit stated honestly (plan §4.16 -
    /// `execution.list` is hardcoded to the 100 most recent, no
    /// pagination; this surface says so rather than implying an
    /// infinite-scroll that silently stops), and a cloud-dispatched
    /// execution visibly distinguished from a local one with its
    /// correlation ID and requesting principal (§4.17).
    /// </summary>
    public sealed partial class ActivityViewModel : SurfaceViewModelBase
    {
        private readonly RuntimeService _runtime;

        public ObservableCollection<ExecutionItem> Executions { get; } = new();

        public ActivityViewModel(RuntimeService runtime)
        {
            _runtime = runtime;
        }

        protected override async Task LoadCoreAsync(CancellationToken ct)
        {
            var result = await _runtime.ListExecutionsAsync(ct).ConfigureAwait(true);
            Executions.Clear();
            if (result.TryGetProperty("executions", out var list) && list.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in list.EnumerateArray())
                {
                    Executions.Add(ExecutionItem.FromJson(item));
                }
            }
            State = Executions.Count == 0 ? SurfaceState.Empty : SurfaceState.Data;
        }
    }
}
