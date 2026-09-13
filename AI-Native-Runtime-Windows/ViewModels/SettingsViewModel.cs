using AI_Native_Runtime_Windows.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AI_Native_Runtime_Windows.ViewModels
{
    /// <summary>Settings - the ninth navigation surface (master §16.5), covering
    /// sign-out and the per-user background service's status/start, since
    /// installation itself is a one-time interactive step (`Scripts/install-background-service.ps1`)
    /// this app does not perform silently on the user's behalf.</summary>
    public sealed partial class SettingsViewModel : ObservableObject
    {
        private readonly AuthService _auth;
        private readonly BackgroundServiceController _backgroundService;

        [ObservableProperty] private string backgroundServiceStatus = "Checking...";

        public SettingsViewModel(AuthService auth, BackgroundServiceController backgroundService)
        {
            _auth = auth;
            _backgroundService = backgroundService;
        }

        [RelayCommand]
        private async Task RefreshBackgroundServiceStatusAsync()
        {
            var state = await _backgroundService.GetStateAsync();
            BackgroundServiceStatus = state switch
            {
                BackgroundServiceState.NotInstalled => "Not installed - run Scripts\\install-background-service.ps1 once to register it.",
                BackgroundServiceState.Running => "Running",
                BackgroundServiceState.Stopped => "Registered, not currently running",
                _ => "Unknown",
            };
        }

        [RelayCommand]
        private void SignOut()
        {
            _auth.SignOut();
        }
    }
}
