using System.Collections.ObjectModel;
using System.Text.Json;
using AI_Native_Runtime_Windows.Models;
using AI_Native_Runtime_Windows.Services;
using AI_Native_Runtime_Windows.Shared;
using CommunityToolkit.Mvvm.Input;

namespace AI_Native_Runtime_Windows.ViewModels
{
    /// <summary>
    /// Approvals - one of the two surfaces criterion 10 calls out
    /// specifically for accessibility verification (alongside the
    /// emergency controls), because a mis-clicked Allow/Deny here has
    /// real consequences. Allow-once and Deny only (`RT-HITL-003`/`004` -
    /// session/persistent grants - are explicitly out of scope, plan §8):
    /// there is no third option and this surface must not imply one
    /// exists.
    /// </summary>
    public sealed partial class ApprovalsViewModel : SurfaceViewModelBase
    {
        private readonly RuntimeService _runtime;

        public ObservableCollection<ApprovalItem> Approvals { get; } = new();

        public ApprovalsViewModel(RuntimeService runtime)
        {
            _runtime = runtime;
        }

        protected override async Task LoadCoreAsync(CancellationToken ct)
        {
            var result = await _runtime.ListApprovalsAsync(ct).ConfigureAwait(true);
            Approvals.Clear();
            if (result.TryGetProperty("approvals", out var list) && list.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in list.EnumerateArray())
                {
                    Approvals.Add(ApprovalItem.FromJson(item));
                }
            }
            State = Approvals.Count == 0 ? SurfaceState.Empty : SurfaceState.Data;
        }

        [RelayCommand]
        private async Task AllowOnceAsync(ApprovalItem item)
        {
            await DecideAsync(item, allow: true).ConfigureAwait(true);
        }

        [RelayCommand]
        private async Task DenyAsync(ApprovalItem item)
        {
            await DecideAsync(item, allow: false).ConfigureAwait(true);
        }

        private async Task DecideAsync(ApprovalItem item, bool allow)
        {
            try
            {
                await _runtime.DecideApprovalAsync(item.Id, allow).ConfigureAwait(true);
                Approvals.Remove(item);
                if (Approvals.Count == 0) State = SurfaceState.Empty;
            }
            catch (Exception)
            {
                // A decide failure is re-surfaced by the next LoadAsync (the item stays in
                // the list, so the user sees it is still pending rather than silently gone).
                await LoadAsync().ConfigureAwait(true);
            }
        }
    }
}
