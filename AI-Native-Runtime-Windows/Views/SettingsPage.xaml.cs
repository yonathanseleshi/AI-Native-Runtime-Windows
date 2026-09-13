using AI_Native_Runtime_Windows.Services;
using AI_Native_Runtime_Windows.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace AI_Native_Runtime_Windows.Views
{
    public sealed partial class SettingsPage : Page
    {
        public SettingsViewModel ViewModel { get; }

        public SettingsPage()
        {
            ViewModel = new SettingsViewModel(
                App.AppHost.Services.GetRequiredService<AuthService>(),
                App.AppHost.Services.GetRequiredService<BackgroundServiceController>());
            InitializeComponent();
        }

        protected override async void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            await ViewModel.RefreshBackgroundServiceStatusCommand.ExecuteAsync(null);
        }

        private void OnSignOutClick(object sender, RoutedEventArgs e)
        {
            ViewModel.SignOutCommand.Execute(null);
            var window = (Application.Current as App)?.MainAppWindow;
            if (window is MainWindow mainWindow)
            {
                mainWindow.NavigateToSignIn();
            }
        }
    }
}
