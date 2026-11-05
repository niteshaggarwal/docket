using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Docket.Core;
using Docket.Mobile.Services;
using Docket.Mobile.Views;

namespace Docket.Mobile.ViewModels;

/// <summary>A subtask under the open task, at any depth. <see cref="ParentId"/> is the task it hangs from here.</summary>
public sealed record SubRow(VTask Task, int Depth, long ParentId, int ChildCount, int ChildDone)
{
    public long Id => Task.Id;
    public string Title => Task.Title;
    public bool Done => Task.Done;
    public double Opacity => Done ? 0.55 : 1;
    public TextDecorations Decorations => Done ? TextDecorations.Strikethrough : TextDecorations.None;
    public Thickness Indent => new(Math.Min(Depth - 1, 6) * 22, 0, 0, 0);
    public string Meta
    {
        get
        {
            var parts = new List<string>();
            if (ChildCount > 0) parts.Add($"{ChildDone} of {ChildCount}");
            if (Task.Due is { } d) parts.Add(TaskRow.DayLabel(d.ToLocalTime().Date));
            if (Task.Priority > 0 && !Done) parts.Add(DocketService.PriorityName(Task.Priority));
            if (Task.Assignees is { Count: > 0 } a) parts.Add(string.Join(", ", a.Select(DocketService.DisplayName)));
            return string.Join("  ·  ", parts);
        }
    }
    public bool HasMeta => Meta.Length > 0;
    public string StarGlyph => Task.IsFavorite ? IconChars.StarFilled : "";
}

public sealed record ParentRow(long Id, string Title, string Project);

public sealed record PersonRow(VUser User)
{
    public string Name => DocketService.DisplayName(User);
    public string Initials => DocketService.Initials(User);
    public Color Color => Th.AvatarColor(User.Username);
    public string Username => "@" + User.Username;
}

public sealed record HistoryRow(string What, string When);

public sealed partial class TaskViewModel
{
    public ObservableCollection<SubRow> Subtasks { get; } = [];
    public ObservableCollection<ParentRow> Parents { get; } = [];
    public ObservableCollection<PersonRow> Assignees { get; } = [];
    public ObservableCollection<HistoryRow> History { get; } = [];

    [ObservableProperty] public partial string NewSubtask { get; set; } = "";
    [ObservableProperty] public partial string PriorityText { get; set; } = "Priority";
    [ObservableProperty] public partial Color? PriorityColor { get; set; }
    [ObservableProperty] public partial string StartText { get; set; } = "Add start date";
    [ObservableProperty] public partial string EndText { get; set; } = "Add end date";
    [ObservableProperty] public partial bool HasSteps { get; set; }
    [ObservableProperty] public partial bool HasParents { get; set; }
    [ObservableProperty] public partial bool HasAssignees { get; set; }
    [ObservableProperty] public partial string SubtasksHeader { get; set; } = "";

    private void RenderMore(VTask t)
    {
        var p = Math.Clamp(t.Priority, 0, 5);
        PriorityText = p == 0 ? "Set priority" : $"Priority: {DocketService.PriorityName(p)}";
        PriorityColor = p == 0 ? null : Th.Priority(p).Now;
        StartText = VDates.OrNull(t.StartDate) is { } s ? $"Starts {s.ToLocalTime().ToString("ddd, d MMMM", Ui)}" : "Add start date";
        EndText = VDates.OrNull(t.EndDate) is { } e ? $"Ends {e.ToLocalTime().ToString("ddd, d MMMM", Ui)}" : "Add end date";
        HasSteps = _notes.Steps.Count > 0;

        Parents.Clear();
        foreach (var parent in t.Parents) Parents.Add(new ParentRow(parent.Id, parent.Title, ""));
        HasParents = Parents.Count > 0;
        Assignees.Clear();
        foreach (var u in t.Assignees ?? []) Assignees.Add(new PersonRow(u));
        HasAssignees = Assignees.Count > 0;
        History.Clear();
        foreach (var (what, when) in DocketService.History(t))
            History.Add(new HistoryRow(what, when.ToLocalTime().ToString("ddd d MMM yyyy, HH:mm", Ui)));
    }

