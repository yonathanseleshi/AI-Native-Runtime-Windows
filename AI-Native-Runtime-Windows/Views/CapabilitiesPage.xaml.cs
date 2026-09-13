using AI_Native_Runtime_Windows.Services;
using AI_Native_Runtime_Windows.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace AI_Native_Runtime_Windows.Views
{
    public sealed partial class CapabilitiesPage : Page
    {
        public CapabilitiesViewModel ViewModel { get; }

        public CapabilitiesPage()
        {
            ViewModel = new CapabilitiesViewModel(App.AppHost.Services.GetRequiredService<RuntimeService>());
            InitializeComponent();
        }

        protected override async void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            await ViewModel.LoadAsync();
        }
    }
}
