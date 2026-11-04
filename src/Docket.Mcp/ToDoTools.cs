using System.ComponentModel;
using Docket.Core;
using Docket.Shared;
using ModelContextProtocol.Server;
using static Docket.Mcp.TaskTools;

namespace Docket.Mcp;

/// <summary>To Do style tools: My Day, important, steps, repeat, reminders, categories, notes, lists. None delete.</summary>
[McpServerToolType]
public static class ToDoTools
{
    [McpServerTool(Name = "my_day", Title = "My Day", ReadOnly = true, Idempotent = true)]
    [Description("The user's My Day list (resets each day), with its completed tasks. With suggestions=true also lists suggested tasks to add (overdue, due today, upcoming, recently added).")]
    public static Task<string> MyDay(DocketService svc, ConnectorOptions settings,
        [Description("Also show suggestions. Default false.")] bool suggestions = false,
        CancellationToken ct = default) => Run(async () =>
    {
        var now = DateTimeOffset.Now;
        var text = "My Day\n" + ToDoFormat.Content(await svc.SmartAsync(SmartList.MyDay, SortMode.Custom, settings.DefaultProjectId, ct), now, "Empty.", 20);
        if (suggestions) text += "\n\nSuggestions\n" + ToDoFormat.Suggestions(await svc.MyDaySuggestionsAsync(ct), now);
        return text;
    });

    [McpServerTool(Name = "set_my_day", Title = "Add to or remove from My Day", Destructive = false, Idempotent = true)]
    [Description("Add tasks to My Day (in_my_day=true, default) or remove them (false).")]
    public static Task<string> SetMyDay(DocketService svc, [Description("Task ids.")] long[] ids,
        [Description("true adds, false removes.")] bool in_my_day = true, CancellationToken ct = default) => Run(async () =>
    {
        if (ids.Length == 0) throw new DocketException("Give at least one task id.");
        var lines = new List<string>();
        foreach (var id in ids) lines.Add((in_my_day ? "In My Day: " : "Removed from My Day: ") + ToDoFormat.Line(await svc.SetMyDayAsync(id, in_my_day, ct), DateTimeOffset.Now));
        return string.Join("\n", lines);
    });

    [McpServerTool(Name = "set_important", Title = "Mark important", Destructive = false, Idempotent = true)]
    [Description("Mark tasks important (starred) or not.")]
    public static Task<string> SetImportant(DocketService svc, [Description("Task ids.")] long[] ids,
        [Description("true stars, false unstars.")] bool important = true, CancellationToken ct = default) => Run(async () =>
    {
        if (ids.Length == 0) throw new DocketException("Give at least one task id.");
        var lines = new List<string>();
        foreach (var id in ids) lines.Add(ToDoFormat.Line(await svc.SetImportantAsync(id, important, ct), DateTimeOffset.Now));
        return string.Join("\n", lines);
    });

    [McpServerTool(Name = "steps", Title = "Edit steps", Destructive = false)]
    [Description("Work with a task's steps (a checklist inside the task). action: add (needs text), complete / uncomplete / remove (need index, 1-based as shown by get_task). Returns the updated steps.")]
    public static Task<string> Steps(DocketService svc,
        [Description("Task id.")] long id,
        [Description("add | complete | uncomplete | remove")] string action,
        [Description("Step text, for add.")] string? text = null,
        [Description("Step number (1-based), for complete, uncomplete and remove.")] int? index = null,
        CancellationToken ct = default) => Run(async () =>
    {
        int Index() => index is { } i && i >= 1 ? i - 1 : throw new DocketException("Give the step's number (1-based) in index.");
        var t = action.Trim().ToLowerInvariant() switch
        {
            "add" => await svc.AddStepAsync(id, text ?? "", ct),
            "complete" or "done" or "check" => await svc.SetStepDoneAsync(id, Index(), true, ct),
            "uncomplete" or "undo" or "uncheck" => await svc.SetStepDoneAsync(id, Index(), false, ct),
            "remove" or "delete" => await svc.RemoveStepAsync(id, Index(), ct),
            _ => throw new DocketException("action must be add, complete, uncomplete or remove."),
        };
        var steps = TaskNotes.Parse(t.Task.Description).Steps;
        return $"#{id} {t.Task.Title}: " + (steps.Count == 0 ? "no steps." : "\n" + string.Join("\n", steps.Select((s, i) => $"{i + 1}. [{(s.Done ? "x" : " ")}] {s.Title}")));
    });