    /// <summary>Fetches subtasks level by level (they can nest to any depth) and lays them out indented.</summary>
    private async Task LoadSubtasksAsync(DocketService svc, VTask root)
    {
        var known = new Dictionary<long, TaskInfo> { [root.Id] = new TaskInfo(root, ListText) };
        var level = root.Subtasks.Select(s => s.Id).ToList();
        for (var depth = 0; depth < 8 && level.Count > 0 && known.Count < 80; depth++)
        {
            var fetched = await Task.WhenAll(level.Where(id => !known.ContainsKey(id)).Distinct().Select(id => svc.Client.GetTaskAsync(id)));
            var next = new List<long>();
            foreach (var f in fetched.OfType<VTask>())
            {
                known[f.Id] = new TaskInfo(f, "");
                next.AddRange(f.Subtasks.Select(s => s.Id));
            }
            level = next;
        }
        var tree = DocketService.BuildTree([known[root.Id]], known);
        Subtasks.Clear();
        void Walk(TaskNode n, long parent)
        {
            foreach (var c in n.Children)
            {
                Subtasks.Add(new SubRow(c.Info.Task, c.Depth, n.Id, c.Children.Count, c.DoneChildren));
                Walk(c, c.Id);
            }
        }
        if (tree.Count > 0) Walk(tree[0], root.Id);
        var direct = tree.Count > 0 ? tree[0].Children : [];
        SubtasksHeader = direct.Count == 0 ? "" : $"Subtasks  {direct.Count(c => c.Info.Task.Done)} of {direct.Count}";
    }

    [RelayCommand]
    private Task AddSubtaskAsync()
    {
        var text = NewSubtask.Trim();
        if (text.Length == 0) return Task.CompletedTask;
        NewSubtask = "";
        return SaveAsync(s => s.AddSubtaskAsync(_id, text));
    }

    [RelayCommand] private Task ToggleSubtaskAsync(SubRow row) { HapticFeedback.Default.Perform(HapticFeedbackType.Click); return SaveAsync(s => s.SetDoneAsync(row.Id, !row.Done)); }

    [RelayCommand] private Task OpenSubtaskAsync(SubRow row) => Shell.Current.GoToAsync($"task?id={row.Id}");

    [RelayCommand]
    private async Task SubtaskMenuAsync(SubRow row)
    {
        var pick = await ChoiceSheet.PickAsync(row.Title, [
            new("Open", IconChars.ArrowRight),
            new(row.Done ? "Mark as not done" : "Mark as done", IconChars.Done),
            new("Take out of this task", IconChars.Dismiss, "It stays a task of its own"),
            new("Delete task", IconChars.Delete, Danger: true),
        ]);
        switch (pick)
        {
            case 0: await OpenSubtaskAsync(row); break;
            case 1: await ToggleSubtaskAsync(row); break;
            case 2: await SaveAsync(s => s.UnlinkSubtaskAsync(row.ParentId, row.Id)); break;
            case 3:
                if (await Shell.Current.DisplayAlertAsync("Delete task?", $"“{row.Title}” will be permanently deleted.", "Delete", "Cancel"))
                    await SaveAsync(s => s.DeleteTaskAsync(row.Id));
                break;
        }
    }

    [RelayCommand]
    private async Task AddExistingSubtaskAsync()
    {
        if (session.Service is not { } svc) return;
        var exclude = Subtasks.Select(s => s.Id).Append(_id).Concat(Parents.Select(p => p.Id));
        var pick = await Sheet<TaskInfo>.ShowAsync(new TaskPickerSheet(svc, "Add an existing task as a subtask", exclude));
        if (pick is not null) await SaveAsync(s => s.LinkSubtaskAsync(_id, pick.Id));
    }

    [RelayCommand]
    private async Task AddParentAsync()
    {
        if (session.Service is not { } svc) return;
        var exclude = Subtasks.Select(s => s.Id).Append(_id).Concat(Parents.Select(p => p.Id));
        var pick = await Sheet<TaskInfo>.ShowAsync(new TaskPickerSheet(svc, "Put this task under…", exclude));
        if (pick is not null) await SaveAsync(s => s.LinkSubtaskAsync(pick.Id, _id));
    }

