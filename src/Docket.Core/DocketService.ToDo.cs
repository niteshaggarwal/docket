namespace Docket.Core;

/// <summary>Microsoft To Do style smart lists.</summary>
public enum SmartList { MyDay, Important, Planned, All, Completed, Tasks }

public enum SortMode { Custom, Importance, DueDate, Alphabetical, CreationDate }

/// <summary>Where My Day's date lives (the shared settings file), so My Day resets once a day for every Docket tool.</summary>
public interface IDocketState
{
    DateOnly? MyDayDate { get; set; }

    /// <summary>Re-reads the stored date, so a long-running tool sees a reset another tool already did.</summary>
    void RefreshMyDayDate();

    /// <summary>Stores only the My Day date, without overwriting other settings saved meanwhile.</summary>
    void SaveMyDayDate(DateOnly day);
}

/// <summary>Open and completed tasks of one list, already sorted.</summary>
public sealed record ListContent(IReadOnlyList<TaskInfo> Open, IReadOnlyList<TaskInfo> Completed);

public sealed record Suggestion(string Group, TaskInfo Task);

public sealed partial class DocketService
{
    public const string MyDayLabel = "My Day";
    private const string MyDayColor = "f2a93b";

    public IDocketState? State { get; init; }

    /// <summary>Labels the user picks as categories (not My Day, not repeat markers).</summary>
    public static bool IsCategory(VLabel l) =>
        !l.Title.Equals(MyDayLabel, StringComparison.OrdinalIgnoreCase) && !l.Title.StartsWith(RepeatRule.LabelPrefix, StringComparison.Ordinal);

    public static bool InMyDay(VTask t) => t.HasLabel(MyDayLabel);

    // ---------- labels ----------
    private List<VLabel>? _labels;

    public async Task<List<VLabel>> LabelsAsync(bool refresh = false, CancellationToken ct = default)
    {
        if (_labels is null || refresh) _labels = await client.GetLabelsAsync(ct);
        return _labels;
    }

    public async Task<List<VLabel>> CategoriesAsync(CancellationToken ct = default) =>
        (await LabelsAsync(true, ct)).Where(IsCategory).OrderBy(l => l.Title, StringComparer.CurrentCultureIgnoreCase).ToList();

    public async Task<VLabel> EnsureLabelAsync(string title, string color = "", CancellationToken ct = default)
    {
        var all = await LabelsAsync(false, ct);
        var found = all.FirstOrDefault(l => l.Title.Equals(title, StringComparison.OrdinalIgnoreCase));
        if (found is not null) return found;
        var created = await client.CreateLabelAsync(title, color, ct) ?? throw new DocketException($"Couldn't create the label \"{title}\".");
        all.Add(created);
        return created;
    }

    private async Task<VTask> SetLabelsAsync(VTask t, IEnumerable<VLabel> labels, CancellationToken ct)
    {
        var list = labels.GroupBy(l => l.Id).Select(g => g.First()).ToList();
        await client.SetTaskLabelsAsync(t.Id, list.Select(l => l.Id), ct);
        t.Labels = list;
        return t;
    }

    // ---------- My Day ----------
    private const string MyDayStamp = "Docket My Day ";
    private readonly SemaphoreSlim _myDayLock = new(1, 1);
    private DateOnly? _myDayChecked;

    /// <summary>The day My Day was last reset, stored on the server in the My Day label's description.</summary>
    public static DateOnly? MyDayDateOf(VLabel label) =>
        label.Description.StartsWith(MyDayStamp, StringComparison.Ordinal)
        && DateOnly.TryParseExact(label.Description[MyDayStamp.Length..].Trim(), "yyyy-MM-dd", out var d) ? d : null;

    private async Task StampMyDayAsync(VLabel label, DateOnly day, CancellationToken ct)
    {
        label.Description = MyDayStamp + day.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
        await client.UpdateLabelAsync(label, ct);
    }

