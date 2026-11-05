namespace Docket.Core;

public enum SlashKind { None, Search, Done, MyDay, Important, Ai, Help }

/// <summary>
/// Commands typed into the add-task bar: "/s words" searches, "/x words" marks every match done,
/// "/m" adds matches to My Day, "/i" stars them, "/ai text" asks the AI to plan tasks, "/?" lists them.
/// Anything else is a new task.
/// </summary>
public sealed record SlashCommand(SlashKind Kind, string Argument)
{
    public static readonly IReadOnlyList<(string Command, string Help)> All =
    [
        ("/s", "Search tasks"),
        ("/x", "Mark the tasks found done"),
        ("/m", "Add the tasks found to My Day"),
        ("/i", "Star the tasks found"),
        ("/ai", "Describe what you need; the AI plans the tasks"),
        ("/?", "Show these commands"),
    ];

    public static SlashCommand Parse(string? input)
    {
        var text = (input ?? "").Trim();
        if (!text.StartsWith('/')) return new(SlashKind.None, text);
        var space = text.IndexOf(' ');
        var verb = (space < 0 ? text : text[..space]).ToLowerInvariant();
        var arg = space < 0 ? "" : text[(space + 1)..].Trim();
        var kind = verb switch
        {
            "/s" or "/search" => SlashKind.Search,
            "/x" or "/done" => SlashKind.Done,
            "/m" or "/myday" => SlashKind.MyDay,
            "/i" or "/star" => SlashKind.Important,
            "/ai" => SlashKind.Ai,
            "/?" or "/help" or "/" => SlashKind.Help,
            _ => SlashKind.None,
        };
        return kind == SlashKind.None ? new(SlashKind.None, text) : new(kind, arg);
    }

    /// <summary>True for commands that act on the tasks a search finds.</summary>
    public bool ActsOnMatches => Kind is SlashKind.Done or SlashKind.MyDay or SlashKind.Important;
}