    [RelayCommand] private Task OpenParentAsync(ParentRow p) => Shell.Current.GoToAsync($"task?id={p.Id}");

    [RelayCommand] private Task RemoveParentAsync(ParentRow p) => SaveAsync(s => s.UnlinkSubtaskAsync(p.Id, _id));

    [RelayCommand]
    private async Task ConvertStepsAsync()
    {
        if (!await Shell.Current.DisplayAlertAsync("Turn steps into subtasks?", $"Each of the {_notes.Steps.Count} steps becomes a real subtask (with its own dates, star and reminders). Ticked steps stay done.", "Turn into subtasks", "Cancel")) return;
        await SaveAsync(s => s.ConvertStepsToSubtasksAsync(_id));
    }

    [RelayCommand]
    private async Task PriorityAsync()
    {
        var current = Math.Clamp(_task?.Priority ?? 0, 0, 5);
        var choices = Enumerable.Range(0, 6).Reverse().Select(p => new Choice(p == 0 ? "No priority" : DocketService.PriorityName(p), p == 0 ? IconChars.Dismiss : IconChars.Flag, Selected: p == current)).ToList();
        var pick = await ChoiceSheet.PickAsync("Priority", choices);
        if (pick < 0) return;
        var value = 5 - pick;
        if (value != current) await SaveAsync(s => s.SetPriorityAsync(_id, value));
    }

    [RelayCommand] private Task StartAsync() => SpanAsync(start: true);
    [RelayCommand] private Task EndAsync() => SpanAsync(start: false);

    private async Task SpanAsync(bool start)
    {
        if (_task is null) return;
        var current = VDates.OrNull(start ? _task.StartDate : _task.EndDate);
        if (current is not null)
        {
            var pick = await ChoiceSheet.PickAsync(start ? "Start date" : "End date", [new("Change date", IconChars.Calendar), new("Remove date", IconChars.Dismiss, Danger: true)]);
            if (pick < 0) return;
            if (pick == 1) { await SaveAsync(s => s.SetSpanAsync(_id, start ? VDates.Empty : null, start ? null : VDates.Empty)); return; }
        }
        var initial = current?.ToLocalTime().DateTime ?? (start ? DateTime.Today : (VDates.OrNull(_task.StartDate)?.ToLocalTime().DateTime ?? DateTime.Today).AddDays(2));
        var d = await Sheet<DateTime?>.ShowAsync(new DateTimeSheet(false, initial, start ? "Start date" : "End date"));
        if (d is not { } day) return;
        var at = new DateTimeOffset(day.Date.AddHours(start ? 9 : 18));
        await SaveAsync(s => s.SetSpanAsync(_id, start ? at : null, start ? null : at));
    }

    [RelayCommand]
    private async Task AssignAsync()
    {
        if (session.Service is not { } svc) return;
        try
        {
            var people = (await svc.PeopleAsync()).Where(p => Assignees.All(a => a.User.Id != p.Id)).ToList();
            var me = await svc.MeAsync();
            if (people.Count == 0) { await Shell.Current.DisplayAlertAsync("Assign", "Everyone you work with is already assigned. Add people from Docket on your PC.", "OK"); return; }
            var pick = await ChoiceSheet.PickAsync("Assign to", people.Select(p => new Choice(p.Id == me.Id ? $"{DocketService.DisplayName(p)} (you)" : DocketService.DisplayName(p), IconChars.Person, "@" + p.Username)).ToList(),
                "If they can't see this list yet, Docket shares it with them (can edit).");
            if (pick >= 0) await SaveAsync(s => s.AssignAsync(_id, people[pick]));
        }
        catch (Exception ex) when (U.IsExpected(ex)) { Error = U.Friendly(ex); }
    }

    [RelayCommand]
    private async Task RemoveAssigneeAsync(PersonRow p)
    {
        if (await Shell.Current.DisplayAlertAsync("Remove assignee?", $"{p.Name} will no longer be assigned to this task.", "Remove", "Cancel"))
            await SaveAsync(s => s.UnassignAsync(_id, p.User.Id));
    }
}
