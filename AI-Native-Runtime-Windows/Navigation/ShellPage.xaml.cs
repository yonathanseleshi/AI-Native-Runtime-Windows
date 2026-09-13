using System;
using System.Text.Json;
using AI_Native_Runtime_Windows.Services;
using AI_Native_Runtime_Windows.Shared;
using AI_Native_Runtime_Windows.Views;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace AI_Native_Runtime_Windows.Navigation
{
    /// <summary>
    /// The authenticated shell: a <see cref="NavigationView"/> hosting the
    /// eight non-Settings surfaces (Settings is the NavigationView's own
    /// built-in item — nine surfaces total, master §16.5) plus the
    /// system-tray presence that mirrors MAC's menu bar (§35): connection
    /// state, pending-approval count, and the emergency controls, wired to
    /// the same <see cref="RuntimeService"/> instance the main window uses
    /// so both observe one session and one connection.
    /// </summary>
    public sealed partial class ShellPage : Page
    {
        private readonly RuntimeService _runtime = App.AppHost.Services.GetRequiredService<RuntimeService>();
        private DispatcherQueueTimer? _statusTimer;
        private bool _isPaused;

        public ShellPage()
        {
            InitializeComponent();
            ContentFrame.Navigate(typeof(HomePage));
        }

        private void NavView_Loaded(object sender, RoutedEventArgs e)
        {
            NavView.SelectedItem = NavView.MenuItems[0];
            TryAttachTrayIcon();

            _statusTimer = DispatcherQueue.GetForCurrentThread().CreateTimer();
            _statusTimer.Interval = TimeSpan.FromSeconds(5);
            _statusTimer.Tick += async (_, _) => await RefreshTrayStateAsync();
            _statusTimer.Start();
            _ = RefreshTrayStateAsync();
        }

        private void TryAttachTrayIcon()
        {
            try
            {
                var exePath = Environment.ProcessPath;
                if (exePath is not null)
                {
                    using var icon = System.Drawing.Icon.ExtractAssociatedIcon(exePath);
                    if (icon is not null)
                    {
                        TrayIcon.Icon = icon;
                    }
                }
                TrayIcon.ForceCreate();
            }
            catch (Exception)
            {
                // A tray icon is a convenience, not a safety-critical surface - a failure
                // here (e.g. no icon resolvable) must never take the app down with it.
            }
        }

        private async System.Threading.Tasks.Task RefreshTrayStateAsync()
        {
            if (!_runtime.Rpc.IsConnected)
            {
                ConnectionStatusItem.Text = "Connection: Runtime unavailable";
                return;
            }
            try
            {
                var connection = await _runtime.GetConnectionStatusAsync();
                var state = connection.TryGetProperty("state", out var s) ? s.GetString() : "UNKNOWN";
                ConnectionStatusItem.Text = $"Connection: {state}";

                var approvals = await _runtime.ListApprovalsAsync();
                var count = approvals.TryGetProperty("approvals", out var list) && list.ValueKind == JsonValueKind.Array
                    ? list.GetArrayLength()
                    : 0;
                PendingApprovalsItem.Text = $"Pending approvals: {count}";
            }
            catch (Exception)
            {
                ConnectionStatusItem.Text = "Connection: unknown";
            }
        }

        private void NavView_ItemInvoked(NavigationView sender, NavigationViewItemInvokedEventArgs args)
        {
            if (args.IsSettingsInvoked)
            {
                ContentFrame.Navigate(typeof(SettingsPage));
                return;
            }

            var tag = (args.InvokedItemContainer as NavigationViewItem)?.Tag as string;
            switch (tag)
            {
                case "Home": ContentFrame.Navigate(typeof(HomePage)); break;
                case "Activity": ContentFrame.Navigate(typeof(ActivityPage)); break;
                case "Approvals": ContentFrame.Navigate(typeof(ApprovalsPage)); break;
                case "Capabilities": ContentFrame.Navigate(typeof(CapabilitiesPage)); break;
                case "Logs": ContentFrame.Navigate(typeof(LogsPage)); break;
                // Plan §4.18: Applications, Permissions, and Security have no backing CORE
                // surface on either platform yet - an honest "not yet available" state,
                // never a placeholder screen implying the feature exists.
                case "Applications":
                    ContentFrame.Navigate(typeof(NotYetAvailablePage), (
                        "Applications",
                        "Application management is not yet available in this v0.1 shell. This surface will list applications bound to this installation once a future wave adds the underlying CORE API."));
                    break;
                case "Permissions":
                    ContentFrame.Navigate(typeof(NotYetAvailablePage), (
                        "Permissions",
                        "Per-capability permission management is not yet available. Filesystem roots and other permission grants are configured today only through the Runtime Core's own environment configuration."));
                    break;
                case "Security":
                    ContentFrame.Navigate(typeof(NotYetAvailablePage), (
                        "Security",
                        "A dedicated security overview (audit history, session management) is not yet available in this v0.1 shell. Audit records exist in CORE today but have no desktop surface of their own yet."));
                    break;
            }
        }

        // ---- Emergency controls (mirrors HomePage's own, per §35's "not a second Home") ----

        private async void OnPauseResumeClick(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_isPaused)
                {
                    await _runtime.ResumeRuntimeAsync();
                    _isPaused = false;
                    PauseResumeItem.Text = "Pause Execution";
                }
                else
                {
                    await _runtime.PauseRuntimeAsync();
                    _isPaused = true;
                    PauseResumeItem.Text = "Resume Execution";
                }
            }
            catch (Exception)
            {
                // A tray-menu action failing surfaces via the Home page's own error state
                // on next visit; the tray menu itself has no room for a full error banner.
            }
        }

        private bool _cloudConnected = true;

        private async void OnCloudConnectClick(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_cloudConnected)
                {
                    await _runtime.DisconnectCloudAsync();
                    _cloudConnected = false;
                    CloudConnectItem.Text = "Connect Cloud";
                }
                else
                {
                    await _runtime.ConnectCloudAsync();
                    _cloudConnected = true;
                    CloudConnectItem.Text = "Disconnect Cloud";
                }
            }
            catch (Exception) { }
        }

        private async void OnStopRuntimeClick(object sender, RoutedEventArgs e)
        {
            try { await _runtime.StopRuntimeAsync(); } catch (Exception) { }
        }

        private void OnOpenRuntimeClick(object sender, RoutedEventArgs e)
        {
            var window = (Application.Current as App)?.MainAppWindow;
            window?.Activate();
        }

        private void OnQuitClick(object sender, RoutedEventArgs e)
        {
            // Quitting the app leaves the Runtime running (§35's close/quit/stop
            // distinction) - the daemon is a wholly separate per-user background
            // process (ADR-0014), never a child process this app spawns or owns.
            Application.Current.Exit();
        }
    }
}
