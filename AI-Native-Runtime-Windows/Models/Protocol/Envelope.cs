using System.Text.Json;
using System.Text.Json.Serialization;

namespace AI_Native_Runtime_Windows.Models.Protocol
{
    /// <summary>
    /// Wire shapes matching CORE's `api/framing.rs` exactly (camelCase on
    /// the wire; `params` defaults to `null`/absent when not supplied) —
    /// per `desktop-shell-conventions.md` §1, the single authoritative
    /// source for this shape, not `docs/local-api.md`. Shared by both
    /// halves of the two-connection model (§2): <c>RpcConnection</c> and
    /// <c>EventStreamConnection</c> serialize/deserialize these same
    /// types rather than each inventing its own.
    /// </summary>
    public sealed class RequestEnvelope
    {
        [JsonPropertyName("protocolVersion")] public required ProtocolVersionDto ProtocolVersion { get; init; }
        [JsonPropertyName("requestId")] public required string RequestId { get; init; }

        /// <summary>Omitted (not sent as `null`) for the four pre-session methods
        /// (`bootstrap.enroll`, `application.bind`, `session.challenge`,
        /// `session.establish`) — `desktop-shell-conventions.md` §1.</summary>
        [JsonPropertyName("sessionToken"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? SessionToken { get; init; }

        [JsonPropertyName("method")] public required string Method { get; init; }
        [JsonPropertyName("params")] public JsonElement Params { get; init; }
    }

    public sealed class ProtocolVersionDto
    {
        [JsonPropertyName("major")] public required int Major { get; init; }
        [JsonPropertyName("minor")] public required int Minor { get; init; }

        /// <summary>CORE is at protocol 0.7 (`CURRENT_PROTOCOL_VERSION`); 0.6 or lower is
        /// still compatible per `ProtocolVersion::is_compatible_with` (same major, minor
        /// not exceeding the implementation's), but this desktop client always sends the
        /// version it was actually built against.</summary>
        public static readonly ProtocolVersionDto Current = new() { Major = 0, Minor = 7 };
    }

    public sealed class ResponseEnvelope
    {
        [JsonPropertyName("requestId")] public string RequestId { get; init; } = "";
        [JsonPropertyName("result")] public JsonElement? Result { get; init; }
        [JsonPropertyName("error")] public RuntimeErrorDto? Error { get; init; }
    }

    /// <summary>A single pushed event line on a subscribed connection — distinct from
    /// <see cref="ResponseEnvelope"/> (no result/error split), matching CORE's
    /// `api::framing::EventPush`.</summary>
    public sealed class EventPushEnvelope
    {
        [JsonPropertyName("requestId")] public string RequestId { get; init; } = "";
        [JsonPropertyName("event")] public JsonElement Event { get; init; }
    }

    /// <summary>ADR-0008's error envelope: `{code, message, details?, retryable?}`.</summary>
    public sealed class RuntimeErrorDto
    {
        [JsonPropertyName("code")] public string Code { get; init; } = "UNKNOWN";
        [JsonPropertyName("message")] public string Message { get; init; } = "";
        [JsonPropertyName("details")] public JsonElement? Details { get; init; }
        [JsonPropertyName("retryable")] public bool? Retryable { get; init; }
    }

    public static class ProtocolJson
    {
        public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);
    }
}
