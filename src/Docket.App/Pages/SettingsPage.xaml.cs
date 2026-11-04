using Docket.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace Docket.App.Pages;

public sealed partial class SettingsPage : Page
{
    private bool _loading;

    public SettingsPage() => InitializeComponent();

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        _loading = true;
        var s = App.Settings;
        ServerBox.Text = s.ServerUrl;
        TokenBox.PlaceholderText = s.Token is null ? "tk_..." : "Saved. Paste a new token to replace it.";
        var projects = App.Window?.Projects ?? [];
        DefaultProject.ItemsSource = projects;
        DefaultProject.SelectedItem = projects.FirstOrDefault(p => p.Id == (App.Window?.DefaultProject?.Id ?? s.DefaultProjectId));
        ConfirmDeleteSwitch.IsOn = s.ConfirmDelete;
        ShowImportantSwitch.IsOn = s.ShowImportant;
        ShowPlannedSwitch.IsOn = s.ShowPlanned;
        ShowAllSwitch.IsOn = s.ShowAll;
        ShowCompletedSwitch.IsOn = s.ShowCompletedList;
        HideEmptySwitch.IsOn = s.HideEmptySmartLists;
        SoundSwitch.IsOn = s.ReminderSound;
        ThemeChoice.SelectedIndex = s.Theme switch { "light" => 1, "dark" => 2, _ => 0 };
        _loading = false;
    }

    private async void General_Changed(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        var s = App.Settings;
        s.ConfirmDelete = ConfirmDeleteSwitch.IsOn;
        s.ShowImportant = ShowImportantSwitch.IsOn;
        s.ShowPlanned = ShowPlannedSwitch.IsOn;
        s.ShowAll = ShowAllSwitch.IsOn;
        s.ShowCompletedList = ShowCompletedSwitch.IsOn;
        s.HideEmptySmartLists = HideEmptySwitch.IsOn;
        s.ReminderSound = SoundSwitch.IsOn;
        s.Save();
        if (App.Window is not null) await App.Window.RefreshCountsAsync();
    }

    private void Theme_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_loading) return;
        App.Settings.Theme = ThemeChoice.SelectedIndex switch { 1 => "light", 2 => "dark", _ => "system" };
        App.Settings.Save();
        App.Window?.ApplyTheme();
    }

    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        var url = ServerBox.Text.Trim();
        var token = TokenBox.Password.Trim();
        if (token.Length == 0) token = App.Settings.Token ?? "";
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || token.Length == 0)
        {
            Show(InfoBarSeverity.Warning, "Check the details", "Enter a full server address and an API token.");
            return;
        }

        SaveButton.IsEnabled = false;
        Busy.IsActive = true;
        try
        {
            using var probe = new VikunjaClient(uri, token);
            var version = await probe.GetServerVersionAsync();
            var projects = await probe.GetProjectsAsync();
            App.Settings.ServerUrl = url.TrimEnd('/');
            App.Settings.Token = token;
            App.Settings.Save();
            App.ReloadClient();
            TokenBox.Password = "";
            Show(InfoBarSeverity.Success, "Connected", $"Vikunja {version}, {projects.Count} lists.");
            if (App.Window is not null) await App.Window.OnConnectedAsync();
        }
        catch (Exception ex) when (ex is HttpRequestException or VikunjaException or TaskCanceledException)
        {
            Show(InfoBarSeverity.Error, "Couldn't connect", ex.Message);
        }
        finally
        {
            SaveButton.IsEnabled = true;
            Busy.IsActive = false;
        }
    }

    private async void DefaultProject_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || DefaultProject.SelectedItem is not VProject p) return;
        App.Settings.DefaultProjectId = p.Id;
        App.Settings.Save();
        if (App.Window is not null) await App.Window.LoadListsAsync();
    }

    private void Show(InfoBarSeverity severity, string title, string message)
    {
        Result.Severity = severity;
        Result.Title = title;
        Result.Message = message;
        Result.IsOpen = true;
    }
}
