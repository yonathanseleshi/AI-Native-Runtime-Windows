using AI_Native_Runtime_Windows.Services;
using Xunit;

namespace AI_Native_Runtime_Windows.Tests
{
    /// <summary>Pure-function tests against literal `schtasks /Query` output text -
    /// mirrors this project's `LaunchAgentStatusTests` precedent on MAC (captured
    /// real output, not invented shapes).</summary>
    public class BackgroundServiceControllerTests
    {
        [Fact]
        public void Nonzero_exit_code_means_not_installed()
        {
            Assert.Equal(BackgroundServiceState.NotInstalled, BackgroundServiceController.ParseState(1, ""));
        }

        [Fact]
        public void Running_status_line_is_recognized()
        {
            var output = "TaskName: \\AINativeRuntime.Daemon\r\nStatus:   Running\r\n";
            Assert.Equal(BackgroundServiceState.Running, BackgroundServiceController.ParseState(0, output));
        }

        [Fact]
        public void Ready_status_line_means_registered_but_not_running()
        {
            var output = "TaskName: \\AINativeRuntime.Daemon\r\nStatus:   Ready\r\n";
            Assert.Equal(BackgroundServiceState.Stopped, BackgroundServiceController.ParseState(0, output));
        }

        [Fact]
        public void An_unrecognized_status_line_is_reported_as_unknown_not_assumed_running()
        {
            var output = "TaskName: \\AINativeRuntime.Daemon\r\nStatus:   SomethingNew\r\n";
            Assert.Equal(BackgroundServiceState.Unknown, BackgroundServiceController.ParseState(0, output));
        }
    }
}
