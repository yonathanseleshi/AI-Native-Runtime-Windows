using System;
using System.Threading.Tasks;
using AI_Native_Runtime_Windows.Services;
using AI_Native_Runtime_Windows.Services.Transport;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Controls;

namespace AI_Native_Runtime_Windows.Views
{
    /// <summary>
    /// The sign-in screen: Firebase email/password sign-in
    /// (<see cref="AuthService"/>) followed by the local device
    /// registration ceremony against CORE (<see cref="RuntimeService"/>).
    /// Every dependency is now resolved from <see cref="App.AppHost"/>
    /// (plan §4.5) rather than constructed inline, so a missing/blank
    /// Firebase key surfaces as <see cref="ConfigurationErrorBanner"/>,
    /// never a constructor-time exception during window construction.
    /// </summary>
    public sealed partial class SignInPage : Page
    {
        private readonly AuthService _authService = App.AppHost.Services.GetRequiredService<AuthService>();
        private readonly RuntimeService _runtime = App.AppHost.Services.GetRequiredService<RuntimeService>();

        public SignInPage()
        {
            InitializeComponent();
            Loaded += async (_, _) => await TryAutoSignInAsync();
        }

        /// <summary>If a Firebase refresh token can be restored, skip straight to the
        /// registration ceremony rather than asking the user to type a password again -
        /// a CORE session is still re-established fresh on every launch regardless
        /// (`RuntimeService`'s own doc comment: session tokens are never persisted).</summary>
        private async Task TryAutoSignInAsync()
        {
            if (!_authService.IsConfigured)
            {
                ShowBanner(ConfigurationErrorBanner);
                return;
            }
            if (_authService.TryRestoreSession())
            {
                await CompleteSignInAsync();
            }
        }

        private async void OnSignInClick(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
        {
            await SignInAsync();
        }

        private async void OnRetryClick(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
        {
            await SignInAsync();
        }

        private async Task SignInAsync()
        {
            if (!_authService.IsConfigured)
            {
                HideAllBanners();
                ShowBanner(ConfigurationErrorBanner);
                return;
            }

            HideAllBanners();
            SetBusy(true);
            try
            {
                var email = EmailBox.Text?.Trim() ?? string.Empty;
                var password = PasswordBox.Password ?? string.Empty;

                if (string.IsNullOrEmpty(email) || string.IsNullOrEmpty(password))
                {
                    ShowBanner(SignInFailedBanner, "Enter your email and password.");
                    return;
                }

                // Step 1: authenticate the human against Firebase.
                await _authService.SignInAsync(email, password);

                await CompleteSignInAsync();
            }
            catch (SubstrateOfflineException)
            {
                ShowBanner(SubstrateOfflineBanner);
            }
            catch (SignInRejectedException)
            {
                ShowBanner(SignInFailedBanner, "Incorrect email or password.");
            }
            finally
            {
                SetBusy(false);
            }
        }

        /// <summary>Steps 2-3 of the ceremony, shared by the manual sign-in path and the
        /// auto-restored-session path: connect to CORE, then bind/establish/register.</summary>
        private async Task CompleteSignInAsync()
        {
            HideAllBanners();
            SetBusy(true);
            try
            {
                // Step 2: connect to CORE over the named pipe (the RPC connection).
                await _runtime.StartAsync();

                // Step 3: bind this installation, establish a session, and register the
                // device - using a freshly refreshed Firebase ID token (plan §4.31),
                // never the one SignInAsync first returned, since some time may have
                // passed between it and this call.
                var freshToken = await _authService.GetFreshIdTokenAsync();
                await _runtime.SignInAndRegisterAsync(freshToken);

                // The event-stream connection requires a session and is therefore
                // started only now, after the ceremony above has one (§4.6).
                _runtime.StartEventStream();

                var window = (Microsoft.UI.Xaml.Application.Current as App)?.MainAppWindow;
                if (window is MainWindow mainWindow)
                {
                    mainWindow.NavigateToShell();
                }
            }
            catch (RuntimeUnavailableException)
            {
                ShowBanner(RuntimeUnavailableBanner);
            }
            catch (RuntimeRpcException rpcEx)
            {
                var appError = RuntimeAppError.FromException(rpcEx);
                if (appError.PresentationCategory == ErrorPresentation.RetryableFailure && rpcEx.Code == "NODE_UNAVAILABLE")
                {
                    // CORE itself could not reach RTAPI - from the user's perspective
                    // this is the same "substrate offline" state as Firebase being
                    // unreachable, even though it surfaced from a different leg of
                    // the ceremony.
                    ShowBanner(SubstrateOfflineBanner);
                }
                else
                {
                    ShowBanner(RegistrationRejectedBanner, rpcEx.Message);
                }
            }
            finally
            {
                SetBusy(false);
            }
        }

        private void SetBusy(bool busy)
        {
            SignInButton.IsEnabled = !busy;
            BusyRing.IsActive = busy;
            BusyRing.Visibility = busy ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;
        }

        private void HideAllBanners()
        {
            RuntimeUnavailableBanner.IsOpen = false;
            SubstrateOfflineBanner.IsOpen = false;
            RegistrationRejectedBanner.IsOpen = false;
            SignInFailedBanner.IsOpen = false;
            ConfigurationErrorBanner.IsOpen = false;
        }

        private static void ShowBanner(InfoBar banner, string? message = null)
        {
            if (message is not null)
            {
                banner.Message = message;
            }
            banner.IsOpen = true;
        }
    }
}
