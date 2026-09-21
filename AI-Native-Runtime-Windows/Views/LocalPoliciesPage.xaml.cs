using AI_Native_Runtime_Windows.Services;
using AI_Native_Runtime_Windows.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace AI_Native_Runtime_Windows.Views
{
    /// <summary>Local Runtime Policy review surface (INV-03 plan §4.7/§4.8/§10,
    /// Checkpoint INV-03D). Mirrors `PermissionGrantsPage`'s own
    /// construction/navigation idiom exactly.</summary>
    public sealed partial class LocalPoliciesPage : Page
    {
        public LocalPoliciesViewModel ViewModel { get; }

        public LocalPoliciesPage()
        {
            ViewModel = new LocalPoliciesViewModel(App.AppHost.Services.GetRequiredService<RuntimeService>());
            InitializeComponent();
        }

        protected override async void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            await ViewModel.LoadAsync();
        }
    }
}
