using System.Globalization;

namespace Docket.Core;

/// <summary>A user-facing error (bad project name, unknown column...). Its message is safe to show as-is.</summary>
public sealed class DocketException(string message) : Exception(message);

/// <summary>The task lists Docket knows by name.</summary>
public enum TaskView { Today, Upcoming, Overdue, Open, Done }

/// <summary>A task plus the names that make it readable.</summary>
public sealed record TaskInfo(VTask Task, string Project, string? Column = null)
{
    public long Id => Task.Id;
}

public sealed record TaskDetail(TaskInfo Info, IReadOnlyList<VComment> Comments, IReadOnlyList<string> Columns);

public sealed record BoardColumnInfo(string Title, long Id, bool IsDone, IReadOnlyList<TaskInfo> Tasks);

public sealed record ProjectInfo(VProject Project, string Path, int OpenTasks, IReadOnlyList<string> Columns);

/// <summary>
/// Task operations shared by the docket CLI and the Claude connector: names instead of ids,
/// natural due dates, and moving cards by column name. Nothing here deletes.
/// </summary>
public sealed partial class DocketService(VikunjaClient client, TimeProvider? time = null)
{
    private readonly TimeProvider _time = time ?? TimeProvider.System;
    private List<VProject>? _projects;

    public VikunjaClient Client => client;
    private DateTimeOffset Now => _time.GetLocalNow();

    // ---------- projects ----------
    public async Task<List<VProject>> ProjectsAsync(bool refresh = false, CancellationToken ct = default)
    {
        if (_projects is null || refresh)
            _projects = (await client.GetProjectsAsync(ct)).Where(p => !p.IsSavedFilter && !p.IsArchived).OrderBy(p => p.Position).ToList();
        return _projects;
    }

    public async Task<string> ProjectNameAsync(long id, CancellationToken ct = default) =>
        (await ProjectsAsync(false, ct)).FirstOrDefault(p => p.Id == id)?.Title ?? $"project {id}";

    /// <summary>Finds a project by id or name: exact match first, then a unique partial match.</summary>
    public async Task<VProject> ResolveProjectAsync(string nameOrId, CancellationToken ct = default)
    {
        var all = await ProjectsAsync(false, ct);
        var key = nameOrId.Trim().TrimStart('#');
        if (long.TryParse(key, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id) && all.FirstOrDefault(p => p.Id == id) is { } byId) return byId;
        var exact = all.Where(p => p.Title.Equals(key, StringComparison.OrdinalIgnoreCase)).ToList();
        if (exact.Count == 1) return exact[0];
        var partial = all.Where(p => p.Title.Contains(key, StringComparison.OrdinalIgnoreCase)).ToList();
        if (partial.Count == 1) return partial[0];
        var names = string.Join(", ", (partial.Count > 1 ? partial : all).Select(p => p.Title));
        throw new DocketException(partial.Count > 1
            ? $"\"{nameOrId}\" matches several projects: {names}. Be more specific."
            : $"No project called \"{nameOrId}\". Projects: {names}.");
    }

    public async Task<VProject> DefaultProjectAsync(long preferredId, CancellationToken ct = default)
    {
        var all = await ProjectsAsync(false, ct);
        return all.FirstOrDefault(p => p.Id == preferredId)
            ?? all.FirstOrDefault(p => p.Title.Equals("Inbox", StringComparison.OrdinalIgnoreCase))
            ?? all.FirstOrDefault()
            ?? throw new DocketException("There are no projects yet. Create one first.");
    }

    public async Task<List<ProjectInfo>> ProjectOverviewAsync(CancellationToken ct = default)
    {
        var all = await ProjectsAsync(true, ct);
        var open = await client.GetTasksAsync("done = false", ct: ct);
        var counts = open.GroupBy(t => t.ProjectId).ToDictionary(g => g.Key, g => g.Count());
        var result = new List<ProjectInfo>();
        foreach (var p in all)
        {
            var columns = p.KanbanView is { } v ? (await client.GetBoardAsync(p.Id, v.Id, ct)).OrderBy(b => b.Position).Select(b => b.Title).ToList() : [];
            result.Add(new ProjectInfo(p, PathOf(p, all), counts.GetValueOrDefault(p.Id), columns));
        }
        return result;
    }

