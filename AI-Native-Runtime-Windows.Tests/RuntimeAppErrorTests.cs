using AI_Native_Runtime_Windows.Models.Protocol;
using AI_Native_Runtime_Windows.Services.Transport;
using Xunit;

namespace AI_Native_Runtime_Windows.Tests
{
    /// <summary>The one code-to-presentation mapping every feature surface reads
    /// (`desktop-shell-conventions.md` §5) - tested exhaustively here so no surface
    /// needs to re-derive or guess at it.</summary>
    public class RuntimeAppErrorTests
    {
        [Theory]
        [InlineData("NODE_UNAVAILABLE")]
        [InlineData("NODE_BUSY")]
        [InlineData("STREAM_LAGGED")]
        public void Retryable_codes_map_to_retryable_failure(string code)
        {
            var error = RuntimeAppError.FromException(new RuntimeRpcException(code, "message"));
            Assert.Equal(ErrorPresentation.RetryableFailure, error.PresentationCategory);
        }

        [Theory]
        [InlineData("UNAUTHENTICATED")]
        [InlineData("SESSION_EXPIRED")]
        [InlineData("PERMISSION_DENIED")]
        [InlineData("SCOPE_DENIED")]
        [InlineData("APPROVAL_DENIED")]
        public void Access_denied_codes_map_to_access_denied(string code)
        {
            var error = RuntimeAppError.FromException(new RuntimeRpcException(code, "message"));
            Assert.Equal(ErrorPresentation.AccessDenied, error.PresentationCategory);
        }

        [Theory]
        [InlineData("RESOURCE_NOT_FOUND")]
        [InlineData("PROVIDER_NOT_CONFIGURED")]
        [InlineData("CAPABILITY_UNKNOWN")]
        public void Not_found_codes_map_to_not_found_or_unconfigured(string code)
        {
            var error = RuntimeAppError.FromException(new RuntimeRpcException(code, "message"));
            Assert.Equal(ErrorPresentation.NotFoundOrUnconfigured, error.PresentationCategory);
        }

        [Fact]
        public void An_unrecognized_code_is_a_terminal_failure_not_silently_swallowed()
        {
            var error = RuntimeAppError.FromException(new RuntimeRpcException("SOMETHING_NEW", "message"));
            Assert.Equal(ErrorPresentation.TerminalFailure, error.PresentationCategory);
        }

        [Fact]
        public void A_transport_level_failure_is_retryable_the_runtime_unavailable_state()
        {
            var error = RuntimeAppError.FromException(new RuntimeUnavailableException("pipe not reachable"));
            Assert.Equal(ErrorPresentation.RetryableFailure, error.PresentationCategory);
            Assert.IsType<RuntimeAppError.TransportUnavailable>(error);
        }
    }
}
