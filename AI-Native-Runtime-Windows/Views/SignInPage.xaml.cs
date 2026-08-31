using System;
using System.Net.Http;
using System.Threading.Tasks;
using AI_Native_Runtime_Windows.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.UI.Xaml.Controls;

namespace AI_Native_Runtime_Windows.Views
{
    /// <summary>
    /// The sign-in screen: Firebase email/password sign-in
    /// (<see cref="AuthService"/>) followed by the local device
    /// registration ceremony against CORE (<see cref="RuntimeClient"/>).
    /// Surfaces three distinguishable failure states per the application
    /// guide - runtime unavailable, substrate offline, registration
    /// rejected - as separate <c>InfoBar</c>s, plus a fourth ("sign-in
    /// failed") for the ordinary wrong-password case, which is not one of
    /// the three named states but must not be collapsed into them either.
    /// </summary>
    public sealed partial class SignInPage : Page
    {
        private readonly AuthService _authService;
        private readonly RuntimeClient _runtimeClient;

        public SignInPage()
        {
            InitializeComponent();

            // Foundation-wave wiring: a real app would resolve these through
            // Microsoft.Extensions.DependencyInjection (already referenced
            // in the .csproj) from App.xaml.cs's host builder. Constructed
            // directly here to keep this checkpoint's slice self-contained
            // and easy to follow end-to-end in one file.
            var configuration = new ConfigurationBuilder()
                .SetBasePath(AppContext.BaseDirectory)
                .AddJsonFile("appsettings.json", optional: true, reloadOnChange: false)
                .Build();

            var firebaseApiKey = configuration["Firebase:ApiKey"] ?? string.Empty;
            var applicationId = configuration["Runtime:ApplicationId"] ?? "app_desktop_windows";
            var pipeName = configuration["Runtime:PipeName"] ?? "ainativeruntime-runtime";

            var credentialStore = new CredentialStore();
            _authService = new AuthService(new HttpClient(), credentialStore, firebaseApiKey);
            _runtimeClient = new RuntimeClient(credentialStore, applicationId, pipeName);
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

                // Step 1: authenticate the human against Firebase. Failures
                // here are either "substrate offline" (network) or an
                // ordinary rejected sign-in (bad credentials) - AuthService
                // distinguishes these via exception type.
                await _authService.SignInAsync(email, password);

                // Step 2: connect to CORE over the named pipe. A failure
                // here is the "runtime unavailable" state - it happens
                // before any registration ceremony begins, so it is checked
                // even though sign-in itself already succeeded.
                await _runtimeClient.ConnectAsync();

                // Step 3: bind this installation, establish a session, and
                // register the device - using a freshly refreshed Firebase
                // ID token (plan §4.31), never the one SignInAsync first
                // returned, since some time may have passed.
                var freshToken = await _authService.GetFreshIdTokenAsync();
                var outcome = await _runtimeClient.SignInAndRegisterAsync(freshToken);

                // A real app would navigate to the main window/shell here,
                // passing `outcome` (nodeId/deviceId/organizationId/state)
                // along. Out of scope for this checkpoint's sign-in slice.
                System.Diagnostics.Debug.WriteLine(
                    $"Device registered: nodeId={outcome.NodeId} organizationId={outcome.OrganizationId} state={outcome.State}");
            }
            catch (SubstrateOfflineException)
            {
                ShowBanner(SubstrateOfflineBanner);
            }
            catch (SignInRejectedException)
            {
                ShowBanner(SignInFailedBanner, "Incorrect email or password.");
            }
            catch (RuntimeUnavailableException)
            {
                ShowBanner(RuntimeUnavailableBanner);
            }
            catch (RuntimeRpcException rpcEx) when (rpcEx.IndicatesSubstrateOffline)
            {
                // CORE itself could not reach RTAPI - from the user's
                // perspective this is the same "substrate offline" state as
                // Firebase being unreachable, even though it surfaced from
                // a different leg of the ceremony.
                ShowBanner(SubstrateOfflineBanner);
            }
            catch (RuntimeRpcException rpcEx) when (rpcEx.IndicatesRegistrationRejected)
            {
                ShowBanner(RegistrationRejectedBanner, rpcEx.Message);
            }
            catch (RuntimeRpcException rpcEx)
            {
                ShowBanner(RegistrationRejectedBanner, rpcEx.Message);
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