    public async Task<VProject> CreateProjectAsync(string title, string? parent, CancellationToken ct = default)
    {
        var parentId = parent is null ? 0 : (await ResolveProjectAsync(parent, ct)).Id;
        var created = await client.CreateProjectAsync(title.Trim(), parentId, ct) ?? throw new DocketException("Vikunja did not return the new project.");
        await ProjectsAsync(true, ct);
        return created;
    }

    private static string PathOf(VProject p, List<VProject> all)
    {
        var parent = all.FirstOrDefault(x => x.Id == p.ParentProjectId);
        return parent is null ? p.Title : $"{PathOf(parent, all)} / {p.Title}";
    }

    // ---------- lists ----------
    public static string FilterFor(TaskView view) => view switch
    {
        TaskView.Today => "done = false && due_date < now/d+1d",
        TaskView.Upcoming => "done = false && due_date >= now/d+1d && due_date < now/d+8d",
        TaskView.Overdue => "done = false && due_date < now",
        TaskView.Open => "done = false",
        TaskView.Done => "done = true",
        _ => "done = false",
    };

    public static TaskView ParseView(string? view) => (view ?? "today").Trim().ToLowerInvariant() switch
    {
        "today" or "" => TaskView.Today,
        "upcoming" or "week" or "next" => TaskView.Upcoming,
        "overdue" or "late" => TaskView.Overdue,
        "open" or "all" or "todo" => TaskView.Open,
        "done" or "completed" or "finished" => TaskView.Done,
        var v => throw new DocketException($"Unknown list \"{v}\". Use today, upcoming, overdue, open or done."),
    };

    public async Task<List<TaskInfo>> ListAsync(TaskView view, string? project = null, string? search = null, int limit = 100, CancellationToken ct = default)
    {
        List<VTask> tasks;
        if (!string.IsNullOrWhiteSpace(search)) tasks = await client.SearchTasksAsync(search, view == TaskView.Done, ct);
        else if (view == TaskView.Done) tasks = await client.GetTasksAsync(FilterFor(view), "done_at", "desc", ct);
        else tasks = await client.GetTasksAsync(FilterFor(view), ct: ct);

        if (project is not null)
        {
            var p = await ResolveProjectAsync(project, ct);
            tasks = tasks.Where(t => t.ProjectId == p.Id).ToList();
        }
        var ordered = view == TaskView.Done ? tasks.AsEnumerable()
            : tasks.OrderBy(t => t.Due ?? DateTimeOffset.MaxValue).ThenByDescending(t => t.Priority).ThenBy(t => t.Id);
        var result = new List<TaskInfo>();
        foreach (var t in ordered.Take(Math.Clamp(limit, 1, 500)))
            result.Add(new TaskInfo(t, await ProjectNameAsync(t.ProjectId, ct)));
        return result;
    }

    // ---------- single task ----------
    public async Task<TaskDetail> DetailAsync(long id, CancellationToken ct = default)
    {
        var t = await GetAsync(id, ct);
        var p = (await ProjectsAsync(false, ct)).FirstOrDefault(x => x.Id == t.ProjectId);
        string? column = null;
        var columns = new List<string>();
        if (p?.KanbanView is { } v)
        {
            var board = await client.GetBoardAsync(p.Id, v.Id, ct);
            columns = board.OrderBy(b => b.Position).Select(b => b.Title).ToList();
            column = board.FirstOrDefault(b => b.Tasks?.Any(x => x.Id == id) == true)?.Title;
        }
        var comments = await client.GetCommentsAsync(id, ct);
        return new TaskDetail(new TaskInfo(t, p?.Title ?? $"project {t.ProjectId}", column), comments, columns);
    }

    public async Task<VTask> GetAsync(long id, CancellationToken ct = default)
    {
        try { return await client.GetTaskAsync(id, ct) ?? throw new DocketException($"Task #{id} not found."); }
        catch (VikunjaException ex) when (ex.Status is System.Net.HttpStatusCode.NotFound or System.Net.HttpStatusCode.Forbidden)
        { throw new DocketException($"Task #{id} not found."); }
    }

