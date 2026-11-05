using Docket.Core;
using Microsoft.UI.Xaml;

namespace Docket.App;

public partial class App : Application
{
    public App()
    {
        Settings = AppSettings.Load();
        // Apply a chosen theme app-wide, so brushes looked up from code match the window.
        if (Settings.Theme == "light") RequestedTheme = ApplicationTheme.Light;
        else if (Settings.Theme == "dark") RequestedTheme = ApplicationTheme.Dark;
        InitializeComponent();
        UnhandledException += (_, e) =>
        {
            // Keep the app open: log the problem and show it in the error bar instead of closing.
            try
            {
                var dir = Path.Combine(Path.GetTempPath(), "Docket");
                Directory.CreateDirectory(dir);
                File.AppendAllText(Path.Combine(dir, "errors.log"), $"{DateTimeOffset.Now:O} {e.Exception}\n\n");
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            e.Handled = true;
            Window?.ShowError("Something went wrong", e.Message);
        };
    }

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
        ReloadClient();
        Window = new MainWindow();
        Window.Activate();
    }
}
