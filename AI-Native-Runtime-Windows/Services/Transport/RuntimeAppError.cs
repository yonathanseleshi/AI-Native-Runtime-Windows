using AI_Native_Runtime_Windows.Models.Protocol;

namespace AI_Native_Runtime_Windows.Services.Transport
{
    /// <summary>How a feature surface should render a failure — the one
    /// mapping every surface reads, per `desktop-shell-conventions.md`
    /// §5: "map every envelope to one typed application error, once,
    /// reusable by every feature surface — never re-derive a
    /// code-to-presentation mapping per screen."</summary>
    public enum ErrorPresentation
    {
        /// <summary>Transient; a retry (automatic or user-initiated) is reasonable.</summary>
        RetryableFailure,
        /// <summary>Not (or no longer) authenticated/authorized — re-establish a session or sign in again.</summary>
        AccessDenied,
        /// <summary>The requested thing genuinely doesn't exist or isn't set up — an empty state, not an error banner.</summary>
        NotFoundOrUnconfigured,
        /// <summary>A protocol-level or programming-adjacent problem no retry fixes.</summary>
        TerminalFailure,
    }

    /// <summary>
    /// The one typed application error every feature surface consumes —
    /// mirrors MAC's `RuntimeAppError.swift` (`desktop-shell-conventions.md`
    /// §5). Distinguishes a transport-level failure (the pipe itself could
    /// not be reached/opened — the "Runtime unavailable" state, §43) from a
    /// structurally valid, server-returned rejection.
    /// </summary>
    public abstract record RuntimeAppError
    {
        public abstract ErrorPresentation PresentationCategory { get; }
        public abstract string Message { get; }

        /// <summary>The pipe could not be reached at all — daemon not running, pipe name
        /// mismatch, connect timeout. This is CORE's own "Runtime unavailable" §43 state.</summary>
        public sealed record TransportUnavailable(string Detail) : RuntimeAppError
        {
            public override ErrorPresentation PresentationCategory => ErrorPresentation.RetryableFailure;
            public override string Message => $"The Runtime Core is unavailable: {Detail}";
        }

        /// <summary>CORE answered with a well-formed `{code, message}` rejection.</summary>
        public sealed record ServerRejected(RuntimeErrorDto Error) : RuntimeAppError
        {
            public override ErrorPresentation PresentationCategory => Error.Code switch
            {
                "NODE_UNAVAILABLE" or "NODE_BUSY" or "STREAM_LAGGED" => ErrorPresentation.RetryableFailure,
                "UNAUTHENTICATED" or "SESSION_EXPIRED" or "PERMISSION_DENIED" or "SCOPE_DENIED" or "APPROVAL_DENIED"
                    => ErrorPresentation.AccessDenied,
                "RESOURCE_NOT_FOUND" or "PROVIDER_NOT_CONFIGURED" or "CAPABILITY_UNKNOWN"
                    => ErrorPresentation.NotFoundOrUnconfigured,
                _ => ErrorPresentation.TerminalFailure,
            };
            public override string Message => Error.Message;
            public string Code => Error.Code;
        }

        /// <summary>A malformed response, an unparseable envelope, or any other
        /// protocol-level problem no retry fixes.</summary>
        public sealed record Protocol(string Detail) : RuntimeAppError
        {
            public override ErrorPresentation PresentationCategory => ErrorPresentation.TerminalFailure;
            public override string Message => Detail;
        }

        public static RuntimeAppError FromException(Exception ex) => ex switch
        {
            RuntimeUnavailableException u => new TransportUnavailable(u.Message),
            RuntimeRpcException r => new ServerRejected(new RuntimeErrorDto { Code = r.Code, Message = r.Message }),
            _ => new Protocol(ex.Message),
        };
    }

    /// <summary>The pipe itself could not be reached (daemon not running, pipe name
    /// mismatch, connect timeout, or lost mid-request).</summary>
    public sealed class RuntimeUnavailableException : Exception
    {
        public RuntimeUnavailableException(string message, Exception? inner = null) : base(message, inner) { }
    }

    /// <summary>CORE answered over the pipe with a well-formed error envelope.</summary>
    public sealed class RuntimeRpcException : Exception
    {
        public string Code { get; }
        public RuntimeRpcException(string code, string message) : base(message) { Code = code; }
    }
}