    /// <summary>
    /// Adds a task. The title may carry quick-add words ("tomorrow", "fri", "!3"); explicit
    /// due/priority arguments win over them.
    /// </summary>
    public async Task<TaskInfo> AddAsync(string text, string? project = null, string? due = null, int? priority = null,
        string? description = null, IEnumerable<string>? labels = null, long defaultProjectId = 0, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(text)) throw new DocketException("A task needs a title.");
        var parsed = QuickAdd.Parse(text, Now);
        var p = project is null ? await DefaultProjectAsync(defaultProjectId, ct) : await ResolveProjectAsync(project, ct);
        var dueAt = due is null ? parsed.Due : ParseDue(due, Now);
        var prio = ValidPriority(priority ?? parsed.Priority);
        var created = await client.CreateTaskAsync(p.Id, parsed.Title, dueAt, prio, ct) ?? throw new DocketException("Vikunja did not return the new task.");
        if (!string.IsNullOrWhiteSpace(description))
            created = await client.ModifyTaskAsync(created.Id, t => t.Description = description, ct) ?? created;
        if (labels is not null) foreach (var l in labels.Where(l => !string.IsNullOrWhiteSpace(l))) await AddLabelAsync(created.Id, l, ct);
        return new TaskInfo(created, p.Title);
    }

    /// <summary>Changes only the fields given. due: a date, a word like "friday", or "none" to clear.</summary>
    public async Task<TaskInfo> UpdateAsync(long id, string? title = null, string? description = null, string? due = null,
        int? priority = null, string? project = null, CancellationToken ct = default)
    {
        await GetAsync(id, ct);
        VProject? target = project is null ? null : await ResolveProjectAsync(project, ct);
        DateTimeOffset? dueAt = due is null ? null : ParseDue(due, Now);
        var clearDue = due is not null && dueAt is null;
        var updated = await client.ModifyTaskAsync(id, t =>
        {
            if (!string.IsNullOrWhiteSpace(title)) t.Title = title.Trim();
            if (description is not null) t.Description = description;
            if (dueAt is { } d) t.DueDate = d;
            if (clearDue) t.DueDate = VDates.Empty;
            if (priority is { } pr) t.Priority = ValidPriority(pr);
            if (target is not null) t.ProjectId = target.Id;
        }, ct) ?? throw new DocketException($"Task #{id} not found.");
        return new TaskInfo(updated, await ProjectNameAsync(updated.ProjectId, ct));
    }

    public async Task<TaskInfo> SetDoneAsync(long id, bool done, CancellationToken ct = default)
    {
        var current = await GetAsync(id, ct);
        var t = await CompleteAsync(current, done, ct);
        return new TaskInfo(t, await ProjectNameAsync(t.ProjectId, ct));
    }

    /// <summary>Moves a task to a Kanban column of its own project, matched by name.</summary>
    public async Task<TaskInfo> MoveAsync(long id, string column, CancellationToken ct = default)
    {
        var t = await GetAsync(id, ct);
        var p = (await ProjectsAsync(false, ct)).FirstOrDefault(x => x.Id == t.ProjectId) ?? throw new DocketException($"Can't find the project of task #{id}.");
        var view = p.KanbanView ?? throw new DocketException($"\"{p.Title}\" has no Kanban board.");
        var board = await client.GetBoardAsync(p.Id, view.Id, ct);
        var bucket = board.FirstOrDefault(b => b.Title.Equals(column.Trim(), StringComparison.OrdinalIgnoreCase))
            ?? board.SingleOrDefault(b => b.Title.Contains(column.Trim(), StringComparison.OrdinalIgnoreCase))
            ?? throw new DocketException($"No column \"{column}\" in {p.Title}. Columns: {string.Join(", ", board.Select(b => b.Title))}.");
        await client.MoveToBucketAsync(p.Id, view.Id, bucket.Id, id, ct);
        var fresh = await GetAsync(id, ct);
        return new TaskInfo(fresh, p.Title, bucket.Title);
    }

    public async Task<VComment> CommentAsync(long id, string text, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(text)) throw new DocketException("The comment is empty.");
        await GetAsync(id, ct);
        return await client.AddCommentAsync(id, text, ct) ?? throw new DocketException("Vikunja did not return the comment.");
    }

    /// <summary>Adds a label to a task, creating the label if it doesn't exist yet.</summary>
    public async Task<VLabel> AddLabelAsync(long id, string label, CancellationToken ct = default)
    {
        var name = label.Trim();
        var existing = (await client.GetLabelsAsync(ct)).FirstOrDefault(l => l.Title.Equals(name, StringComparison.OrdinalIgnoreCase))
            ?? await client.CreateLabelAsync(name, ct) ?? throw new DocketException($"Couldn't create the label \"{name}\".");
        try { await client.AddLabelToTaskAsync(id, existing.Id, ct); }
        catch (VikunjaException ex) when (ex.Status == System.Net.HttpStatusCode.BadRequest || ex.Status == System.Net.HttpStatusCode.Conflict) { /* already has it */ }
        return existing;
    }

    public async Task<List<BoardColumnInfo>> BoardAsync(string project, CancellationToken ct = default)
    {
        var p = await ResolveProjectAsync(project, ct);
        var view = p.KanbanView ?? throw new DocketException($"\"{p.Title}\" has no Kanban board.");
        var board = await client.GetBoardAsync(p.Id, view.Id, ct);
        return board.OrderBy(b => b.Position).Select(b => new BoardColumnInfo(b.Title, b.Id, b.Id == view.DoneBucketId,
            (b.Tasks ?? []).OrderBy(t => t.Position).Select(t => new TaskInfo(t, p.Title, b.Title)).ToList())).ToList();
    }

    // ---------- parsing ----------
    private static int ValidPriority(int p) => p is >= 0 and <= 5 ? p : throw new DocketException("Priority goes from 0 (none) to 5 (do now).");

    /// <summary>
    /// ISO date-time. With an offset or Z it is taken as written; without one it means that clock time in the
    /// user's zone (now.Offset), not the machine's, so a server in UTC reads "2026-11-20T09:15" the same as the PC.
    /// </summary>
    public static bool TryParseIso(string text, DateTimeOffset now, out DateTimeOffset value)
    {
        var s = text.Trim().Replace(' ', 'T');
        if (HasOffset(s))
            return DateTimeOffset.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.None, out value);
        if (DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.None, out var local))
        {
            value = new DateTimeOffset(DateTime.SpecifyKind(local, DateTimeKind.Unspecified), now.Offset);
            return true;
        }
        value = default;
        return false;

        static bool HasOffset(string v) =>
            v.EndsWith('Z') || (v.Length > 6 && (v[^6] is '+' or '-') && v[^3] == ':');
    }

    /// <summary>
    /// Due date from text: "none"/"clear" (null), ISO date or date-time, or the quick-add words
    /// (today, tomorrow, weekday, "in 3 days", 12/11). A bare date means 6 pm that day.
    /// </summary>
    public static DateTimeOffset? ParseDue(string text, DateTimeOffset now)
    {
        var s = text.Trim();
        if (s.Length == 0 || s.Equals("none", StringComparison.OrdinalIgnoreCase) || s.Equals("clear", StringComparison.OrdinalIgnoreCase)) return null;
        if (DateOnly.TryParseExact(s, ["yyyy-MM-dd", "yyyy/MM/dd"], CultureInfo.InvariantCulture, DateTimeStyles.None, out var d))
            return new DateTimeOffset(d.Year, d.Month, d.Day, 18, 0, 0, now.Offset);
        if (s.Contains('T', StringComparison.Ordinal) && TryParseIso(s, now, out var dt)) return dt;
        const string marker = "⁣";
        var q = QuickAdd.Parse(marker + " " + s, now);
        if (q.Due is { } due && q.Title == marker) return due;
        throw new DocketException($"Couldn't read the date \"{text}\". Use 2026-11-20, today, tomorrow, a weekday, \"in 3 days\", 20/11 or none.");
    }
}
