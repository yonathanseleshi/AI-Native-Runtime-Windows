using AI_Native_Runtime_Windows.Models;
using AI_Native_Runtime_Windows.Services;
using AI_Native_Runtime_Windows.Services.Transport;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.UI.Xaml;

namespace AI_Native_Runtime_Windows
{
    /// <summary>
    /// Application entry point. Plan §4.5's fix: every service that could
    /// previously throw during construction (`AuthService` on a blank
    /// Firebase key) is now built once, here, inside a
    /// <see cref="Microsoft.Extensions.Hosting"/> DI container at process
    /// startup — `SignInPage` and every other view resolve their
    /// dependencies from <see cref="AppHost"/> rather than constructing
    /// them inline, so a missing/blank key becomes a state the sign-in
    /// surface renders (`AuthService.IsConfigured`), never an exception
    /// thrown while a XAML page's constructor runs.
    /// </summary>
    public partial class App : Application
    {
        /// <summary>The DI container for the whole application's lifetime. Not run as an
        /// actual hosted service loop (`RunAsync`) - WinUI has its own message loop via
        /// `OnLaunched`; this is used purely as a service container, built once here.</summary>
        public static IHost AppHost { get; private set; } = null!;

        public Window? MainAppWindow { get; private set; }

        public App()
        {
            InitializeComponent();

            AppHost = Host.CreateDefaultBuilder()
                .ConfigureAppConfiguration((_, config) =>
                {
                    config.SetBasePath(AppContext.BaseDirectory);
                    config.AddJsonFile("appsettings.json", optional: true, reloadOnChange: false);
                })
                .ConfigureServices((context, services) =>
                {
                    services.Configure<FirebaseOptions>(context.Configuration.GetSection(FirebaseOptions.SectionName));
                    services.Configure<RuntimeOptions>(context.Configuration.GetSection(RuntimeOptions.SectionName));

                    services.AddHttpClient();
                    services.AddSingleton<CredentialStore>();
                    services.AddSingleton<CursorStore>();

                    services.AddSingleton(sp =>
                    {
                        var http = sp.GetRequiredService<IHttpClientFactory>().CreateClient();
                        var credentialStore = sp.GetRequiredService<CredentialStore>();
                        var apiKey = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<FirebaseOptions>>().Value.ApiKey;
                        return new AuthService(http, credentialStore, apiKey);
                    });

                    services.AddSingleton<RuntimeService>();
                    services.AddSingleton<BackgroundServiceController>();
                })
                .Build();
        }

        protected override void OnLaunched(LaunchActivatedEventArgs args)
        {
            MainAppWindow = new MainWindow();
            MainAppWindow.Activate();
        }
    }
}
