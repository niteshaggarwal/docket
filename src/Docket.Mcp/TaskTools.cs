using System.ComponentModel;
using Docket.Core;
using Docket.Shared;
using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace Docket.Mcp;

/// <summary>
/// Task tools for Claude. Every result is plain text with task ids as #123, so Claude can
/// refer back to them. Errors come back as readable messages, never stack traces.
/// </summary>
[McpServerToolType]
public static class TaskTools
{
    internal const string DueHelp = "Due date: 2026-11-20, 2026-11-20T15:00, today, tomorrow, a weekday (fri), \"in 3 days\", 20/11 (day/month), or none to clear.";

    [McpServerTool(Name = "list_tasks", Title = "List tasks", ReadOnly = true, Idempotent = true)]
    [Description("List the user's tasks. Views: today (due today or overdue), upcoming (next 7 days), overdue, open (everything unfinished), done (recently finished), my_day (the user's My Day list), important (starred), planned (has a due date, grouped Earlier/Today/Tomorrow/This week/Later), all (every open task), completed. Optionally narrow to one list (project) or search words. Lines show ★ for important, My Day, steps progress, ↻ repeat and ⏰ next reminder.")]
    public static Task<string> ListTasks(DocketService svc, ConnectorOptions settings,
        [Description("today | upcoming | overdue | open | done | my_day | important | planned | all | completed. Default today.")] string view = "today",
        [Description("List (project) name or id to narrow to. Optional.")] string? project = null,
        [Description("Words to search in titles and notes. Optional.")] string? search = null,
        [Description("Maximum tasks to return, 1-200. Default 50.")] int limit = 50,
        CancellationToken ct = default) => Run(async () =>
    {
        var now = DateTimeOffset.Now;
        var max = Math.Clamp(limit, 1, 200);
        var key = view.Trim().ToLowerInvariant().Replace(" ", "_").Replace("-", "_");
        SmartList? smart = key switch
        {
            "my_day" or "myday" => SmartList.MyDay, "important" or "starred" => SmartList.Important,
            "planned" => SmartList.Planned, "all" => SmartList.All, "completed" => SmartList.Completed, _ => null,
        };
        if (smart is { } s && string.IsNullOrWhiteSpace(search))
        {
            var c = await svc.SmartAsync(s, s == SmartList.Planned ? SortMode.DueDate : SortMode.Custom, settings.DefaultProjectId, ct);
            if (project is not null)
            {
                var p = await svc.ResolveProjectAsync(project, ct);
                c = new ListContent(c.Open.Where(t => t.Task.ProjectId == p.Id).ToList(), c.Completed.Where(t => t.Task.ProjectId == p.Id).ToList());
            }
            c = new ListContent(c.Open.Take(max).ToList(), c.Completed.Take(max).ToList());
            return s == SmartList.Planned ? ToDoFormat.Planned(c.Open, now) : ToDoFormat.Content(c, now, "No tasks here.", 20);
        }
        var v = DocketService.ParseView(smart switch { SmartList.Completed => "done", null => view, _ => "open" });
        var list = await svc.ListAsync(v, project, search, max, ct);
        return ToDoFormat.List(list, now, "No tasks match.");
    });

    [McpServerTool(Name = "get_task", Title = "Get task", ReadOnly = true, Idempotent = true)]
    [Description("Full details of one task: important, My Day, due date, repeat, reminders, priority, categories, steps (numbered), notes, file attachments, its Kanban column and the board's columns, and comments.")]
    public static Task<string> GetTask(DocketService svc, [Description("Task id, e.g. 12")] long id, CancellationToken ct = default) =>
        Run(async () => ToDoFormat.Detail(await svc.DetailAsync(id, ct), DateTimeOffset.Now));

    [McpServerTool(Name = "add_task", Title = "Add task", Destructive = false)]
    [Description("Add a task. The title may include quick-add words, which are taken out of the title: today, tomorrow, a weekday, \"in 3 days\", 20/11 for the due date and !1 to !5 for priority (\"Send invoice friday !3\"). Without a list it goes to the user's default list (usually Inbox). Use set_my_day, set_important, steps, set_repeat and set_reminder afterwards for the rest.")]
    public static Task<string> AddTask(DocketService svc, ConnectorOptions settings,
        [Description("Task title, optionally with quick-add words.")] string title,
        [Description("List (project) name or id. Optional.")] string? project = null,
        [Description(DueHelp + " Optional; overrides quick-add words.")] string? due = null,
        [Description("0 none, 1 low, 2 medium, 3 high, 4 urgent, 5 do now. Optional.")] int? priority = null,
        [Description("Notes for the task. Optional.")] string? description = null,
        [Description("Category (label) names to attach; missing ones are created. Optional.")] string[]? labels = null,
        CancellationToken ct = default) => Run(async () =>
    {
        var t = await svc.AddAsync(title, project, due, priority, description, labels, settings.DefaultProjectId, ct);
        return "Added " + ToDoFormat.Line(t, DateTimeOffset.Now);
    });

    [McpServerTool(Name = "update_task", Title = "Update task", Destructive = false, Idempotent = true)]
    [Description("Change a task's title, due date, priority or list. Only the fields you pass change. To change notes without losing steps use set_notes; `description` here replaces the whole description including steps.")]
    public static Task<string> UpdateTask(DocketService svc,
        [Description("Task id.")] long id,
        [Description("New title. Optional.")] string? title = null,
        [Description("Replaces the entire description, including steps. Prefer set_notes. Optional.")] string? description = null,
        [Description(DueHelp + " Optional.")] string? due = null,
        [Description("0-5. Optional.")] int? priority = null,
        [Description("Move to this list (name or id). Optional.")] string? project = null,
        CancellationToken ct = default) => Run(async () =>
    {
        var t = await svc.UpdateAsync(id, title, description, due, priority, project, ct);
        return "Updated " + ToDoFormat.Line(t, DateTimeOffset.Now);
    });

