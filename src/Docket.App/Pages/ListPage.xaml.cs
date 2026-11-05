using System.Collections.ObjectModel;
using System.Globalization;
using System.Text;
using Docket.App.Controls;
using Docket.App.Services;
using Docket.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Navigation;
using Windows.ApplicationModel.DataTransfer;
using Windows.System;

namespace Docket.App.Pages;

public sealed class RowTemplateSelector : DataTemplateSelector
{
    public DataTemplate? Header { get; set; }
    public DataTemplate? Task { get; set; }
    protected override DataTemplate SelectTemplateCore(object item) => item is GroupHeader ? Header! : Task!;
    protected override DataTemplate SelectTemplateCore(object item, DependencyObject container) => SelectTemplateCore(item);
}

/// <summary>
/// One list: a smart list (My Day, Important...), an extra view (Upcoming, Open tasks), one of the user's lists, or search results.
/// Lists show as a nested tree (subtasks under their parents, to any depth) or, for the user's lists, as a table, Gantt chart or board.
/// </summary>
public sealed partial class ListPage : Page, IRefreshable, ITaskHost
{
    private ListKey _key = ListKey.Of(SmartList.MyDay);
    private readonly ObservableCollection<object> _open = [];
    private readonly ObservableCollection<object> _done = [];
    private readonly ListOptionsMenu _options;
    private SortMode _sort;
    private bool _prioritySort;
    private VProject? _project;
    private string? _results;
    private int _days = 7;
    private bool _withOverdue = true;
    private ListViewMode _mode = ListViewMode.List;
    private bool _settingUp;

    /// <summary>The open part of the tree in display groups (null title = no heading), and the completed part.</summary>
    private List<(string? Title, IReadOnlyList<TaskNode> Nodes)> _groups = [];
    private IReadOnlyList<TaskNode> _doneNodes = [];

    private TaskItem? _dragItem;
    private readonly Dictionary<ListViewMode, FrameworkElement> _views = [];

    private static readonly HashSet<string> PrioritySorted = [];

    public ListPage()
    {
        InitializeComponent();
        OpenList.ItemsSource = _open;
        DoneList.ItemsSource = _done;
        _options = new ListOptionsMenu(this, () => _project, () => _key.SettingsKey, CopyText, RefreshAsync);
        AddBar.AddTask = AddAsync;
        AddBar.TargetProject = () => _project?.Id ?? 0;
        AddBar.SearchChanged += async (_, q) => { if (q != _results) { _results = q; await RefreshAsync(); } };
        AddBar.TasksChanged += async (_, _) => await RefreshAsync();
        AddKey(VirtualKey.N, VirtualKeyModifiers.Control, () => AddBar.FocusBox());
        AddKey(VirtualKey.D, VirtualKeyModifiers.Control, () => _ = WithSelected(t => ToggleDoneAsync(t, !t.IsDone)));
        AddKey(VirtualKey.T, VirtualKeyModifiers.Control, () => _ = WithSelected(t => SetMyDayAsync(t, !t.InMyDay)));
        AddKey(VirtualKey.I, VirtualKeyModifiers.Control, () => _ = WithSelected(ToggleImportantAsync));
        AddKey(VirtualKey.Escape, VirtualKeyModifiers.None, CloseDetail);
    }

    private void AddKey(VirtualKey key, VirtualKeyModifiers mods, Action action)
    {
        var a = new KeyboardAccelerator { Key = key, Modifiers = mods };
        a.Invoked += (_, e) => { e.Handled = true; action(); };
        KeyboardAccelerators.Add(a);
    }

    private static DocketService? Svc => App.Service;
    private static MainWindow? Win => App.Window;

