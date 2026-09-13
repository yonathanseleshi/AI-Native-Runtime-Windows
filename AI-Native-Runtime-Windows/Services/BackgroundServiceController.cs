using System.Diagnostics;

namespace AI_Native_Runtime_Windows.Services
{
    public enum BackgroundServiceState
    {
        NotInstalled,
        Running,
        Stopped,
        Unknown,
    }

    /// <summary>
    /// Queries and toggles the per-user scheduled task
    /// (`Scripts/install-background-service.ps1`) that runs the Runtime
    /// Core daemon at logon - ADR-0014's Windows choice: a per-user
    /// background process, not a Windows Service, with the "requires a
    /// signed-in user, does not survive a full logout with nobody signed
    /// in" limitation stated rather than hidden, mirroring MAC's
    /// LaunchAgent decision. Installation itself happens via the
    /// PowerShell scripts (run once, interactively, like MAC's shell
    /// scripts) - this class only reports status and can start/stop the
    /// already-registered task; it does not silently register one on the
    /// user's behalf.
    /// </summary>
    public sealed class BackgroundServiceController
    {
        private const string TaskName = "AINativeRuntime.Daemon";

        public async Task<BackgroundServiceState> GetStateAsync(CancellationToken ct = default)
        {
            var (exitCode, output) = await RunSchtasksAsync($"/Query /TN \"{TaskName}\"", ct).ConfigureAwait(false);
            return ParseState(exitCode, output);
        }

        /// <summary>Pure parsing, factored out for unit testing against captured real
        /// `schtasks /Query` output - mirrors this project's own precedent
        /// (`LaunchAgentStatus.parse` on MAC, tested against real `launchctl print` text).</summary>
        public static BackgroundServiceState ParseState(int exitCode, string output)
        {
            if (exitCode != 0)
            {
                return BackgroundServiceState.NotInstalled;
            }
            if (output.Contains("Running", StringComparison.OrdinalIgnoreCase))
            {
                return BackgroundServiceState.Running;
            }
            if (output.Contains("Ready", StringComparison.OrdinalIgnoreCase) || output.Contains("Queued", StringComparison.OrdinalIgnoreCase))
            {
                return BackgroundServiceState.Stopped;
            }
            return BackgroundServiceState.Unknown;
        }

        public Task<(int ExitCode, string Output)> StartAsync(CancellationToken ct = default) =>
            RunSchtasksAsync($"/Run /TN \"{TaskName}\"", ct);

        private static async Task<(int ExitCode, string Output)> RunSchtasksAsync(string arguments, CancellationToken ct)
        {
            var psi = new ProcessStartInfo("schtasks.exe", arguments)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            using var process = Process.Start(psi)
                ?? throw new InvalidOperationException("Could not start schtasks.exe.");
            var stdout = await process.StandardOutput.ReadToEndAsync(ct).ConfigureAwait(false);
            await process.WaitForExitAsync(ct).ConfigureAwait(false);
            return (process.ExitCode, stdout);
        }
    }
}