    [McpServerTool(Name = "complete_task", Title = "Complete task", Destructive = false, Idempotent = false)]
    [Description("Mark one or more tasks done, or reopen them with done=false. A repeating task moves to its next date and stays open, with its steps unticked.")]
    public static Task<string> CompleteTask(DocketService svc,
        [Description("Task ids.")] long[] ids,
        [Description("true marks done (default), false reopens.")] bool done = true,
        CancellationToken ct = default) => Run(async () =>
    {
        if (ids.Length == 0) throw new DocketException("Give at least one task id.");
        var lines = new List<string>();
        foreach (var id in ids)
        {
            var t = await svc.SetDoneAsync(id, done, ct);
            var verb = !done ? "Reopened " : t.Task.Done ? "Done " : "Done, next occurrence: ";
            lines.Add(verb + ToDoFormat.Line(t, DateTimeOffset.Now));
        }
        return string.Join("\n", lines);
    });

    [McpServerTool(Name = "move_task", Title = "Move task to column", Destructive = false, Idempotent = true)]
    [Description("Move a task to a column of its list's Kanban board, by column name (e.g. Doing). Moving into the Done column marks it done.")]
    public static Task<string> MoveTask(DocketService svc,
        [Description("Task id.")] long id,
        [Description("Column name, e.g. To-Do, Doing, Done.")] string column,
        CancellationToken ct = default) => Run(async () =>
        "Moved " + ToDoFormat.Line(await svc.MoveAsync(id, column, ct), DateTimeOffset.Now));

    [McpServerTool(Name = "add_comment", Title = "Comment on task", Destructive = false)]
    [Description("Add a comment to a task, for progress notes or context.")]
    public static Task<string> AddComment(DocketService svc, [Description("Task id.")] long id, [Description("Comment text.")] string text, CancellationToken ct = default) =>
        Run(async () => { await svc.CommentAsync(id, text, ct); return $"Comment added to #{id}."; });

    [McpServerTool(Name = "add_label", Title = "Add category", Destructive = false, Idempotent = true)]
    [Description("Attach one category (label) to a task, keeping its others. The category is created if it doesn't exist. To replace all categories use set_categories.")]
    public static Task<string> AddLabel(DocketService svc, [Description("Task id.")] long id, [Description("Category name.")] string label, CancellationToken ct = default) =>
        Run(async () => $"Labelled #{id} \"{(await svc.AddLabelAsync(id, label, ct)).Title}\".");

    [McpServerTool(Name = "list_projects", Title = "List lists", ReadOnly = true, Idempotent = true)]
    [Description("All lists (projects, with groups shown as Parent / Child) with their open-task counts and Kanban columns.")]
    public static Task<string> ListProjects(DocketService svc, CancellationToken ct = default) => Run(async () =>
    {
        var ps = await svc.ProjectOverviewAsync(ct);
        return ps.Count == 0 ? "No lists yet." : string.Join("\n", ps.Select(p =>
            $"{p.Path} (id {p.Project.Id}, {p.OpenTasks} open){(p.Columns.Count > 0 ? " · columns: " + string.Join(", ", p.Columns) : "")}"));
    });

    [McpServerTool(Name = "get_board", Title = "Get board", ReadOnly = true, Idempotent = true)]
    [Description("A list's Kanban board: each column with its tasks.")]
    public static Task<string> GetBoard(DocketService svc, [Description("List (project) name or id.")] string project, CancellationToken ct = default) => Run(async () =>
    {
        var p = await svc.ResolveProjectAsync(project, ct);
        return TaskFormat.Board(p.Title, await svc.BoardAsync(project, ct), DateTimeOffset.Now);
    });

    [McpServerTool(Name = "create_project", Title = "Create list", Destructive = false)]
    [Description("Create a list (a project; it gets a list view and a Kanban board with To-Do, Doing and Done). Optionally inside a parent list, which then acts as a group. Same as create_list.")]
    public static Task<string> CreateProject(DocketService svc,
        [Description("List name.")] string title,
        [Description("Parent list (group) name or id. Optional.")] string? parent = null,
        CancellationToken ct = default) => Run(async () =>
    {
        var p = await svc.CreateProjectAsync(title, parent, ct);
        return $"Created list \"{p.Title}\" (id {p.Id}).";
    });

    /// <summary>Turns known failures into readable tool errors.</summary>
    /// <summary>The server address, set at start-up, so a stopped local server can be restarted.</summary>
    internal static string ServerUrl { get; set; } = "";

    internal static async Task<string> Run(Func<Task<string>> body)
    {
        try { return await body(); }
        catch (HttpRequestException ex) when (OperatingSystem.IsWindows() && LocalServer.IsConnectionRefused(ex) && LocalServer.IsLocal(ServerUrl))
        {
            // The server on this PC has stopped: start it and try once more before reporting a problem.
            if (!await LocalServer.EnsureRunningAsync(ServerUrl)) throw new McpException(LocalServer.StillDownMessage(ServerUrl));
            try { return await body(); }
            catch (DocketException again) { throw new McpException(again.Message); }
            catch (VikunjaException again) { throw new McpException(again.Message); }
        }
        catch (DocketException ex) { throw new McpException(ex.Message); }
        catch (VikunjaException ex) { throw new McpException(ex.Message); }
        catch (HttpRequestException ex) { throw new McpException($"Can't reach Vikunja at the configured address ({ex.Message}). Is it running?"); }
        catch (TaskCanceledException) { throw new McpException("Vikunja didn't answer in time."); }
    }
}
