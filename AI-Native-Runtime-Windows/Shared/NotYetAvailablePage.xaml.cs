using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace AI_Native_Runtime_Windows.Shared
{
    /// <summary>
    /// Plan §4.18: every surface with no backing implementation states
    /// plainly that the capability is not yet available, naming the
    /// future wave where known - never a placeholder screen that implies
    /// a feature exists. Applications, Permissions, and Security all
    /// render this today, matching MAC's own treatment of the identical
    /// gap (Phase 1 report §11): these are genuinely unbacked by any
    /// CORE surface yet, on either platform, not a Windows-specific
    /// shortfall.
    /// </summary>
    public sealed partial class NotYetAvailablePage : Page
    {
        public NotYetAvailablePage()
        {
            InitializeComponent();
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            if (e.Parameter is (string title, string body))
            {
                TitleText.Text = title;
                BodyText.Text = body;
            }
        }
    }
}
