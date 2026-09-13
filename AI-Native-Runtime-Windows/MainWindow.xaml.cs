using Microsoft.UI.Xaml;

namespace AI_Native_Runtime_Windows
{
    /// <summary>The application's single top-level window. Always starts at the sign-in
    /// page (plan §4.5/§4.3): a CORE session token is never persisted (`RuntimeService`'s
    /// own doc comment), so every launch re-establishes one, even when a Firebase
    /// refresh token can be restored without asking the user to type a password again -
    /// `SignInPage` itself decides that shortcut.</summary>
    public sealed partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
            RootFrame.Navigate(typeof(Views.SignInPage));
        }

        /// <summary>Navigates back to sign-in - called after Settings' "Sign Out," which
        /// discards only the local Firebase credential (`AuthService.SignOut`'s own
        /// doc comment: it never touches CORE session/control-channel state).</summary>
        public void NavigateToSignIn()
        {
            RootFrame.Navigate(typeof(Views.SignInPage));
        }

        public void NavigateToShell()
        {
            RootFrame.Navigate(typeof(Navigation.ShellPage));
        }
    }
}
