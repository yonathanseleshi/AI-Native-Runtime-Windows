using System.Text.Json;

namespace AI_Native_Runtime_Windows.Models
{
    /// <summary>Projection of one `permission.list` row (INV-02 plan §4.8/§10,
    /// Checkpoint INV-02C — RT-PERM-008). Mirrors `ApprovalItem`/`CapabilityItem`'s
    /// own "view-model projection, not a persistence entity" convention
    /// (`data-model.inventory.md` §82). Expired and revoked grants are represented
    /// here exactly like active ones — this surface must never filter them out
    /// (plan revision v1.1, finding 10).</summary>
    public sealed record PermissionGrantItem(
        string Id,
        string ApplicationId,
        string CapabilityId,
        string Effect,
        string ScopeType,
        string? ResourcePattern,
        string Duration,
        string? SessionId,
        string GrantedBy,
        string GrantedAt,
        string? ExpiresAt,
        string? RevokedAt,
        string? RevokedBy,
        string Source,
        string Status)
    {
        public static PermissionGrantItem FromJson(JsonElement e) => new(
            Id: e.GetProperty("id").GetString() ?? "",
            ApplicationId: e.TryGetProperty("applicationId", out var a) ? a.GetString() ?? "" : "",
            CapabilityId: e.TryGetProperty("capabilityId", out var c) ? c.GetString() ?? "" : "",
            Effect: e.TryGetProperty("effect", out var ef) ? ef.GetString() ?? "" : "",
            ScopeType: e.TryGetProperty("scopeType", out var st) ? st.GetString() ?? "" : "",
            ResourcePattern: e.TryGetProperty("resourcePattern", out var rp) && rp.ValueKind == JsonValueKind.String ? rp.GetString() : null,
            Duration: e.TryGetProperty("duration", out var d) ? d.GetString() ?? "" : "",
            SessionId: e.TryGetProperty("sessionId", out var sid) && sid.ValueKind == JsonValueKind.String ? sid.GetString() : null,
            GrantedBy: e.TryGetProperty("grantedBy", out var gb) ? gb.GetString() ?? "" : "",
            GrantedAt: e.TryGetProperty("grantedAt", out var ga) ? ga.GetString() ?? "" : "",
            ExpiresAt: e.TryGetProperty("expiresAt", out var ea) && ea.ValueKind == JsonValueKind.String ? ea.GetString() : null,
            RevokedAt: e.TryGetProperty("revokedAt", out var ra) && ra.ValueKind == JsonValueKind.String ? ra.GetString() : null,
            RevokedBy: e.TryGetProperty("revokedBy", out var rb) && rb.ValueKind == JsonValueKind.String ? rb.GetString() : null,
            Source: e.TryGetProperty("source", out var s) ? s.GetString() ?? "" : "",
            Status: e.TryGetProperty("status", out var stat) ? stat.GetString() ?? "" : "");

        /// <summary>Only an `ACTIVE` grant is a candidate for revocation via
        /// `permission.revoke` — an already-`EXPIRED`/`REVOKED` row has nothing left
        /// to revoke, and CORE would answer with its non-disclosing "not found"
        /// rejection (§4.8 rule 5) if asked anyway.</summary>
        public bool CanRevoke => Status == "ACTIVE";

        /// <summary>Scope summary combining scope type and resource pattern — a
        /// capability-scoped grant has no resource pattern at all.</summary>
        public string ScopeSummary => ScopeType == "capability" || string.IsNullOrEmpty(ResourcePattern)
            ? ScopeType
            : $"{ScopeType}: {ResourcePattern}";

        public string RevokeAutomationLabel => $"Revoke {CapabilityId} grant for {ApplicationId}";

        public string AccessibilitySummary =>
            $"{CapabilityId} on {ApplicationId}, effect {Effect}, scope {ScopeSummary}, duration {Duration}, status {Status}. " +
            $"Granted by {GrantedBy} at {GrantedAt}." +
            (RevokedAt is not null ? $" Revoked at {RevokedAt} by {RevokedBy}." : "");
    }
}