    /// <summary>
    /// Clears yesterday's My Day the first time any Docket tool or device looks at it on a new day.
    /// The reset date lives on the server (the My Day label), so the PC, phone, CLI and Claude agree
    /// and a newly installed device never wipes a My Day that was set up earlier today.
    /// </summary>
    public async Task EnsureMyDayFreshAsync(CancellationToken ct = default)
    {
        var today = DateOnly.FromDateTime(Now.Date);
        if (_myDayChecked == today) return;
        await _myDayLock.WaitAsync(ct);
        try
        {
            if (_myDayChecked == today) return;
            var label = (await LabelsAsync(true, ct)).FirstOrDefault(l => l.Title.Equals(MyDayLabel, StringComparison.OrdinalIgnoreCase));
            if (label is not null)
            {
                var stored = MyDayDateOf(label);
                if (stored is { } s && s < today)
                    foreach (var t in await client.GetTasksAsync($"labels in {label.Id}", ct: ct))
                        await SetLabelsAsync(t, (t.Labels ?? []).Where(l => l.Id != label.Id), ct);
                // No stamp yet (first run after upgrading): adopt today without clearing anything.
                if (stored != today) await StampMyDayAsync(label, today, ct);
            }
            _myDayChecked = today;
        }
        finally { _myDayLock.Release(); }
    }

    public async Task<TaskInfo> SetMyDayAsync(long id, bool inMyDay, CancellationToken ct = default)
    {
        await EnsureMyDayFreshAsync(ct);
        var t = await GetAsync(id, ct);
        var label = await EnsureLabelAsync(MyDayLabel, MyDayColor, ct);
        if (MyDayDateOf(label) is null) await StampMyDayAsync(label, DateOnly.FromDateTime(Now.Date), ct);
        var labels = (t.Labels ?? []).Where(l => l.Id != label.Id).ToList();
        if (inMyDay) labels.Add(label);
        await SetLabelsAsync(t, labels, ct);
        return new TaskInfo(t, await ProjectNameAsync(t.ProjectId, ct));
    }

    // ---------- smart lists and lists ----------
    public async Task<ListContent> SmartAsync(SmartList list, SortMode sort, long defaultProjectId = 0, CancellationToken ct = default)
    {
        if (list == SmartList.MyDay) await EnsureMyDayFreshAsync(ct);
        if (list == SmartList.Tasks) return await ProjectListAsync((await DefaultProjectAsync(defaultProjectId, ct)).Id, sort, ct);

        var open = await client.GetTasksAsync("done = false", ct: ct);
        List<VTask> done = [];
        IEnumerable<VTask> pick = list switch
        {
            SmartList.MyDay => open.Where(InMyDay),
            SmartList.Important => open.Where(t => t.IsFavorite),
            SmartList.Planned => open.Where(t => t.Due is not null),
            SmartList.All => open,
            _ => [],
        };
        if (list is SmartList.MyDay or SmartList.Important or SmartList.Completed)
        {
            var recent = await client.GetTasksAsync("done = true", "done_at", "desc", ct);
            done = list switch
            {
                SmartList.MyDay => recent.Where(InMyDay).ToList(),
                SmartList.Important => recent.Where(t => t.IsFavorite).Take(100).ToList(),
                _ => recent.Take(300).ToList(),
            };
        }
        var openInfo = await InfoAsync(Sort(pick, list == SmartList.Planned && sort == SortMode.Custom ? SortMode.DueDate : sort), ct);
        var doneInfo = await InfoAsync(done.OrderByDescending(t => VDates.OrNull(t.DoneAt) ?? t.Updated), ct);
        return list == SmartList.Completed ? new ListContent(doneInfo, []) : new ListContent(openInfo, doneInfo);
    }

    public async Task<ListContent> ProjectListAsync(long projectId, SortMode sort, CancellationToken ct = default)
    {
        var open = (await client.GetTasksAsync("done = false", ct: ct)).Where(t => t.ProjectId == projectId);
        var done = (await client.GetTasksAsync("done = true", "done_at", "desc", ct)).Where(t => t.ProjectId == projectId).Take(200);
        return new ListContent(await InfoAsync(Sort(open, sort), ct), await InfoAsync(done, ct));
    }

