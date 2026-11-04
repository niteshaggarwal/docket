using Docket.Core;
using Microsoft.UI.Xaml;

namespace Docket.App;

public partial class App : Application
{
    public App() => InitializeComponent();

    public static AppSettings Settings { get; private set; } = new();
    public static VikunjaClient? Client { get; private set; }
    public static DocketService? Service { get; private set; }
    public static MainWindow? Window { get; private set; }

    /// <summary>Recreates the API client after settings change.</summary>
    public static void ReloadClient()
    {
        Client?.Dispose();
        Client = Settings.CreateClient();
        Service = Client is null ? null : new DocketService(Client) { State = Settings };
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        Settings = AppSettings.Load();
        ReloadClient();
        Window = new MainWindow();
        Window.Activate();
    }
}
