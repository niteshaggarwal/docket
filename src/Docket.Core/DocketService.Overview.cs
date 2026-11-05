namespace Docket.Core;

/// <summary>One list's progress on the Overview dashboard.</summary>
public sealed record ListProgress(VProject Project, ListStyle Style, int Open, int Overdue, int DoneThisWeek);

/// <summary>Everything the Overview dashboard shows, from one fetch.</summary>
public sealed record Overview(
    int Open, int DueToday, int Overdue, int Next7Days, int Important, int DoneToday, int DoneThisWeek,
    IReadOnlyList<int> ByPriority, IReadOnlyList<(DateOnly Day, int Done)> DonePerDay,
    IReadOnlyList<TaskInfo> OverdueTasks, IReadOnlyList<TaskInfo> TodayTasks, IReadOnlyList<TaskInfo> UpcomingTasks,
    IReadOnlyList<TaskInfo> RecentlyDone, IReadOnlyList<ListProgress> Lists, IReadOnlyList<TaskInfo> AssignedToMe);

public sealed partial class DocketService
{
    public async Task<Overview> OverviewAsync(CancellationToken ct = default)
    {
        await EnsureMyDayFreshAsync(ct);
        var now = Now;
        var today = DateOnly.FromDateTime(now.Date);
        var weekStart = today.AddDays(-6);
        var open = await client.GetTasksAsync("done = false", ct: ct);
        var done = await client.GetTasksAsync("done = true", "done_at", "desc", ct);
        DateOnly Day(DateTimeOffset d) => DateOnly.FromDateTime(d.ToOffset(now.Offset).Date);
        DateOnly? DoneDay(VTask t) => VDates.OrNull(t.DoneAt) is { } d ? Day(d) : null;

        var overdue = open.Where(t => t.IsOverdue(now)).OrderBy(t => t.Due).ToList();
        var dueToday = open.Where(t => t.Due is { } d && Day(d) == today).ToList();
        var upcoming = open.Where(t => t.Due is { } d && Day(d) > today && Day(d) <= today.AddDays(7)).OrderBy(t => t.Due).ToList();
        var doneWeek = done.Where(t => DoneDay(t) is { } d && d >= weekStart).ToList();
        var perDay = Enumerable.Range(0, 14).Select(i => today.AddDays(i - 13))
            .Select(d => (d, done.Count(t => DoneDay(t) == d))).ToList();
        var byPriority = Enumerable.Range(0, 6).Select(p => open.Count(t => Math.Clamp(t.Priority, 0, 5) == p)).ToList();

        var me = await MeAsync(ct);
        var mine = open.Where(t => t.Assignees?.Any(a => a.Id == me.Id) == true).ToList();

        var lists = new List<ListProgress>();
        foreach (var p in await ProjectsAsync(false, ct))
        {
            var o = open.Where(t => t.ProjectId == p.Id).ToList();
            lists.Add(new ListProgress(p, ListStyle.Parse(p.Description), o.Count, o.Count(t => t.IsOverdue(now)), doneWeek.Count(t => t.ProjectId == p.Id)));
        }

        return new Overview(open.Count, dueToday.Count, overdue.Count, upcoming.Count, open.Count(t => t.IsFavorite),
            done.Count(t => DoneDay(t) == today), doneWeek.Count, byPriority, perDay,
            await InfoAsync(overdue.Take(20), ct), await InfoAsync(dueToday.OrderBy(t => t.Due), ct), await InfoAsync(upcoming.Take(30), ct),
            await InfoAsync(done.Take(10), ct), lists.Where(l => l.Open > 0 || l.DoneThisWeek > 0).OrderByDescending(l => l.Open).ToList(),
            await InfoAsync(mine, ct));
    }

    /// <summary>Open tasks due in the next <paramref name="days"/> days (overdue first), as Vikunja's Upcoming shows.</summary>
    public async Task<List<TaskInfo>> UpcomingAsync(int days = 7, bool includeOverdue = true, CancellationToken ct = default)
    {
        var now = Now;
        var end = now.Date.AddDays(days + 1);
        var open = await client.GetTasksAsync("done = false", ct: ct);
        var pick = open.Where(t => t.Due is { } d && d < end && (includeOverdue || d >= now.Date)).OrderBy(t => t.Due);
        return await InfoAsync(pick, ct);
    }

    /// <summary>Sets start and end dates (what the Gantt chart draws). Null leaves a date unchanged; VDates.Empty clears it.</summary>
    public async Task<TaskInfo> SetSpanAsync(long id, DateTimeOffset? start, DateTimeOffset? end, CancellationToken ct = default)
    {
        await GetAsync(id, ct);
        var t = await client.ModifyTaskAsync(id, x =>
        {
            if (start is { } s) x.StartDate = s;
            if (end is { } e) x.EndDate = e;
        }, ct) ?? throw new DocketException($"Task #{id} not found.");
        return new TaskInfo(t, await ProjectNameAsync(t.ProjectId, ct));
    }

    public async Task<TaskInfo> SetPriorityAsync(long id, int priority, CancellationToken ct = default)
    {
        await GetAsync(id, ct);
        var t = await client.ModifyTaskAsync(id, x => x.Priority = ValidPriority(priority), ct) ?? throw new DocketException($"Task #{id} not found.");
        return new TaskInfo(t, await ProjectNameAsync(t.ProjectId, ct));
    }

    public static string PriorityName(int p) => p switch { >= 5 => "Do now", 4 => "Urgent", 3 => "High", 2 => "Medium", 1 => "Low", _ => "None" };

    /// <summary>Who created a task and when it was created, last changed and finished: the task's history line.</summary>
    public static IReadOnlyList<(string What, DateTimeOffset When)> History(VTask t)
    {
        var list = new List<(string, DateTimeOffset)>();
        if (VDates.OrNull(t.Created) is { } c) list.Add((t.CreatedBy is { } u ? $"Created by {DisplayName(u)}" : "Created", c));
        if (VDates.OrNull(t.Updated) is { } up && (VDates.OrNull(t.Created) is not { } c2 || (up - c2).TotalSeconds > 5)) list.Add(("Last changed", up));
        if (t.Done && VDates.OrNull(t.DoneAt) is { } d) list.Add(("Completed", d));
        return list;
    }
}