    public async Task<int> CountAsync(SmartList list, CancellationToken ct = default) => (await SmartAsync(list, SortMode.Custom, 0, ct)).Open.Count;

    private async Task<List<TaskInfo>> InfoAsync(IEnumerable<VTask> tasks, CancellationToken ct)
    {
        var result = new List<TaskInfo>();
        foreach (var t in tasks) result.Add(new TaskInfo(t, await ProjectNameAsync(t.ProjectId, ct)));
        return result;
    }

    public static IEnumerable<VTask> Sort(IEnumerable<VTask> tasks, SortMode sort) => sort switch
    {
        SortMode.Importance => tasks.OrderByDescending(t => t.IsFavorite).ThenByDescending(t => t.Priority).ThenBy(t => t.Due ?? DateTimeOffset.MaxValue),
        SortMode.DueDate => tasks.OrderBy(t => t.Due ?? DateTimeOffset.MaxValue).ThenByDescending(t => t.Priority),
        SortMode.Alphabetical => tasks.OrderBy(t => t.Title, StringComparer.CurrentCultureIgnoreCase),
        SortMode.CreationDate => tasks.OrderByDescending(t => t.Created),
        _ => tasks.OrderBy(t => t.Position).ThenByDescending(t => t.Created),
    };

    /// <summary>Planned view groups, as in Microsoft To Do.</summary>
    public static string PlannedGroup(VTask t, DateTimeOffset now)
    {
        if (t.Due is not { } due) return "No date";
        var d = due.ToOffset(now.Offset).Date;
        var today = now.Date;
        if (d < today) return "Earlier";
        if (d == today) return "Today";
        if (d == today.AddDays(1)) return "Tomorrow";
        var endOfWeek = today.AddDays(7 - ((int)today.DayOfWeek + 6) % 7); // through Sunday
        return d < endOfWeek ? "This week" : "Later";
    }

    /// <summary>Suggestions for My Day: overdue, due today, upcoming, recently added, older.</summary>
    public async Task<List<Suggestion>> MyDaySuggestionsAsync(CancellationToken ct = default)
    {
        await EnsureMyDayFreshAsync(ct);
        var open = (await client.GetTasksAsync("done = false", ct: ct)).Where(t => !InMyDay(t)).ToList();
        var now = Now;
        var used = new HashSet<long>();
        var result = new List<Suggestion>();
        async Task AddGroup(string name, IEnumerable<VTask> ts, int max)
        {
            foreach (var t in ts.Where(t => used.Add(t.Id)).Take(max)) result.Add(new Suggestion(name, new TaskInfo(t, await ProjectNameAsync(t.ProjectId, ct))));
        }
        await AddGroup("Overdue", open.Where(t => t.Due < now.Date).OrderBy(t => t.Due), 20);
        await AddGroup("Due today", open.Where(t => t.Due is { } d && d.ToOffset(now.Offset).Date == now.Date), 20);
        await AddGroup("Upcoming", open.Where(t => t.Due is { } d && d.ToOffset(now.Offset).Date > now.Date && d < now.AddDays(7)).OrderBy(t => t.Due), 10);
        await AddGroup("Recently added", open.Where(t => t.Created > now.AddDays(-3)).OrderByDescending(t => t.Created), 10);
        await AddGroup("Earlier", open.Where(t => t.IsFavorite).Concat(open.OrderByDescending(t => t.Created)), 10);
        return result;
    }

    // ---------- task fields ----------
    public async Task<TaskInfo> SetImportantAsync(long id, bool important, CancellationToken ct = default)
    {
        await GetAsync(id, ct);
        var t = await client.ModifyTaskAsync(id, x => x.IsFavorite = important, ct) ?? throw new DocketException($"Task #{id} not found.");
        return new TaskInfo(t, await ProjectNameAsync(t.ProjectId, ct));
    }

