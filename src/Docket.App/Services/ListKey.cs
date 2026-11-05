using Docket.Core;

namespace Docket.App.Services;

/// <summary>Extra views the app adds next to the To Do smart lists.</summary>
public enum ExtraView { None, Upcoming, Open }

/// <summary>How a list is shown.</summary>
public enum ListViewMode { List, Table, Gantt, Board }

/// <summary>What the list page shows: a smart list, one of the user's lists, an extra view, or search results.</summary>
public sealed record ListKey(SmartList? Smart = null, long ProjectId = 0, string? Search = null, ExtraView Extra = ExtraView.None)
{
    public static ListKey Of(SmartList s) => new(s);
    public static ListKey Project(long id) => new(ProjectId: id);
    public static ListKey Find(string q) => new(Search: q);
    public static ListKey Of(ExtraView v) => new(Extra: v);

    public bool IsProject => Smart is null && Search is null && Extra == ExtraView.None;
    public string SettingsKey => Search is not null ? "search" : Extra != ExtraView.None ? $"view:{Extra}" : Smart is { } s ? $"smart:{s}" : $"list:{ProjectId}";

    public static string SmartTitle(SmartList s) => s switch
    {
        SmartList.MyDay => "My Day", SmartList.Important => "Important", SmartList.Planned => "Planned",
        SmartList.All => "All", SmartList.Completed => "Completed", _ => "Tasks",
    };

    public static string SmartGlyph(SmartList s) => s switch
    {
        SmartList.MyDay => "", SmartList.Important => "", SmartList.Planned => "",
        SmartList.All => "", SmartList.Completed => "", _ => "",
    };
}

/// <summary>The view chosen per list, and which tree rows are open, remembered while the app runs.</summary>
public static class ViewPrefs
{
    private static readonly Dictionary<string, ListViewMode> Modes = [];
    private static readonly Dictionary<string, HashSet<string>> Expanded = [];

    /// <summary>The view chosen for a list; remembered in the settings file so it survives a restart.</summary>
    public static ListViewMode Get(string key) =>
        Modes.TryGetValue(key, out var m) ? m
        : App.Settings.ListViews.TryGetValue(key, out var s) && Enum.TryParse<ListViewMode>(s, out var saved) ? Modes[key] = saved
        : ListViewMode.List;

    public static void Set(string key, ListViewMode mode)
    {
        Modes[key] = mode;
        if (mode == ListViewMode.List) App.Settings.ListViews.Remove(key); else App.Settings.ListViews[key] = mode.ToString();
        App.Settings.Save();
    }

    public static HashSet<string> ExpandedOf(string key)
    {
        if (!Expanded.TryGetValue(key, out var set)) Expanded[key] = set = [];
        return set;
    }
}
