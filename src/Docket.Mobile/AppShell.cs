using CommunityToolkit.Maui.Core;
using Docket.Mobile.Views;

namespace Docket.Mobile;

/// <summary>Navigation: sign-in and home at the root; list, task and settings pages pushed on top.</summary>
public sealed class AppShell : Shell
{
    public AppShell(IServiceProvider services)
    {
        FlyoutBehavior = FlyoutBehavior.Disabled;
        Items.Add(new ShellContent { Route = "signin", ContentTemplate = new DataTemplate(() => services.GetRequiredService<SignInPage>()) });
        Items.Add(new ShellContent { Route = "home", ContentTemplate = new DataTemplate(() => services.GetRequiredService<HomePage>()) });
        Routing.RegisterRoute("list", typeof(ListPage));
        Routing.RegisterRoute("task", typeof(TaskPage));
        Routing.RegisterRoute("settings", typeof(SettingsPage));
        Routing.RegisterRoute("overview", typeof(OverviewPage));
        Routing.RegisterRoute("people", typeof(PeoplePage));
        Routing.RegisterRoute("share", typeof(SharePage));
        Routing.RegisterRoute("liststyle", typeof(ListStylePage));
        Routing.RegisterRoute("aisettings", typeof(AiSettingsPage));

        // Top bar and status bar take the page colour, like a WinUI window with Mica.
        this.SetAppThemeColor(BackgroundColorProperty, Color.FromArgb("#F3F3F3"), Color.FromArgb("#202020"));
        this.SetAppThemeColor(ForegroundColorProperty, Color.FromArgb("#1A1A1A"), Colors.White);
        SetNavBarHasShadow(this, false);
        // The Android activity (and Application.Current) exist only once the shell is on screen.
        Loaded += (_, _) =>
        {
            ApplyStatusBar();
            if (!_themeHooked && Application.Current is { } app) { app.RequestedThemeChanged += (_, _) => ApplyStatusBar(); _themeHooked = true; }
        };
    }

    private bool _themeHooked;

    public static void ApplyStatusBar()
    {
#if ANDROID
        try
        {
            var dark = Application.Current?.RequestedTheme == AppTheme.Dark;
            CommunityToolkit.Maui.Core.Platform.StatusBar.SetColor(Color.FromArgb(dark ? "#202020" : "#F3F3F3"));
            CommunityToolkit.Maui.Core.Platform.StatusBar.SetStyle(dark ? StatusBarStyle.LightContent : StatusBarStyle.DarkContent);
        }
        catch (InvalidOperationException) { /* no activity yet; Loaded applies it */ }
#endif
    }
}
