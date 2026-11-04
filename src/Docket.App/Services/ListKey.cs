using Docket.Core;

namespace Docket.App.Services;

/// <summary>What the list page shows: a smart list, one of the user's lists, or search results.</summary>
public sealed record ListKey(SmartList? Smart = null, long ProjectId = 0, string? Search = null)
{
    public static ListKey Of(SmartList s) => new(s);
    public static ListKey Project(long id) => new(ProjectId: id);
    public static ListKey Find(string q) => new(Search: q);

    public bool IsProject => Smart is null && Search is null;
    public string SettingsKey => Search is not null ? "search" : Smart is { } s ? $"smart:{s}" : $"list:{ProjectId}";

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