    /// <summary>The user's list shown here: a list, or the default list behind "Tasks".</summary>
    private bool HasProject => _project is not null;
    private bool InResults => _results is not null || _key.Search is not null;

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        if (e.Parameter is ListKey k) _key = k;
        _sort = App.Settings.Sorts.GetValueOrDefault(_key.SettingsKey, _key.Smart == SmartList.Planned ? SortMode.DueDate : SortMode.Custom);
        _prioritySort = PrioritySorted.Contains(_key.SettingsKey);
        _mode = ViewPrefs.Get(_key.SettingsKey);
        AddBar.Placeholder = _key.Smart switch
        {
            SmartList.MyDay => "Add a task to My Day",
            SmartList.Important => "Add an important task",
            SmartList.Planned => "Add a task due today",
            _ => "Add a task",
        };
        await RefreshAsync();
        // DOCKET_OPEN=<task id> opens a task's details once at start-up (development and screenshots).
        if (!_openedAtStart && long.TryParse(Environment.GetEnvironmentVariable("DOCKET_OPEN"), CultureInfo.InvariantCulture, out var openId))
        {
            _openedAtStart = true;
            await OpenDetailAsync(openId);
        }
    }

    private static bool _openedAtStart;

    // ---------- loading ----------
    public async Task RefreshAsync()
    {
        if (Svc is not { } svc || Win is null) return;
        LoadingRing.IsActive = _open.Count == 0;
        await Win.RunAsync("Couldn't load tasks", async () =>
        {
            _project = _key.IsProject ? (await svc.ProjectsAsync()).FirstOrDefault(p => p.Id == _key.ProjectId)
                : _key.Smart == SmartList.Tasks ? Win.DefaultProject : null;
            SetupHeader();
            _ = ApplyBackgroundAsync();

            var mode = HasProject && !InResults ? _mode : ListViewMode.List;
            ShowView(mode);
            if (mode != ListViewMode.List && _project is { } proj)
            {
                if (_views[mode] is IListView v) await v.LoadAsync(proj, _sort, _prioritySort);
                ShowEmpty(false);
            }
            else await LoadTreeAsync(svc);

            await Win.RefreshCountsAsync();
            if (Detail.TaskId is { } openId && Detail.Visibility == Visibility.Visible) await Detail.LoadAsync(openId);
        });
        LoadingRing.IsActive = false;
    }

    private async Task LoadTreeAsync(DocketService svc)
    {
        var now = DateTimeOffset.Now;
        TreeContent tree;
        var groups = new List<(string?, IReadOnlyList<TaskNode>)>();
        if (InResults)
        {
            var q = _results ?? _key.Search!;
            var open = await svc.ListAsync(TaskView.Open, search: q, limit: 200);
            var done = (await svc.ListAsync(TaskView.Done, search: q, limit: 200)).Where(t => t.Task.Done).ToList();
            tree = await svc.AsTreeAsync(new ListContent(open, done));
            groups.Add((null, tree.Open));
            ResultsBar.Message = $"{open.Count} open {(open.Count == 1 ? "task matches" : "tasks match")} \"{q}\"" + (done.Count > 0 ? $", {done.Count} completed" : "");
        }
        else if (_key.Extra == ExtraView.Upcoming)
        {
            var list = await svc.UpcomingAsync(_days, _withOverdue);
            tree = await svc.AsTreeAsync(new ListContent(list, []));
            foreach (var g in tree.Open.GroupBy(n => DayGroup(n.Info.Task, now)))
                groups.Add((g.Key, g.ToList()));
        }
        else if (_key.Extra == ExtraView.Open)
        {
            var list = await svc.ListAsync(TaskView.Open, limit: 500);
            var sorted = DocketService.Sort(list.Select(t => t.Task), _sort).Select(t => list.First(i => i.Id == t.Id)).ToList();
            tree = await svc.AsTreeAsync(new ListContent(sorted, []));
            foreach (var g in tree.Open.GroupBy(n => n.Info.Project))
                groups.Add((g.Key, g.ToList()));
        }
        else if (_project is { } p)
        {
            tree = await svc.ProjectTreeAsync(p.Id, _sort);
            groups.Add((null, tree.Open));
        }
        else
        {
            var content = await svc.SmartAsync(_key.Smart ?? SmartList.MyDay, _sort, App.Settings.DefaultProjectId);
            tree = await svc.AsTreeAsync(content);
            if (_key.Smart == SmartList.Planned)
                foreach (var g in tree.Open.GroupBy(n => DocketService.PlannedGroup(n.Info.Task, now)))
                    groups.Add((g.Key, g.ToList()));
            else groups.Add((null, tree.Open));
        }
        if (_prioritySort) groups = groups.Select(g => (g.Item1, (IReadOnlyList<TaskNode>)ByPriority(g.Item2))).ToList();
        _groups = groups;
        _doneNodes = tree.Completed;
        Render();
    }

    private static List<TaskNode> ByPriority(IEnumerable<TaskNode> nodes) =>
        nodes.OrderByDescending(n => n.Info.Task.Priority).ThenBy(n => n.Info.Task.Due ?? DateTimeOffset.MaxValue).ToList();

    private static string DayGroup(VTask t, DateTimeOffset now)
    {
        if (t.Due is not { } d) return "No date";
        var day = d.ToOffset(now.Offset).Date;
        if (day < now.Date) return "Overdue";
        if (day == now.Date) return "Today";
        if (day == now.Date.AddDays(1)) return "Tomorrow";
        return day.ToString(day < now.Date.AddDays(7) ? "dddd" : "ddd d MMMM", CultureInfo.CurrentCulture);
    }

    private bool ShowListName => !_key.IsProject && _key.Smart != SmartList.Tasks && _key.Extra != ExtraView.Open;

    private bool _treeColumn = true;

    private void Render()
    {
        var expanded = ViewPrefs.ExpandedOf(_key.SettingsKey);
        if (Environment.GetEnvironmentVariable("DOCKET_EXPAND") == "1" && expanded.Count == 0) ExpandAll(_groups.SelectMany(g => g.Nodes), expanded);
        _treeColumn = _groups.Any(g => g.Nodes.Any(n => n.HasChildren)) || _doneNodes.Any(n => n.HasChildren);
        _open.Clear();
        foreach (var (title, nodes) in _groups)
        {
            if (title is not null) _open.Add(new GroupHeader(title, nodes.Count));
            foreach (var row in DocketService.Flatten(nodes, expanded)) _open.Add(Item(row));
        }
        _done.Clear();
        foreach (var row in DocketService.Flatten(_doneNodes, expanded)) _done.Add(Item(row));

        var hide = App.Settings.HideCompleted.Contains(_key.SettingsKey);
        CompletedExpander.Visibility = _done.Count > 0 && !hide && _key.Smart != SmartList.Completed ? Visibility.Visible : Visibility.Collapsed;
        CompletedHeader.Text = $"Completed  {_done.Count}";
        ShowEmpty(_open.Count == 0 && (_done.Count == 0 || hide));
    }

    /// <summary>DOCKET_EXPAND=1 opens every subtask level at start-up (development and screenshots).</summary>
    private static void ExpandAll(IEnumerable<TaskNode> nodes, HashSet<string> into)
    {
        foreach (var n in nodes.Where(n => n.HasChildren)) { into.Add(n.Key); ExpandAll(n.Children, into); }
    }

    private TaskItem Item(TaskRow row) => new(row.Node.Info.Task, row.Node.Info.Project, ShowListName, row.Node, row.Expanded) { TreeColumn = _treeColumn };

    // ---------- tree ----------
    public void ToggleExpanded(TaskItem item)
    {
        var expanded = ViewPrefs.ExpandedOf(_key.SettingsKey);
        var list = _open.Contains(item) ? _open : _done;
        var index = list.IndexOf(item);
        if (index < 0 || item.Node is null) return;
        var open = !expanded.Contains(item.Key);
        if (open) expanded.Add(item.Key); else expanded.Remove(item.Key);
        var replacement = new TaskItem(item.Task, item.ProjectTitle, item.ShowList, item.Node, open) { TreeColumn = item.TreeColumn };
        list[index] = replacement;
        if (open)
        {
            var rows = DocketService.Flatten(item.Node.Children, expanded);
            for (var i = 0; i < rows.Count; i++) list.Insert(index + 1 + i, Item(rows[i]));
        }
        else
        {
            var prefix = item.Key + "/";
            while (index + 1 < list.Count && list[index + 1] is TaskItem next && next.Key.StartsWith(prefix, StringComparison.Ordinal)) list.RemoveAt(index + 1);
        }
        if (list == _open) OpenList.SelectedItem = replacement; else DoneList.SelectedItem = replacement;
    }

    private void Expand(string key)
    {
        ViewPrefs.ExpandedOf(_key.SettingsKey).Add(key);
    }

    // ---------- header ----------
    private void SetupHeader()
    {
        _settingUp = true;
        string title, sub = "";
        string? icon = null;
        string glyph;
        if (_key.Search is { } q) { title = $"Searching for \"{q}\""; glyph = ""; }
        else if (_key.Extra == ExtraView.Upcoming) { title = "Upcoming"; glyph = ""; sub = "Due soon, grouped by day"; }
        else if (_key.Extra == ExtraView.Open) { title = "Open tasks"; glyph = ""; sub = "Everything still to do, by list"; }
        else if (_key.Smart is { } s && s != SmartList.Tasks)
        {
            title = ListKey.SmartTitle(s);
            glyph = ListKey.SmartGlyph(s);
            if (s == SmartList.MyDay) sub = DateTimeOffset.Now.ToString("dddd, d MMMM", CultureInfo.CurrentCulture);
        }
        else
        {
            title = _key.Smart == SmartList.Tasks ? "Tasks" : _project?.Title ?? "List";
            glyph = _key.Smart == SmartList.Tasks ? "" : Icons.ListGlyph;
            icon = _project is { } p ? ListStyle.Parse(p.Description).Icon : null;
        }
        Heading.Text = title;
        Subheading.Text = sub;
        Subheading.Visibility = sub.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        var tint = _project is { HexColor.Length: 6 } pr ? Ui.Hex(pr.HexColor) : Ui.Brush("AccentTextFillColorPrimaryBrush");
        HeadingIcon.Content = Icons.Element(icon, 26, tint, glyph);

        SuggestButton.Visibility = Vis(_key.Smart == SmartList.MyDay && !InResults);
        ViewBar.Visibility = Vis(HasProject && !InResults);
        if (HasProject) ViewBar.SelectedItem = ViewBar.Items.FirstOrDefault(i => (string)i.Tag == _mode.ToString());
        RangeBox.Visibility = OverdueToggle.Visibility = Vis(_key.Extra == ExtraView.Upcoming);
        RangeBox.SelectedIndex = _days switch { 14 => 1, 30 => 2, _ => 0 };
        OverdueToggle.IsChecked = _withOverdue;
        ResultsBar.Visibility = Vis(_results is not null);
        AddBar.Visibility = Vis(_key.Smart != SmartList.Completed && _key.Search is null && _key.Extra != ExtraView.Upcoming || _results is not null);
        SortButton.Visibility = Vis(_key.Smart != SmartList.Completed && _key.Extra != ExtraView.Upcoming && !InResults && _mode != ListViewMode.Gantt);
        var sortName = _prioritySort ? "Priority" : _sort.ToString();
        SortText.Text = _prioritySort ? "Priority" : _sort switch
        {
            SortMode.Importance => "Importance", SortMode.DueDate => "Due date", SortMode.Alphabetical => "A–Z", SortMode.CreationDate => "Newest", _ => "Sort",
        };
        foreach (var item in SortMenu.Items.OfType<RadioMenuFlyoutItem>()) item.IsChecked = (string)item.Tag == sortName;
        MoreButton.Visibility = Vis(_key.Search is null && _key.Extra == ExtraView.None);
        _settingUp = false;
        _ = UpdateSharedAsync();
    }

    private static Visibility Vis(bool b) => b ? Visibility.Visible : Visibility.Collapsed;

    private async Task UpdateSharedAsync()
    {
        SharedChip.Visibility = Visibility.Collapsed;
        if (_project is not { } p || Svc is not { } svc) return;
        try
        {
            var s = await svc.SharingAsync(p.Id);
            if (s.People.Count + s.Teams.Count + s.Links.Count == 0 || _project?.Id != p.Id) return;
            var parts = new List<string>();
            if (s.People.Count > 0) parts.Add(s.People.Count == 1 ? "1 person" : $"{s.People.Count} people");
            if (s.Teams.Count > 0) parts.Add(s.Teams.Count == 1 ? "1 team" : $"{s.Teams.Count} teams");
            var who = parts.Count > 0 ? "Shared with " + string.Join(" and ", parts) : "";
            var link = s.Links.Count == 0 ? "" : s.Links.Count == 1 ? "public link" : $"{s.Links.Count} public links";
            SharedText.Text = who.Length > 0 && link.Length > 0 ? $"{who} · {link}" : who.Length > 0 ? who : char.ToUpper(link[0], CultureInfo.CurrentCulture) + link[1..];
            SharedChip.Visibility = Visibility.Visible;
        }
        catch (Exception ex) when (ex is HttpRequestException or VikunjaException or TaskCanceledException) { }
    }

    private async Task ApplyBackgroundAsync()
    {
        if (_project is not { } p) { BackgroundLayer.Visibility = Scrim.Visibility = Visibility.Collapsed; return; }
        var style = ListStyle.Parse(p.Description);
        var image = style.Background == "image" ? await Backgrounds.ImageAsync(p.Id) : null;
        if (_project?.Id != p.Id) return;
        var brush = Icons.Background(style, image);
        BackgroundLayer.Background = brush;
        BackgroundLayer.Visibility = Scrim.Visibility = Vis(brush is not null);
        Scrim.Opacity = style.Background == "image" ? 0.62 : 0.5;
    }

    private void ShowView(ListViewMode mode)
    {
        var list = mode == ListViewMode.List;
        Scroller.Visibility = Vis(list);
        ViewHost.Visibility = Vis(!list);
        if (list) return;
        if (!_views.TryGetValue(mode, out var view))
        {
            view = mode switch
            {
                ListViewMode.Table => new TableView(),
                ListViewMode.Gantt => new GanttView(),
                _ => new BoardView(),
            };
            if (view is IListView lv)
            {
                lv.OpenTask += async (_, id) => await OpenDetailAsync(id);
                lv.Changed += async (_, _) => { if (Detail.TaskId is { } openId && Detail.Visibility == Visibility.Visible) await Detail.LoadAsync(openId); await (Win?.RefreshCountsAsync() ?? Task.CompletedTask); };
            }
            _views[mode] = view;
        }
        ViewHost.Content = view;
    }

    private void ShowEmpty(bool empty)
    {
        EmptyPanel.Visibility = Vis(empty);
        if (!empty) return;
        (EmptyIcon.Glyph, EmptyTitle.Text, EmptyText.Text) = InResults ? ("", "Nothing found", "No tasks match your search. Try other words.")
            : _key.Extra == ExtraView.Upcoming ? ("", "Nothing coming up", $"No tasks are due in the next {_days} days.")
            : _key.Extra == ExtraView.Open ? ("", "All done", "There are no open tasks. Enjoy the quiet.")
            : _key.Smart switch
            {
                SmartList.MyDay => ("", "Focus on your day", "Add tasks below, or pick some from Suggestions."),
                SmartList.Important => ("", "Nothing starred", "Star a task to see it here."),
                SmartList.Planned => ("", "Nothing planned", "Tasks with due dates show up here."),
                SmartList.Completed => ("", "Nothing completed yet", "Completed tasks show up here."),
                _ => ("", "No tasks yet", "Add one in the bar below. Type / for commands."),
            };
    }

    // ---------- adding ----------
    private async Task AddAsync(string text)
    {
        if (Svc is not { } svc || Win is null) return;
        await Win.RunAsync("Couldn't add the task", async () =>
        {
            var project = _project?.Id.ToString(CultureInfo.InvariantCulture);
            var due = _key.Smart == SmartList.Planned && QuickAdd.Parse(text, DateTimeOffset.Now).Due is null ? "today" : null;
            var t = await svc.AddAsync(text, project, due, defaultProjectId: App.Settings.DefaultProjectId);
            if (_key.Smart == SmartList.MyDay) await svc.SetMyDayAsync(t.Id, true);
            if (_key.Smart == SmartList.Important) await svc.SetImportantAsync(t.Id, true);
            Win.SetStatus($"Added to {t.Project}");
            await RefreshAsync();
        });
    }

    // ---------- row actions (ITaskHost) ----------
    public async Task ToggleDoneAsync(TaskItem t, bool done)
    {
        if (Svc is not { } svc || Win is null) return;
        await Win.RunAsync("Couldn't update the task", async () =>
        {
            var wasDue = t.Task.DueDate;
            var r = await svc.SetDoneAsync(t.Id, done);
            t.Replace(r.Task);
            if (done && !r.Task.Done) Win.SetStatus($"Repeats: next due {r.Task.Due?.ToLocalTime().ToString("ddd d MMM", CultureInfo.CurrentCulture)}");
            if (done) Win.OfferUndo($"Completed \"{t.Title}\"", async () =>
            {
                if (r.Task.Done) await svc.SetDoneAsync(t.Id, false);
                else await svc.Client.ModifyTaskAsync(t.Id, x => x.DueDate = wasDue);
            });
            await Task.Delay(300);
            await RefreshAsync();
        });
    }

    public async Task ToggleImportantAsync(TaskItem t)
    {
        if (Svc is not { } svc || Win is null) return;
        await Win.RunAsync("Couldn't update the task", async () =>
        {
            t.Replace((await svc.SetImportantAsync(t.Id, !t.IsImportant)).Task);
            if (_key.Smart == SmartList.Important) await RefreshAsync(); else await Win.RefreshCountsAsync();
            if (Detail.TaskId == t.Id) await Detail.LoadAsync(t.Id);
        });
    }

    private async Task SetMyDayAsync(TaskItem t, bool on)
    {
        if (Svc is not { } svc || Win is null) return;
        await Win.RunAsync("Couldn't update My Day", async () => { t.Replace((await svc.SetMyDayAsync(t.Id, on)).Task); if (_key.Smart == SmartList.MyDay) await RefreshAsync(); else await Win.RefreshCountsAsync(); });
    }

    private async Task SetDueAsync(TaskItem t, string due)
    {
        if (Svc is not { } svc || Win is null) return;
        await Win.RunAsync("Couldn't set the due date", async () => { await svc.UpdateAsync(t.Id, due: due); await RefreshAsync(); });
    }

    private async Task SetPriorityAsync(TaskItem t, int p)
    {
        if (Svc is not { } svc || Win is null) return;
        await Win.RunAsync("Couldn't set the priority", async () => { t.Replace((await svc.SetPriorityAsync(t.Id, p)).Task); if (_prioritySort) await RefreshAsync(); });
    }

    public async Task DeleteAsync(TaskItem t)
    {
        if (Svc is not { } svc || Win is null) return;
        if (App.Settings.ConfirmDelete && !await Dialogs.ConfirmAsync(XamlRoot, "Delete task?", $"\"{t.Title}\" will be permanently deleted.")) return;
        await Win.RunAsync("Couldn't delete the task", async () =>
        {
            var snapshot = await svc.GetAsync(t.Id);
            await svc.DeleteTaskAsync(t.Id);
            if (Detail.TaskId == t.Id) CloseDetail();
            Win.OfferUndo($"Deleted \"{t.Title}\"", () => RestoreAsync(svc, snapshot));
            await RefreshAsync();
        });
    }

    /// <summary>Undo for delete: recreates the task with its fields and links (comments and files can't come back).</summary>
    public static async Task RestoreAsync(DocketService svc, VTask s)
    {
        var created = await svc.Client.CreateTaskAsync(s.ProjectId, s.Title, s.Due, s.Priority) ?? throw new DocketException("Couldn't restore the task.");
        await svc.Client.ModifyTaskAsync(created.Id, x =>
        {
            x.Description = s.Description; x.IsFavorite = s.IsFavorite; x.Reminders = s.Reminders;
            x.RepeatAfter = s.RepeatAfter; x.RepeatMode = s.RepeatMode; x.Done = s.Done; x.StartDate = s.StartDate; x.EndDate = s.EndDate;
        });
        if (s.Labels is { Count: > 0 } ls) await svc.Client.SetTaskLabelsAsync(created.Id, ls.Select(l => l.Id));
        foreach (var p in s.Parents) await svc.Client.AddRelationAsync(p.Id, created.Id, "subtask");
        foreach (var c in s.Subtasks) await svc.Client.AddRelationAsync(created.Id, c.Id, "subtask");
        foreach (var a in s.Assignees ?? []) await svc.Client.AssignAsync(created.Id, a.Id);
    }

    private async Task WithSelected(Func<TaskItem, Task> action)
    {
        if ((OpenList.SelectedItem ?? DoneList.SelectedItem) is TaskItem t) await action(t);
    }

    private async void List_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (((ListView)sender).SelectedItem is not TaskItem t) return;
        switch (e.Key)
        {
            case VirtualKey.Delete: e.Handled = true; await DeleteAsync(t); break;
            case VirtualKey.Enter: e.Handled = true; await OpenDetailAsync(t.Id); break;
            case VirtualKey.Right when t.HasChildren && !t.IsExpanded: e.Handled = true; ToggleExpanded(t); break;
            case VirtualKey.Left when t.HasChildren && t.IsExpanded: e.Handled = true; ToggleExpanded(t); break;
        }
    }

    // ---------- subtasks ----------
    private async Task AddSubtaskAsync(TaskItem parent)
    {
        if (Svc is not { } svc || Win is null) return;
        var text = await Dialogs.PromptAsync(XamlRoot, "Add subtask", $"Subtask of \"{parent.Title}\"", "", "Add");
        if (string.IsNullOrWhiteSpace(text)) return;
        await Win.RunAsync("Couldn't add the subtask", async () =>
        {
            await svc.AddSubtaskAsync(parent.Id, text);
            Expand(parent.Key);
            await RefreshAsync();
        });
    }

    private async Task MakeSubtaskOfAsync(TaskItem child)
    {
        if (Svc is not { } svc || Win is null) return;
        var pick = await Dialogs.PickTaskAsync(XamlRoot, $"Make \"{child.Title}\" a subtask of…", "Make subtask", new HashSet<long> { child.Id });
        if (pick is null) return;
        await LinkAsync(pick.Id, pick.Task.Title, child);
    }

    private async Task LinkAsync(long parentId, string parentTitle, TaskItem child)
    {
        if (Svc is not { } svc || Win is null) return;
        await Win.RunAsync("Couldn't make it a subtask", async () =>
        {
            await svc.LinkSubtaskAsync(parentId, child.Id);
            foreach (var row in _open.OfType<TaskItem>().Where(r => r.Id == parentId)) Expand(row.Key);
            Win.SetStatus($"\"{child.Title}\" is now a subtask of \"{parentTitle}\"");
            Win.OfferUndo($"Made \"{child.Title}\" a subtask", () => svc.UnlinkSubtaskAsync(parentId, child.Id));
            await RefreshAsync();
        });
    }

    /// <summary>The parent a row sits under in the tree (from its key "parent/child").</summary>
    private static long? ParentOf(TaskItem t)
    {
        var parts = t.Key.Split('/');
        return parts.Length >= 2 && long.TryParse(parts[^2], CultureInfo.InvariantCulture, out var id) ? id : null;
    }

    // ---------- context menu ----------
    private void Row_RightTapped(object sender, RightTappedRoutedEventArgs e)
    {
        if ((e.OriginalSource as FrameworkElement)?.DataContext is not TaskItem t) return;
        var m = new MenuFlyout();
        void Add(string text, string glyph, Func<Task> action)
        {
            var item = new MenuFlyoutItem { Text = text, Icon = new FontIcon { Glyph = glyph } };
            item.Click += async (_, _) => await action();
            m.Items.Add(item);
        }
        Add(t.InMyDay ? "Remove from My Day" : "Add to My Day", "", () => SetMyDayAsync(t, !t.InMyDay));
        Add(t.IsImportant ? "Remove importance" : "Mark as important", "", () => ToggleImportantAsync(t));
        Add(t.IsDone ? "Mark as not completed" : "Mark as completed", "", () => ToggleDoneAsync(t, !t.IsDone));
        m.Items.Add(new MenuFlyoutSeparator());
        Add("Add subtask", "", () => AddSubtaskAsync(t));
        Add("Make subtask of…", "", () => MakeSubtaskOfAsync(t));
        if (ParentOf(t) is { } parent && Svc is { } svc0 && Win is { } w0)
            Add("Remove from parent", "", () => w0.RunAsync("Couldn't move it out", async () => { await svc0.UnlinkSubtaskAsync(parent, t.Id); await RefreshAsync(); }));
        m.Items.Add(new MenuFlyoutSeparator());
        Add("Due today", "", () => SetDueAsync(t, "today"));
        Add("Due tomorrow", "", () => SetDueAsync(t, "tomorrow"));
        if (t.HasDue) Add("Remove due date", "", () => SetDueAsync(t, "none"));
        var prio = new MenuFlyoutSubItem { Text = "Priority", Icon = new FontIcon { Glyph = "" } };
        for (var p = 0; p <= 5; p++)
        {
            var level = p;
            var item = new RadioMenuFlyoutItem { Text = DocketService.PriorityName(p), GroupName = "prio", IsChecked = t.Task.Priority == p };
            if (p > 0) item.Icon = new FontIcon { Glyph = "", Foreground = Ui.Hex(Ui.PriorityHex(p)) };
            item.Click += async (_, _) => await SetPriorityAsync(t, level);
            prio.Items.Add(item);
        }
        m.Items.Add(prio);
        var move = new MenuFlyoutSubItem { Text = "Move task to…", Icon = new FontIcon { Glyph = "" } };
        foreach (var p in Win?.Projects ?? [])
        {
            if (p.Id == t.Task.ProjectId) continue;
            var item = new MenuFlyoutItem { Text = p.Title };
            item.Click += async (_, _) => { if (Svc is { } s && Win is { } w) await w.RunAsync("Couldn't move the task", async () => { await s.UpdateAsync(t.Id, project: p.Id.ToString(CultureInfo.InvariantCulture)); await RefreshAsync(); }); };
            move.Items.Add(item);
        }
        m.Items.Add(move);
        m.Items.Add(new MenuFlyoutSeparator());
        Add("Delete task", "", () => DeleteAsync(t));
        m.ShowAt((FrameworkElement)e.OriginalSource, e.GetPosition((FrameworkElement)e.OriginalSource));
        e.Handled = true;
    }

    // ---------- drag: onto a row = subtask, between rows = reorder, onto the sidebar = move ----------
    private void Rows_DragItemsStarting(object sender, DragItemsStartingEventArgs e)
    {
        if (e.Items.FirstOrDefault() is TaskItem t)
        {
            _dragItem = t;
            e.Data.SetText($"docket-task:{t.Id}");
            e.Data.RequestedOperation = DataPackageOperation.Move;
        }
        else e.Cancel = true;
    }

    private void Rows_DragItemsCompleted(ListViewBase sender, DragItemsCompletedEventArgs args) => _dragItem = null;

    private enum DropKind { None, Subtask, Before, After }

    private (DropKind Kind, TaskItem? Target) DropTarget(DragEventArgs e)
    {
        if (_dragItem is not { } drag) return (DropKind.None, null);
        var target = (e.OriginalSource as FrameworkElement)?.DataContext as TaskItem;
        if (target is null || target.Id == drag.Id) return (DropKind.None, null);
        if (OpenList.ContainerFromItem(target) is not ListViewItem container) return (DropKind.Subtask, target);
        var y = e.GetPosition(container).Y;
        var h = Math.Max(1, container.ActualHeight);
        var canReorder = HasProject && _sort == SortMode.Custom && !_prioritySort && drag.Depth == 0 && target.Depth == 0 && !InResults;
        if (canReorder && y < h * 0.25) return (DropKind.Before, target);
        if (canReorder && y > h * 0.75) return (DropKind.After, target);
        return (DropKind.Subtask, target);
    }

    private void Rows_DragOver(object sender, DragEventArgs e)
    {
        var (kind, target) = DropTarget(e);
        if (kind == DropKind.None || target is null) { e.AcceptedOperation = DataPackageOperation.None; return; }
        e.AcceptedOperation = DataPackageOperation.Move;
        e.DragUIOverride.Caption = kind == DropKind.Subtask ? $"Make subtask of \"{target.Title}\"" : "Move here";
        e.DragUIOverride.IsGlyphVisible = false;
    }

    private async void Rows_Drop(object sender, DragEventArgs e)
    {
        var (kind, target) = DropTarget(e);
        var drag = _dragItem;
        _dragItem = null;
        if (drag is null || target is null || Svc is not { } svc || Win is null) return;
        if (kind == DropKind.Subtask) { await LinkAsync(target.Id, target.Title, drag); return; }
        var view = _project?.ListView;
        if (view is null) return;
        var tops = _open.OfType<TaskItem>().Where(r => r.Depth == 0 && r.Id != drag.Id).ToList();
        var i = tops.IndexOf(target);
        var before = kind == DropKind.Before ? (i > 0 ? tops[i - 1].Task.Position : target.Task.Position - 131072) : target.Task.Position;
        var after = kind == DropKind.Before ? target.Task.Position : (i < tops.Count - 1 ? tops[i + 1].Task.Position : target.Task.Position + 131072);
        if (after <= before) after = before + 131072;
        var pos = (before + after) / 2;
        await Win.RunAsync("Couldn't reorder", async () => { await svc.Client.SetPositionAsync(drag.Id, view.Id, pos); await RefreshAsync(); });
    }

    // ---------- details ----------
    private async void Row_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is TaskItem t) await OpenDetailAsync(t.Id);
    }

    private async Task OpenDetailAsync(long id)
    {
        DetailColumn.Width = new GridLength(400);
        Detail.Visibility = Visibility.Visible;
        await Detail.LoadAsync(id);
    }

    private void CloseDetail()
    {
        Detail.Visibility = Visibility.Collapsed;
        DetailColumn.Width = new GridLength(0);
        Detail.Clear();
    }

    private void Detail_Closed(object? sender, EventArgs e) => CloseDetail();

    private async void Detail_Changed(object? sender, TaskChange change)
    {
        if (change == TaskChange.Deleted) CloseDetail();
        await RefreshAsync();
    }

    private async void Detail_OpenTask(object? sender, long id) => await OpenDetailAsync(id);

    // ---------- header actions ----------
    /// <summary>When the list is narrow (details open), the header buttons move under the title.</summary>
    private void HeaderGrid_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        var narrow = e.NewSize.Width < 760;
        Grid.SetRow(HeaderActions, narrow ? 1 : 0);
        Grid.SetColumn(HeaderActions, narrow ? 0 : 1);
        HeaderActions.HorizontalAlignment = narrow ? HorizontalAlignment.Left : HorizontalAlignment.Right;
    }

    private async void ViewBar_SelectionChanged(SelectorBar sender, SelectorBarSelectionChangedEventArgs args)
    {
        if (_settingUp || sender.SelectedItem?.Tag is not string tag || !Enum.TryParse<ListViewMode>(tag, out var mode) || mode == _mode) return;
        _mode = mode;
        ViewPrefs.Set(_key.SettingsKey, mode);
        await RefreshAsync();
    }

    private async void Range_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_settingUp) return;
        _days = RangeBox.SelectedIndex switch { 1 => 14, 2 => 30, _ => 7 };
        await RefreshAsync();
    }

    private async void Overdue_Click(object sender, RoutedEventArgs e)
    {
        _withOverdue = OverdueToggle.IsChecked == true;
        await RefreshAsync();
    }

    private async void ClearResults_Click(object sender, RoutedEventArgs e)
    {
        _results = null;
        await RefreshAsync();
    }

    private async void Sort_Click(object sender, RoutedEventArgs e)
    {
        var tag = (string)((FrameworkElement)sender).Tag;
        _prioritySort = tag == "Priority";
        if (_prioritySort) { PrioritySorted.Add(_key.SettingsKey); _sort = SortMode.Custom; }
        else
        {
            PrioritySorted.Remove(_key.SettingsKey);
            _sort = Enum.Parse<SortMode>(tag);
        }
        App.Settings.Sorts[_key.SettingsKey] = _sort;
        App.Settings.Save();
        await RefreshAsync();
    }

    private async void Heading_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e) => await _options.RenameAsync();

    private void MoreMenu_Opening(object sender, object e)
    {
        _options.ShowCompletedOption = _key.Smart != SmartList.Completed;
        _options.Fill(MoreMenu);
    }

    private string CopyText()
    {
        var sb = new StringBuilder(Heading.Text).AppendLine();
        foreach (var o in _open)
            if (o is GroupHeader h) sb.AppendLine().AppendLine(h.Title);
            else if (o is TaskItem t) sb.AppendLine($"{new string(' ', t.Depth * 2)}- [ ] {t.Title}{(t.HasDue ? $" ({t.DueText})" : "")}");
        foreach (var t in _done.OfType<TaskItem>()) sb.AppendLine($"{new string(' ', t.Depth * 2)}- [x] {t.Title}");
        return sb.ToString();
    }

    // ---------- My Day suggestions ----------
    private async void Suggest_Opening(object sender, object e)
    {
        if (Svc is not { } svc || Win is null) return;
        SuggestPanel.Children.Clear();
        SuggestPanel.Children.Add(new ProgressRing { IsActive = true, Width = 24, Height = 24, Margin = new Thickness(12) });
        await Win.RunAsync("Couldn't load suggestions", async () =>
        {
            var items = await svc.MyDaySuggestionsAsync();
            SuggestPanel.Children.Clear();
            if (items.Count == 0) { SuggestPanel.Children.Add(new TextBlock { Text = "No suggestions right now.", Opacity = 0.7, Margin = new Thickness(8) }); return; }
            foreach (var g in items.GroupBy(s => s.Group))
            {
                SuggestPanel.Children.Add(new TextBlock { Text = g.Key, Style = Ui.TextStyle("BodyStrongTextBlockStyle"), Margin = new Thickness(4, 10, 0, 2) });
                foreach (var s in g)
                {
                    var row = new Grid { ColumnSpacing = 8, Padding = new Thickness(4) };
                    row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                    row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                    var text = new StackPanel();
                    text.Children.Add(new TextBlock { Text = s.Task.Task.Title, TextWrapping = TextWrapping.Wrap });
                    text.Children.Add(new TextBlock { Text = s.Task.Project, Opacity = 0.6, Style = Ui.TextStyle("CaptionTextBlockStyle") });
                    var add = new Button { Content = new FontIcon { Glyph = "", FontSize = 12 }, Padding = new Thickness(8, 4, 8, 4) };
                    AutomationProperties.SetName(add, $"Add {s.Task.Task.Title} to My Day");
                    var id = s.Task.Id;
                    add.Click += async (_, _) => { add.IsEnabled = false; await Win.RunAsync("Couldn't add to My Day", async () => { await svc.SetMyDayAsync(id, true); row.Opacity = 0.4; await RefreshAsync(); }); };
                    Grid.SetColumn(add, 1);
                    row.Children.Add(text);
                    row.Children.Add(add);
                    SuggestPanel.Children.Add(row);
                }
            }
        });
    }
}

/// <summary>A view of a list other than the nested list: table, Gantt chart or board.</summary>
public interface IListView
{
    Task LoadAsync(VProject project, SortMode sort, bool prioritySort);
    event EventHandler<long>? OpenTask;
    event EventHandler? Changed;
}
