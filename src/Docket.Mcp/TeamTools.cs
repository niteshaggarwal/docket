using System.ComponentModel;
using System.Text;
using Docket.Core;
using Docket.Shared;
using ModelContextProtocol.Server;
using static Docket.Mcp.TaskTools;

namespace Docket.Mcp;

/// <summary>Subtasks, people and the overview. Nothing here deletes or shares (the AI token can't).</summary>
[McpServerToolType]
public static class TeamTools
{
    [McpServerTool(Name = "add_subtask", Title = "Add a subtask", Destructive = false)]
    [Description("Add a subtask under a task. Subtasks are full tasks (own star, dates, reminders, subtasks) and can nest to any depth. Quick-add words work in the text: \"Book train friday !3\".")]
    public static Task<string> AddSubtask(DocketService svc, [Description("Parent task id.")] long parent_id,
        [Description("Subtask text.")] string text, CancellationToken ct = default) => Run(async () =>
        "Added: " + ToDoFormat.Line(await svc.AddSubtaskAsync(parent_id, text, ct), DateTimeOffset.Now) + $" (under #{parent_id})");

    [McpServerTool(Name = "link_subtask", Title = "Put a task under another", Destructive = false, Idempotent = true)]
    [Description("Make an existing task a subtask of another one too. A task may have several parents. Refuses loops.")]
    public static Task<string> LinkSubtask(DocketService svc, [Description("Parent task id.")] long parent_id,
        [Description("The task that goes under it.")] long child_id, CancellationToken ct = default) => Run(async () =>
    {
        await svc.LinkSubtaskAsync(parent_id, child_id, ct);
        return $"#{child_id} is now a subtask of #{parent_id}.";
    });

    [McpServerTool(Name = "task_tree", Title = "Show a list nested", ReadOnly = true, Idempotent = true)]
    [Description("A list's open tasks as a tree, with subtasks indented under their parents (a task with two parents appears under both).")]
    public static Task<string> Tree(DocketService svc, ConnectorOptions settings,
        [Description("List name or id. Default: the default list.")] string? list = null, CancellationToken ct = default) => Run(async () =>
    {
        var p = await svc.ResolveProjectAsync(list ?? settings.DefaultProjectId.ToString(System.Globalization.CultureInfo.InvariantCulture), ct);
        var tree = await svc.ProjectTreeAsync(p.Id, SortMode.Custom, ct);
        var sb = new StringBuilder(p.Title).Append('\n');
        void Walk(TaskNode n)
        {
            sb.Append(new string(' ', n.Depth * 2)).Append(ToDoFormat.Line(n.Info, DateTimeOffset.Now)).Append('\n');
            foreach (var c in n.Children) Walk(c);
        }
        foreach (var n in tree.Open) Walk(n);
        return tree.Open.Count == 0 ? $"{p.Title} has no open tasks." : sb.ToString().TrimEnd();
    });

    [McpServerTool(Name = "assign", Title = "Assign a task", Destructive = false, Idempotent = true)]
    [Description("Assign a task to a person who can already see its list (see the people tool). Use the username.")]
    public static Task<string> Assign(DocketService svc, [Description("Task id.")] long id,
        [Description("Username or display name.")] string person, CancellationToken ct = default) => Run(async () =>
    {
        var t = await svc.GetAsync(id, ct);
        var people = await svc.Client.GetProjectPeopleAsync(t.ProjectId, ct);
        var who = people.FirstOrDefault(u => u.Username.Equals(person, StringComparison.OrdinalIgnoreCase) || DocketService.DisplayName(u).Equals(person, StringComparison.OrdinalIgnoreCase))
            ?? throw new DocketException($"\"{person}\" can't see that list. The user can share it from the Docket app first.");
        return $"Assigned to {DocketService.DisplayName(who)}: " + ToDoFormat.Line(await svc.AssignAsync(id, who, ct), DateTimeOffset.Now);
    });

    [McpServerTool(Name = "people", Title = "People", ReadOnly = true, Idempotent = true)]
    [Description("Everyone the user works with on their lists (usernames and names).")]
    public static Task<string> People(DocketService svc, CancellationToken ct = default) => Run(async () =>
        string.Join("\n", (await svc.PeopleAsync(ct)).Select(u => string.IsNullOrEmpty(u.Name) ? u.Username : $"{u.Username} ({u.Name})")));

    [McpServerTool(Name = "overview", Title = "Overview", ReadOnly = true, Idempotent = true)]
    [Description("Dashboard numbers: open, due today, overdue, next 7 days, done this week, by priority and per list.")]
    public static Task<string> Overview(DocketService svc, CancellationToken ct = default) => Run(async () =>
    {
        var o = await svc.OverviewAsync(ct);
        var sb = new StringBuilder($"Open {o.Open}, due today {o.DueToday}, overdue {o.Overdue}, next 7 days {o.Next7Days}, done today {o.DoneToday}, done this week {o.DoneThisWeek}.\n");
        sb.Append("By priority: ").Append(string.Join(", ", o.ByPriority.Select((n, p) => $"{DocketService.PriorityName(p)} {n}"))).Append('\n');
        foreach (var l in o.Lists) sb.Append($"- {l.Project.Title}: {l.Open} open, {l.Overdue} overdue, {l.DoneThisWeek} done this week\n");
        return sb.ToString().TrimEnd();
    });
}