    public async Task<TaskInfo> SetNotesAndStepsAsync(long id, Func<TaskNotes, TaskNotes> change, CancellationToken ct = default)
    {
        await GetAsync(id, ct);
        var t = await client.ModifyTaskAsync(id, x => x.Description = change(TaskNotes.Parse(x.Description)).ToHtml(), ct)
            ?? throw new DocketException($"Task #{id} not found.");
        return new TaskInfo(t, await ProjectNameAsync(t.ProjectId, ct));
    }

    public Task<TaskInfo> AddStepAsync(long id, string title, CancellationToken ct = default) =>
        string.IsNullOrWhiteSpace(title) ? throw new DocketException("The step is empty.")
        : SetNotesAndStepsAsync(id, n => n with { Steps = [.. n.Steps, new Step(title.Trim(), false)] }, ct);

    public Task<TaskInfo> SetStepDoneAsync(long id, int index, bool done, CancellationToken ct = default) =>
        SetNotesAndStepsAsync(id, n => n with { Steps = n.Steps.Select((s, i) => i == StepIndex(n, index) ? s with { Done = done } : s).ToList() }, ct);

    public Task<TaskInfo> RemoveStepAsync(long id, int index, CancellationToken ct = default) =>
        SetNotesAndStepsAsync(id, n => n with { Steps = n.Steps.Where((_, i) => i != StepIndex(n, index)).ToList() }, ct);

    private static int StepIndex(TaskNotes n, int index) =>
        index >= 0 && index < n.Steps.Count ? index : throw new DocketException($"There is no step {index + 1}; the task has {n.Steps.Count}.");

    /// <summary>Sets or clears (null) the repeat. A repeat needs a due date, so today 6 pm is used when there is none.</summary>
    public async Task<TaskInfo> SetRepeatAsync(long id, RepeatRule? rule, CancellationToken ct = default)
    {
        var t = await GetAsync(id, ct);
        var keep = (t.Labels ?? []).Where(l => !l.Title.StartsWith(RepeatRule.LabelPrefix, StringComparison.Ordinal)).ToList();
        if (rule is not null && !rule.IsNative) keep.Add(await EnsureLabelAsync(rule.Label, "8a8886", ct));
        await SetLabelsAsync(t, keep, ct);
        var (after, mode) = rule?.NativeFields ?? (0, 0);
        var updated = await client.ModifyTaskAsync(id, x =>
        {
            x.RepeatAfter = after;
            x.RepeatMode = mode;
            if (rule is not null && x.Due is null) x.DueDate = new DateTimeOffset(Now.Year, Now.Month, Now.Day, 18, 0, 0, Now.Offset);
        }, ct) ?? throw new DocketException($"Task #{id} not found.");
        return new TaskInfo(updated, await ProjectNameAsync(updated.ProjectId, ct));
    }

    /// <summary>Replaces the task's reminders with these times (empty clears them).</summary>
    public async Task<TaskInfo> SetRemindersAsync(long id, IEnumerable<DateTimeOffset> times, CancellationToken ct = default)
    {
        await GetAsync(id, ct);
        var list = times.Distinct().OrderBy(x => x).Select(x => new VReminder { Reminder = x }).ToList();
        var t = await client.ModifyTaskAsync(id, x => x.Reminders = list, ct) ?? throw new DocketException($"Task #{id} not found.");
        return new TaskInfo(t, await ProjectNameAsync(t.ProjectId, ct));
    }

    /// <summary>Replaces the task's categories, keeping My Day and repeat markers.</summary>
    public async Task<TaskInfo> SetCategoriesAsync(long id, IEnumerable<string> categories, CancellationToken ct = default)
    {
        var t = await GetAsync(id, ct);
        var keep = (t.Labels ?? []).Where(l => !IsCategory(l)).ToList();
        foreach (var c in categories.Where(c => !string.IsNullOrWhiteSpace(c))) keep.Add(await EnsureLabelAsync(c.Trim(), "", ct));
        await SetLabelsAsync(t, keep, ct);
        return new TaskInfo(t, await ProjectNameAsync(t.ProjectId, ct));
    }

