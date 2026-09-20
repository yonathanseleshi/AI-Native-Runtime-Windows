using System.Text.Json;

namespace AI_Native_Runtime_Windows.Models
{
    /// <summary>Projection of one `approval.list` row - a view model concern, not a
    /// persistence entity (`data-model.inventory.md` §82's principle, restated here for
    /// Windows: the desktop stores no domain database of its own).</summary>
    public sealed record ApprovalItem(
        string Id,
        string ExecutionId,
        string ApplicationId,
        string CapabilityId,
        string Resource,
        string BaselineRisk,
        string EffectiveRisk,
        string State,
        string? UserId)
    {
        public static ApprovalItem FromJson(JsonElement e) => new(
            Id: e.GetProperty("id").GetString() ?? "",
            ExecutionId: e.TryGetProperty("executionId", out var ex) ? ex.GetString() ?? "" : "",
            ApplicationId: e.TryGetProperty("applicationId", out var a) ? a.GetString() ?? "" : "",
            CapabilityId: e.TryGetProperty("capabilityId", out var c) ? c.GetString() ?? "" : "",
            Resource: e.TryGetProperty("resource", out var r) ? r.GetString() ?? "" : "",
            BaselineRisk: e.TryGetProperty("baselineRisk", out var br) ? br.GetString() ?? "" : "",
            EffectiveRisk: e.TryGetProperty("effectiveRisk", out var er) ? er.GetString() ?? "" : "",
            State: e.TryGetProperty("state", out var s) ? s.GetString() ?? "" : "",
            UserId: e.TryGetProperty("userId", out var u) && u.ValueKind == JsonValueKind.String ? u.GetString() : null);

        /// <summary>Criterion 10: labels naming exactly which request they decide, rather
        /// than a bare "Deny"/"Allow Once" ambiguous once more than one approval is pending.</summary>
        public string DenyAutomationLabel => $"Deny {CapabilityId} request for {Resource}";
        public string AllowAutomationLabel => $"Allow once: {CapabilityId} request for {Resource}";

        /// <summary>One combined narration stop for the row's static description, instead
        /// of several undifferentiated reads (capability, resource, requester, risk).</summary>
        public string AccessibilitySummary =>
            $"{CapabilityId} on {Resource}. Requested by {ApplicationId}. Baseline risk {BaselineRisk}, effective risk {EffectiveRisk}.";
    }

    public sealed record ExecutionItem(
        string Id,
        string CapabilityId,
        string Origin,
        string State,
        string RequestedByApplicationId,
        string? RequestedByUserId,
        string? CorrelationId,
        string CreatedAt,
        string? CompletedAt)
    {
        public static ExecutionItem FromJson(JsonElement e) => new(
            Id: e.GetProperty("id").GetString() ?? "",
            CapabilityId: e.TryGetProperty("capabilityId", out var c) ? c.GetString() ?? "" : "",
            Origin: e.TryGetProperty("origin", out var o) ? o.GetString() ?? "" : "",
            State: e.TryGetProperty("state", out var s) ? s.GetString() ?? "" : "",
            RequestedByApplicationId: e.TryGetProperty("requestedByApplicationId", out var ra) ? ra.GetString() ?? "" : "",
            RequestedByUserId: e.TryGetProperty("requestedByUserId", out var ru) && ru.ValueKind == JsonValueKind.String ? ru.GetString() : null,
            CorrelationId: e.TryGetProperty("correlationId", out var ci) && ci.ValueKind == JsonValueKind.String ? ci.GetString() : null,
            CreatedAt: e.TryGetProperty("createdAt", out var ca) ? ca.GetString() ?? "" : "",
            CompletedAt: e.TryGetProperty("completedAt", out var co) && co.ValueKind == JsonValueKind.String ? co.GetString() : null);

        public bool IsCloudDispatched => Origin == "CLOUD_DISPATCHED";
        public bool HasCorrelationId => !string.IsNullOrEmpty(CorrelationId);
    }

    public sealed record CapabilityItem(
        string CapabilityId,
        string ProviderKind,
        string? ProviderWorkerId,
        string Availability,
        string RiskBaseline,
        bool ApprovalRequired)
    {
        public static CapabilityItem FromJson(JsonElement e)
        {
            var definition = e.GetProperty("definition");
            return new(
                CapabilityId: definition.TryGetProperty("capabilityId", out var cid) ? cid.GetString() ?? "" : "",
                ProviderKind: e.TryGetProperty("providerKind", out var pk) ? pk.GetString() ?? "" : "",
                ProviderWorkerId: e.TryGetProperty("providerWorkerId", out var pw) && pw.ValueKind == JsonValueKind.String ? pw.GetString() : null,
                Availability: e.TryGetProperty("availability", out var av) ? av.ToString() : "",
                // Sourced generically from the wire `riskBaseline` string (LOW/MODERATE/HIGH/
                // RESTRICTED, per `ainativeruntime_protocol::capability::RiskBaseline`) - no
                // per-capability-id mapping, so any new capability id renders with its real
                // risk label without UI code changes.
                RiskBaseline: definition.TryGetProperty("riskBaseline", out var rb) ? rb.GetString() ?? "" : "",
                ApprovalRequired: definition.TryGetProperty("approvalRequired", out var ar) && ar.ValueKind == JsonValueKind.True);
        }
    }
}
