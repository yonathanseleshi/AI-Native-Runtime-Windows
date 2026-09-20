using System.Collections.ObjectModel;
using System.Text.Json;
using AI_Native_Runtime_Windows.Models;
using AI_Native_Runtime_Windows.Services;
using AI_Native_Runtime_Windows.Shared;
using CommunityToolkit.Mvvm.Input;

namespace AI_Native_Runtime_Windows.ViewModels
{
    /// <summary>
    /// Permission Grants — INV-02 plan §4.8/§10, Checkpoint INV-02C:
    /// "confirm both MAC and WIN render a grant review list (RT-PERM-008),
    /// including expired and revoked grants shown as such." Every grant
    /// `permission.list` returns is rendered, unfiltered — an `EXPIRED`/
    /// `REVOKED` row stays in the list with its status visible, exactly like
    /// `ApprovalsViewModel` keeps a still-pending item visible on a failed
    /// decide, rather than being hidden or dropped.
    /// </summary>
    public sealed partial class PermissionGrantsViewModel : SurfaceViewModelBase
    {
        private readonly RuntimeService _runtime;

        public ObservableCollection<PermissionGrantItem> Grants { get; } = new();

        public PermissionGrantsViewModel(RuntimeService runtime)
        {
            _runtime = runtime;
        }

        protected override async Task LoadCoreAsync(CancellationToken ct)
        {
            var result = await _runtime.ListPermissionGrantsAsync(ct).ConfigureAwait(true);
            Grants.Clear();
            if (result.TryGetProperty("grants", out var list) && list.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in list.EnumerateArray())
                {
                    Grants.Add(PermissionGrantItem.FromJson(item));
                }
            }
            State = Grants.Count == 0 ? SurfaceState.Empty : SurfaceState.Data;
        }

        /// <summary>Revokes an active `source = local` grant. Mirrors
        /// `ApprovalsViewModel.DecideAsync`'s failure handling: on a rejection (e.g.
        /// CORE's non-disclosing "not found" for an already-gone or `org_admin`-sourced
        /// grant, §4.8 rule 5), re-run `LoadAsync` rather than silently drop the row, so
        /// the reviewer sees the grant's real, current state instead of a stale
        /// optimistic removal.</summary>
        [RelayCommand]
        private async Task RevokeAsync(PermissionGrantItem item)
        {
            try
            {
                await _runtime.RevokePermissionGrantAsync(item.Id).ConfigureAwait(true);
                await LoadAsync().ConfigureAwait(true);
            }
            catch (Exception)
            {
                await LoadAsync().ConfigureAwait(true);
            }
        }
    }
}
