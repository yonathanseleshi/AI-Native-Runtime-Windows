using AI_Native_Runtime_Windows.Services;
using AI_Native_Runtime_Windows.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace AI_Native_Runtime_Windows.Views
{
    public sealed partial class ActivityPage : Page
    {
        public ActivityViewModel ViewModel { get; }

        public ActivityPage()
        {
            ViewModel = new ActivityViewModel(App.AppHost.Services.GetRequiredService<RuntimeService>());
            InitializeComponent();
        }

        protected override async void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            await ViewModel.LoadAsync();
        }
    }
}
