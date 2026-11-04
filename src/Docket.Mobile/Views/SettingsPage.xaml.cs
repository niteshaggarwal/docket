using Docket.Mobile.Services;
using Docket.Mobile.ViewModels;

namespace Docket.Mobile.Views;

public partial class SettingsPage : ContentPage
{
    private readonly Session _session;
    private bool _loading;

    public SettingsPage(Session session)
    {
        InitializeComponent();
        _session = session;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        _loading = true;
        ConfirmSwitch.IsToggled = Prefs.ConfirmDelete;
        ImportantSwitch.IsToggled = Prefs.ShowImportant;
        PlannedSwitch.IsToggled = Prefs.ShowPlanned;
        AllSwitch.IsToggled = Prefs.ShowAll;
        CompletedSwitch.IsToggled = Prefs.ShowCompleted;
        (Prefs.Theme switch { "light" => ThemeLight, "dark" => ThemeDark, _ => ThemeSystem }).IsChecked = true;
        ServerText.Text = $"Signed in to {_session.ServerUrl}";
        VersionText.Text = $"Docket {AppInfo.Current.VersionString} ({AppInfo.Current.BuildString})";
        _loading = false;
    }

    private void Changed(object? sender, ToggledEventArgs e)
    {
        if (_loading) return;
        Prefs.ConfirmDelete = ConfirmSwitch.IsToggled;
        Prefs.ShowImportant = ImportantSwitch.IsToggled;
        Prefs.ShowPlanned = PlannedSwitch.IsToggled;
        Prefs.ShowAll = AllSwitch.IsToggled;
        Prefs.ShowCompleted = CompletedSwitch.IsToggled;
    }

    private void Theme_Changed(object? sender, CheckedChangedEventArgs e)
    {
        if (_loading || !e.Value) return;
        Prefs.Theme = ThemeLight.IsChecked ? "light" : ThemeDark.IsChecked ? "dark" : "system";
        Prefs.ApplyTheme();
    }

    private async void SignOut_Clicked(object? sender, EventArgs e)
    {
        if (!await DisplayAlertAsync("Sign out?", "You can sign in again with your Vikunja username and password.", "Sign out", "Cancel")) return;
        _session.SignOut();
        await Shell.Current.GoToAsync("//signin");
    }
}
