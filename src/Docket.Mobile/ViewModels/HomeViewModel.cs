using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Docket.Core;
using Docket.Mobile.Services;

namespace Docket.Mobile.ViewModels;

/// <summary>Home: search, smart lists with counts, the user's lists and groups.</summary>
public sealed partial class HomeViewModel(Session session) : ObservableObject
{
    public ObservableCollection<ListEntry> Smart { get; } = [];
    public ObservableCollection<ListEntry> Lists { get; } = [];

    [ObservableProperty] public partial bool IsBusy { get; set; }
    [ObservableProperty] public partial string? Error { get; set; }
    [ObservableProperty] public partial string Search { get; set; } = "";

    [RelayCommand]
    public async Task RefreshAsync()
    {
        if (session.Service is not { } svc || _refreshing) return;
        _refreshing = true;
        IsBusy = true;
        Error = null;
        try
        {
            await svc.EnsureMyDayFreshAsync();
            var projects = await svc.ProjectsAsync(refresh: true);
            var inbox = await svc.DefaultProjectAsync(Preferences.Default.Get("default.project", 0L));
            var open = await svc.Client.GetTasksAsync("done = false");

            Smart.Clear();
            void AddSmart(SmartList s, string glyph, int count) =>
                Smart.Add(new ListEntry { Key = $"smart:{s}", Title = s == SmartList.Tasks ? "Tasks" : Titles.Of(s), Glyph = glyph, Count = count });
            AddSmart(SmartList.MyDay, "", open.Count(DocketService.InMyDay));
            if (Prefs.ShowImportant) AddSmart(SmartList.Important, "", open.Count(t => t.IsFavorite));
            if (Prefs.ShowPlanned) AddSmart(SmartList.Planned, "", open.Count(t => t.Due is not null));
            if (Prefs.ShowAll) AddSmart(SmartList.All, "", open.Count);
            if (Prefs.ShowCompleted) AddSmart(SmartList.Completed, "", 0);
            AddSmart(SmartList.Tasks, "", open.Count(t => t.ProjectId == inbox.Id));

            Lists.Clear();
            foreach (var p in projects.Where(p => p.ParentProjectId == 0 && p.Id != inbox.Id))
            {
                var children = projects.Where(c => c.ParentProjectId == p.Id).ToList();
                Lists.Add(new ListEntry
                {
                    Key = $"list:{p.Id}", Title = p.Title, Glyph = children.Count > 0 ? "" : "",
                    Tint = p.HexColor.Length == 6 ? TaskRow.ColorOf(p.HexColor) : null, IsGroup = children.Count > 0,
                    Count = open.Count(t => t.ProjectId == p.Id),
                });
                foreach (var c in children)
                    Lists.Add(new ListEntry { Key = $"list:{c.Id}", Title = c.Title, Glyph = "", Indent = 1, Tint = c.HexColor.Length == 6 ? TaskRow.ColorOf(c.HexColor) : null, Count = open.Count(t => t.ProjectId == c.Id) });
            }
            _ = Reminders.RescheduleAsync(svc);
        }
        catch (Exception ex) when (ex is HttpRequestException or VikunjaException or DocketException or TaskCanceledException)
        {
            Error = ex is HttpRequestException ? $"Can't reach {session.ServerUrl}. Is Tailscale on?" : ex.Message;
        }
        finally { IsBusy = false; _refreshing = false; }
    }

    private bool _refreshing;

    [RelayCommand]
    private Task OpenAsync(ListEntry entry) => Shell.Current.GoToAsync($"list?key={Uri.EscapeDataString(entry.Key)}");

    [RelayCommand]
    private Task SearchAsync() => string.IsNullOrWhiteSpace(Search) ? Task.CompletedTask
        : Shell.Current.GoToAsync($"list?key={Uri.EscapeDataString("search:" + Search.Trim())}");

    [RelayCommand]
    private async Task NewListAsync()
    {
        if (session.Service is not { } svc) return;
        var name = await Shell.Current.DisplayPromptAsync("New list", "List name", "Create", "Cancel", "Untitled list");
        if (string.IsNullOrWhiteSpace(name)) return;
        try
        {
            var p = await svc.CreateProjectAsync(name, null);
            await RefreshAsync();
            await Shell.Current.GoToAsync($"list?key={Uri.EscapeDataString($"list:{p.Id}")}");
        }
        catch (Exception ex) when (ex is HttpRequestException or VikunjaException or DocketException) { Error = ex.Message; }
    }

    [RelayCommand]
    private Task SettingsAsync() => Shell.Current.GoToAsync("settings");
}

public static class Titles
{
    public static string Of(SmartList s) => s switch
    {
        SmartList.MyDay => "My Day", SmartList.Important => "Important", SmartList.Planned => "Planned",
        SmartList.All => "All", SmartList.Completed => "Completed", _ => "Tasks",
    };
}

/// <summary>Phone-side preferences (smart list visibility, theme, sort per list).</summary>
public static class Prefs
{
    public static bool ShowImportant { get => Preferences.Default.Get("show.important", true); set => Preferences.Default.Set("show.important", value); }
    public static bool ShowPlanned { get => Preferences.Default.Get("show.planned", true); set => Preferences.Default.Set("show.planned", value); }
    public static bool ShowAll { get => Preferences.Default.Get("show.all", true); set => Preferences.Default.Set("show.all", value); }
    public static bool ShowCompleted { get => Preferences.Default.Get("show.completed", true); set => Preferences.Default.Set("show.completed", value); }
    public static bool ConfirmDelete { get => Preferences.Default.Get("confirm.delete", true); set => Preferences.Default.Set("confirm.delete", value); }
    public static string Theme { get => Preferences.Default.Get("theme", "system"); set => Preferences.Default.Set("theme", value); }
    public static SortMode SortFor(string key) => Enum.TryParse<SortMode>(Preferences.Default.Get("sort." + key, ""), out var s) ? s : key == "smart:Planned" ? SortMode.DueDate : SortMode.Custom;
    public static void SetSort(string key, SortMode s) => Preferences.Default.Set("sort." + key, s.ToString());
    public static bool HideCompleted(string key) => Preferences.Default.Get("hidedone." + key, false);
    public static void SetHideCompleted(string key, bool v) => Preferences.Default.Set("hidedone." + key, v);

    public static void ApplyTheme()
    {
        Application.Current!.UserAppTheme = Theme switch { "light" => AppTheme.Light, "dark" => AppTheme.Dark, _ => AppTheme.Unspecified };
        AppShell.ApplyStatusBar();
    }
}
