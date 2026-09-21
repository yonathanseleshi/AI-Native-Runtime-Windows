using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Text.Json;
using AI_Native_Runtime_Windows.Models;
using AI_Native_Runtime_Windows.Services;
using AI_Native_Runtime_Windows.Shared;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AI_Native_Runtime_Windows.ViewModels
{
    /// <summary>
    /// Local Runtime Policy review surface - INV-03 plan §4.7/§4.8/§10, Checkpoint
    /// INV-03D. Unlike `PermissionGrantsViewModel`, this needs no per-application
    /// fan-out: `local_policies` is a single-device, operator-managed table (every
    /// row governs every application on this device), so one `policy.list` call
    /// already returns everything there is to show, plus `syncState`
    /// (`policy_sync_state.sync_status`/`syncedAt`/`lastError`, plan §4.8's
    /// "must be observable by an operator" requirement).
    ///
    /// There is no `RevokeAsync`/delete command here, deliberately (§4.7, a hard
    /// plan constraint: no delete operation exists to offer) - only
    /// `ToggleEnabledAsync`, which is how a local policy is ever retired.
    /// </summary>
    public sealed partial class LocalPoliciesViewModel : SurfaceViewModelBase
    {
        private readonly RuntimeService _runtime;

        public ObservableCollection<LocalPolicyItem> Policies { get; } = new();

        [ObservableProperty]
        private PolicySyncStateItem syncState = new(null, null, "NEVER_SYNCED", null);

        [ObservableProperty]
        private string newConditionCapabilityKey = "";

        [ObservableProperty]
        private string newConditionApplicationId = "";

        [ObservableProperty]
        private string newConditionDeviceId = "";

        [ObservableProperty]
        private string newEffect = "DENY";

        [ObservableProperty]
        private int newPriority = 10;

        [ObservableProperty]
        private string? actionErrorMessage;

        public bool HasActionError => !string.IsNullOrEmpty(ActionErrorMessage);

        partial void OnActionErrorMessageChanged(string? value) => OnPropertyChanged(nameof(HasActionError));

        public LocalPoliciesViewModel(RuntimeService runtime)
        {
            _runtime = runtime;
        }

        protected override async Task LoadCoreAsync(CancellationToken ct)
        {
            var result = await _runtime.ListPoliciesAsync(ct).ConfigureAwait(true);
            Policies.Clear();
            if (result.TryGetProperty("localPolicies", out var list) && list.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in list.EnumerateArray())
                {
                    Policies.Add(LocalPolicyItem.FromJson(item));
                }
            }
            if (result.TryGetProperty("syncState", out var sync) && sync.ValueKind == JsonValueKind.Object)
            {
                SyncState = PolicySyncStateItem.FromJson(sync);
            }
            State = Policies.Count == 0 ? SurfaceState.Empty : SurfaceState.Data;
        }

        /// <summary>Creates a new device-local policy from the form fields. Empty
        /// condition fields are omitted entirely (an absent condition key imposes no
        /// constraint), never sent as empty strings (which would instead never
        /// match anything).</summary>
        [RelayCommand]
        private async Task CreateAsync()
        {
            ActionErrorMessage = null;
            var conditions = new Dictionary<string, string>();
            if (!string.IsNullOrWhiteSpace(NewConditionCapabilityKey)) conditions["capabilityKey"] = NewConditionCapabilityKey;
            if (!string.IsNullOrWhiteSpace(NewConditionApplicationId)) conditions["runtimeApplicationId"] = NewConditionApplicationId;
            if (!string.IsNullOrWhiteSpace(NewConditionDeviceId)) conditions["runtimeDeviceId"] = NewConditionDeviceId;

            try
            {
                await _runtime.CreatePolicyAsync(conditions, NewEffect, NewPriority).ConfigureAwait(true);
                NewConditionCapabilityKey = "";
                NewConditionApplicationId = "";
                NewConditionDeviceId = "";
                await LoadAsync().ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                ActionErrorMessage = RuntimeAppError.FromException(ex).Message;
            }
        }

        /// <summary>Toggles `enabled` - this **is** how a local policy is "deleted"
        /// (§4.7). Every other field round-trips unchanged. On failure, reload
        /// rather than optimistically flip the row, mirroring
        /// `PermissionGrantsViewModel.RevokeAsync`'s own "show the real current
        /// state" discipline.</summary>
        [RelayCommand]
        private async Task ToggleEnabledAsync(LocalPolicyItem item)
        {
            ActionErrorMessage = null;
            try
            {
                await _runtime.UpdatePolicyAsync(
                    item.Id,
                    item.Conditions,
                    item.Effect,
                    item.Priority,
                    !item.Enabled).ConfigureAwait(true);
                await LoadAsync().ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                ActionErrorMessage = RuntimeAppError.FromException(ex).Message;
                await LoadAsync().ConfigureAwait(true);
            }
        }
    }
}