    [McpServerTool(Name = "set_repeat", Title = "Set repeat", Destructive = false, Idempotent = true)]
    [Description("Make a task repeat, or stop it repeating. When it's completed it moves to the next date and stays open. A task without a due date gets today as its due date.")]
    public static Task<string> SetRepeat(DocketService svc, [Description("Task id.")] long id,
        [Description("daily | weekdays | weekly | monthly | yearly | \"every N days|weeks|months|years\" | none")] string repeat,
        CancellationToken ct = default) => Run(async () =>
    {
        RepeatRule? rule = null;
        var s = repeat.Trim();
        if (!s.Equals("none", StringComparison.OrdinalIgnoreCase) && !s.Equals("never", StringComparison.OrdinalIgnoreCase) && !RepeatRule.TryParse(s, out rule!))
            throw new DocketException($"Can't read \"{repeat}\". Use daily, weekdays, weekly, monthly, yearly, \"every 3 weeks\" or none.");
        var t = await svc.SetRepeatAsync(id, rule, ct);
        return (rule is null ? "No longer repeats: " : $"Repeats {rule.Describe().ToLowerInvariant()}: ") + ToDoFormat.Line(t, DateTimeOffset.Now);
    });

    [McpServerTool(Name = "set_reminder", Title = "Set reminders", Destructive = false, Idempotent = true)]
    [Description("Replace a task's reminders. Each time: ISO date-time (2026-11-20T09:00), or natural: \"tomorrow 9:00\", \"in 2 hours\", \"fri 5pm\", \"today 17:30\", \"20/11 14:00\". An empty list clears all reminders. Reminders show as Windows notifications on the user's PC.")]
    public static Task<string> SetReminder(DocketService svc, [Description("Task id.")] long id,
        [Description("Reminder times; empty clears.")] string[] times, CancellationToken ct = default) => Run(async () =>
    {
        var now = DateTimeOffset.Now;
        var parsed = (times ?? []).Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => WhenParser.Parse(x, now)).ToList();
        var t = await svc.SetRemindersAsync(id, parsed, ct);
        return (parsed.Count == 0 ? "Reminders cleared: " : $"Reminders: {string.Join(", ", parsed.Order().Select(p => ToDoFormat.When(p, now)))}. ") + ToDoFormat.Line(t, now);
    });

    [McpServerTool(Name = "set_categories", Title = "Set categories", Destructive = false, Idempotent = true)]
    [Description("Replace a task's categories (labels) with these names; missing ones are created. An empty list clears them. My Day and repeat settings are kept.")]
    public static Task<string> SetCategories(DocketService svc, [Description("Task id.")] long id,
        [Description("Category names.")] string[] categories, CancellationToken ct = default) => Run(async () =>
        "Categories set: " + ToDoFormat.Line(await svc.SetCategoriesAsync(id, categories ?? [], ct), DateTimeOffset.Now));

    [McpServerTool(Name = "set_notes", Title = "Set notes", Destructive = false, Idempotent = true)]
    [Description("Replace a task's notes (plain text). Its steps are kept. Read the task first if you mean to add to the existing notes.")]
    public static Task<string> SetNotes(DocketService svc, [Description("Task id.")] long id,
        [Description("The new notes, plain text. Empty clears them.")] string notes, CancellationToken ct = default) => Run(async () =>
        "Notes saved: " + ToDoFormat.Line(await svc.SetNotesAndStepsAsync(id, n => n with { Notes = notes ?? "" }, ct), DateTimeOffset.Now));

    [McpServerTool(Name = "create_list", Title = "Create list", Destructive = false)]
    [Description("Create a list, optionally inside a group (an existing list used as its parent).")]
    public static Task<string> CreateList(DocketService svc, [Description("List name.")] string title,
        [Description("Group (parent list) name or id. Optional.")] string? group = null, CancellationToken ct = default) => Run(async () =>
    {
        var p = await svc.CreateProjectAsync(title, group, ct);
        return $"Created list \"{p.Title}\" (id {p.Id}){(group is null ? "" : $" in {group}")}.";
    });
}
