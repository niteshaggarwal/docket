using Docket.Mobile.Services;
using Docket.Mobile.ViewModels;

namespace Docket.Mobile;

public partial class App : Application
{
    private readonly Session _session;
    private readonly AppShell _shell;

    public App(Session session, AppShell shell)
    {
        InitializeComponent();
        _session = session;
        _shell = shell;
        Prefs.ApplyTheme();
        // The add bar rides above the keyboard instead of the whole page sliding up.
        Microsoft.Maui.Controls.PlatformConfiguration.AndroidSpecific.Application.UseWindowSoftInputModeAdjust(
            this.On<Microsoft.Maui.Controls.PlatformConfiguration.Android>(), Microsoft.Maui.Controls.PlatformConfiguration.AndroidSpecific.WindowSoftInputModeAdjust.Resize);
    }

    protected override Window CreateWindow(IActivationState? activationState) => new(_shell);

    protected override async void OnStart()
    {
        base.OnStart();
        var signedIn = await _session.RestoreAsync();
        await Shell.Current.GoToAsync(signedIn ? "//home" : "//signin");
    }

    protected override void OnResume()
    {
        base.OnResume();
        if (_session.Service is { } svc) _ = Reminders.RescheduleAsync(svc);
    }
}
