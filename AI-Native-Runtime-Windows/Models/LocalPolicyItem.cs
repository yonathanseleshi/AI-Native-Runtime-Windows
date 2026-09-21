using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace AI_Native_Runtime_Windows.Models
{
    /// <summary>Projection of one `policy.list` row (INV-03 plan §4.7/§4.8/§10,
    /// Checkpoint INV-03D). Mirrors `PermissionGrantItem`'s own "view-model
    /// projection, not a persistence entity" convention. Disabled rows are
    /// represented exactly like enabled ones - there is no delete operation
    /// (§4.7, a hard plan constraint), so "disabled" is this table's only
    /// retirement state and must never be filtered out of this surface.</summary>
    public sealed record LocalPolicyItem(
        string Id,
        IReadOnlyDictionary<string, string> Conditions,
        string Effect,
        int Priority,
        bool Enabled,
        string CreatedAt,
        string UpdatedAt,
        string UpdatedBy)
    {
        public static LocalPolicyItem FromJson(JsonElement e)
        {
            var conditions = new Dictionary<string, string>();
            if (e.TryGetProperty("conditions", out var c) && c.ValueKind == JsonValueKind.Object)
            {
                foreach (var prop in c.EnumerateObject())
                {
                    if (prop.Value.ValueKind == JsonValueKind.String)
                    {
                        conditions[prop.Name] = prop.Value.GetString() ?? "";
                    }
                }
            }

            return new LocalPolicyItem(
                Id: e.GetProperty("id").GetString() ?? "",
                Conditions: conditions,
                Effect: e.TryGetProperty("effect", out var ef) ? ef.GetString() ?? "" : "",
                Priority: e.TryGetProperty("priority", out var p) ? p.GetInt32() : 0,
                Enabled: e.TryGetProperty("enabled", out var en) && en.ValueKind == JsonValueKind.True,
                CreatedAt: e.TryGetProperty("createdAt", out var ca) ? ca.GetString() ?? "" : "",
                UpdatedAt: e.TryGetProperty("updatedAt", out var ua) ? ua.GetString() ?? "" : "",
                UpdatedBy: e.TryGetProperty("updatedBy", out var ub) ? ub.GetString() ?? "" : "");
        }

        /// <summary>The action label toggles between Disable/Enable - there is no
        /// third state and no delete action anywhere on this row (§4.7).</summary>
        public string ToggleActionLabel => Enabled ? "Disable" : "Enable";

        public string ConditionsSummary => Conditions.Count == 0
            ? "Matches every request"
            : string.Join(", ", Conditions.OrderBy(kv => kv.Key).Select(kv => $"{kv.Key}={kv.Value}"));

        public string StatusLabel => Enabled ? "ENABLED" : "DISABLED";

        public string ToggleAutomationLabel => $"{ToggleActionLabel} local policy, priority {Priority}, effect {Effect}";

        public string AccessibilitySummary =>
            $"{Effect} policy, priority {Priority}, {StatusLabel}. Conditions: {ConditionsSummary}. " +
            $"Updated by {UpdatedBy} at {UpdatedAt}.";
    }

    /// <summary>Projection of `policy.list`'s `syncState` object - `policy_sync_state`'s
    /// three named states (plan §4.8), surfaced genuinely on this desktop review
    /// surface rather than merely stored, per §4.8's explicit "must be observable by
    /// an operator" requirement.</summary>
    public sealed record PolicySyncStateItem(
        string? Digest,
        string? SyncedAt,
        string SyncStatus,
        string? LastError)
    {
        public static PolicySyncStateItem FromJson(JsonElement e) => new(
            Digest: e.TryGetProperty("digest", out var d) && d.ValueKind == JsonValueKind.String ? d.GetString() : null,
            SyncedAt: e.TryGetProperty("syncedAt", out var s) && s.ValueKind == JsonValueKind.String ? s.GetString() : null,
            SyncStatus: e.TryGetProperty("syncStatus", out var st) ? st.GetString() ?? "NEVER_SYNCED" : "NEVER_SYNCED",
            LastError: e.TryGetProperty("lastError", out var le) && le.ValueKind == JsonValueKind.String ? le.GetString() : null);

        public string DisplayLabel => SyncStatus switch
        {
            "CURRENT" => "Organization policy: Current",
            "STALE" => "Organization policy: Stale",
            "NEVER_SYNCED" => "Organization policy: Never Synced",
            _ => $"Organization policy: {SyncStatus}",
        };
    }
}