    /// <summary>
    /// Completes a task. A Docket-managed repeat (weekdays, yearly...) moves to its next date and
    /// stays open with its steps unticked; Vikunja handles its own repeats the same way.
    /// </summary>
    private async Task<VTask> CompleteAsync(VTask t, bool done, CancellationToken ct)
    {
        var rule = RepeatRule.From(t);
        if (!done || rule is null || rule.IsNative)
        {
            var r = await client.ModifyTaskAsync(t.Id, x => x.Done = done, ct) ?? t;
            if (done && rule is not null && !r.Done) r = await UntickStepsAsync(r.Labels is null ? WithLabels(r, t) : r, ct);
            return r;
        }
        var from = t.Due ?? Now;
        var next = rule.Next(from);
        while (next < Now.Date) next = rule.Next(next); // skip missed occurrences
        var shift = next - from;
        var updated = await client.ModifyTaskAsync(t.Id, x =>
        {
            x.DueDate = next;
            if (x.Reminders is { } rs) foreach (var rem in rs) if (VDates.OrNull(rem.Reminder) is { } at && rem.RelativeTo.Length == 0) rem.Reminder = at + shift;
            x.Done = false;
        }, ct) ?? t;
        return await UntickStepsAsync(updated.Labels is null ? WithLabels(updated, t) : updated, ct);
    }

    /// <summary>Update responses can omit labels; carry them over from the copy we read.</summary>
    private static VTask WithLabels(VTask target, VTask source) { target.Labels = source.Labels; return target; }

    /// <summary>The next occurrence of a repeating task starts fresh: steps unticked, out of My Day.</summary>
    private async Task<VTask> UntickStepsAsync(VTask t, CancellationToken ct)
    {
        var notes = TaskNotes.Parse(t.Description);
        if (notes.DoneCount > 0)
            t = await client.ModifyTaskAsync(t.Id, x => x.Description = (notes with { Steps = notes.Steps.Select(s => s with { Done = false }).ToList() }).ToHtml(), ct) ?? t;
        if (InMyDay(t)) t = await SetLabelsAsync(t, (t.Labels ?? []).Where(l => !l.Title.Equals(MyDayLabel, StringComparison.OrdinalIgnoreCase)), ct);
        return t;
    }

    // ---------- delete (Docket app only; Claude's token can't) ----------
    public async Task DeleteTaskAsync(long id, CancellationToken ct = default)
    {
        await GetAsync(id, ct);
        await client.DeleteTaskAsync(id, ct);
    }

    // ---------- lists ----------
    public async Task<VProject> RenameListAsync(long projectId, string? title = null, string? hexColor = null, long? parentId = null, CancellationToken ct = default)
    {
        var p = (await ProjectsAsync(true, ct)).FirstOrDefault(x => x.Id == projectId) ?? throw new DocketException($"List {projectId} not found.");
        if (!string.IsNullOrWhiteSpace(title)) p.Title = title.Trim();
        if (hexColor is not null) p.HexColor = hexColor.TrimStart('#');
        if (parentId is { } pid) p.ParentProjectId = pid;
        var updated = await client.UpdateProjectAsync(p, ct) ?? p;
        await ProjectsAsync(true, ct);
        return updated;
    }

    public async Task DeleteListAsync(long projectId, CancellationToken ct = default)
    {
        await client.DeleteProjectAsync(projectId, ct);
        await ProjectsAsync(true, ct);
    }

    /// <summary>Tasks whose reminder time falls in (since, until]: what a notifier should show now.</summary>
    public async Task<List<(TaskInfo Task, DateTimeOffset At)>> DueRemindersAsync(DateTimeOffset since, DateTimeOffset until, CancellationToken ct = default)
    {
        var result = new List<(TaskInfo, DateTimeOffset)>();
        foreach (var t in await client.GetTasksAsync("done = false", ct: ct))
            foreach (var r in t.Reminders ?? [])
                if (VDates.OrNull(r.Reminder) is { } at && at > since && at <= until)
                    result.Add((new TaskInfo(t, await ProjectNameAsync(t.ProjectId, ct)), at));
        return result;
    }
}
