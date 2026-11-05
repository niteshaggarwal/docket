using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Docket.Core;
using Docket.Mobile.Services;
using Docket.Mobile.Views;
using Icons = Docket.Mobile.Services.Icons;

namespace Docket.Mobile.ViewModels;

/// <summary>
/// One list: a smart list, one of the user's lists, a view (Upcoming, Open tasks) or search results.
/// Tasks are shown as a tree (subtasks under their parents) in List view; the same tasks drive Table, Timeline and Board.
/// </summary>
public sealed partial class ListViewModel(Session session) : ObservableObject, IQueryAttributable
{
    private static readonly Dictionary<string, HashSet<string>> ExpandedByList = [];
    private static readonly Dictionary<long, (string Stamp, byte[] Bytes)> BackgroundCache = [];

    private string _key = "smart:MyDay";
    private VProject? _project;
    private TreeContent _tree = new([], []);
    private ListStyle _style = new();

    public ObservableCollection<object> Rows { get; } = [];
    public ObservableCollection<TaskRow> Done { get; } = [];

    /// <summary>Raised when the tasks or board change, so the Table, Timeline and Board views redraw.</summary>
    public event EventHandler? DataChanged;

    [ObservableProperty] public partial string Title { get; set; } = "";
    [ObservableProperty] public partial string Subtitle { get; set; } = "";
    [ObservableProperty] public partial string Glyph { get; set; } = IconChars.Sun;
    [ObservableProperty] public partial bool GlyphIsEmoji { get; set; }
    [ObservableProperty] public partial Color? Tint { get; set; }
    [ObservableProperty] public partial bool IsBusy { get; set; }
    [ObservableProperty] public partial string? Error { get; set; }
    [ObservableProperty] public partial bool ShowDone { get; set; }
    [ObservableProperty] public partial bool DoneExpanded { get; set; }
    [ObservableProperty] public partial bool IsEmpty { get; set; }
    [ObservableProperty] public partial string EmptyText { get; set; } = "";
    [ObservableProperty] public partial bool CanAdd { get; set; } = true;
    [ObservableProperty] public partial bool IsMyDay { get; set; }
    [ObservableProperty] public partial string ViewMode { get; set; } = "list";
    [ObservableProperty] public partial bool HasBackground { get; set; }
    [ObservableProperty] public partial Brush? BackgroundBrush { get; set; }
    [ObservableProperty] public partial ImageSource? BackgroundImage { get; set; }

    public string GlyphFont => GlyphIsEmoji ? "" : "FluentIcons";
    public double GlyphSize => GlyphIsEmoji ? 24 : 26;
    partial void OnGlyphIsEmojiChanged(bool value) { OnPropertyChanged(nameof(GlyphFont)); OnPropertyChanged(nameof(GlyphSize)); }

    public bool IsListView => ViewMode == "list";
    public bool IsOtherView => ViewMode != "list";
    partial void OnViewModeChanged(string value) { OnPropertyChanged(nameof(IsListView)); OnPropertyChanged(nameof(IsOtherView)); }

    /// <summary>Text on top of a background picture or colour is white; otherwise it follows the theme.</summary>
    public Color HeaderColor => HasBackground ? Colors.White : Th.Text.Now;
    public Color HeaderColor2 => HasBackground ? Colors.White.WithAlpha(0.85f) : Th.Text2.Now;
    partial void OnHasBackgroundChanged(bool value) { OnPropertyChanged(nameof(HeaderColor)); OnPropertyChanged(nameof(HeaderColor2)); OnPropertyChanged(nameof(IconColor)); }
    partial void OnTintChanged(Color? value) => OnPropertyChanged(nameof(IconColor));

    /// <summary>The list icon: white on a background, the list's colour (or the accent) otherwise.</summary>
    public Color IconColor => HasBackground ? Colors.White : Tint ?? Th.Accent.Now;

    /// <summary>The status bar behind a list background: its top colour under the scrim, so the page runs edge to edge.</summary>
    public Color? StatusBarColor
    {
        get
        {
            if (!HasBackground) return null;
            var top = BackgroundBrush switch
            {
                SolidColorBrush s => s.Color,
                LinearGradientBrush g when g.GradientStops.Count > 0 => g.GradientStops[0].Color,
                _ => Color.FromArgb("#303030"),
            };
            return new Color(top.Red * 0.45f, top.Green * 0.45f, top.Blue * 0.45f);
        }
    }

    public string DoneHeader => $"Completed  {Done.Count}";
    public string Key => _key;
    public VProject? Project => _project;
    public ListStyle Style => _style;
    public Board? Board { get; private set; }
    public bool IsProject => _project is not null;
    public bool IsUpcoming => _key == "view:upcoming";
    public SmartList? Smart => _key.StartsWith("smart:", StringComparison.Ordinal) ? Enum.Parse<SmartList>(_key[6..]) : null;
    private string? Search => _key.StartsWith("search:", StringComparison.Ordinal) ? _key[7..] : null;
    private long ProjectId => _key.StartsWith("list:", StringComparison.Ordinal) ? long.Parse(_key[5..], CultureInfo.InvariantCulture) : 0;
    private HashSet<string> Expanded => ExpandedByList.TryGetValue(_key, out var e) ? e : ExpandedByList[_key] = [];

    /// <summary>The views this list offers: Board needs a real list with a board.</summary>
    public IReadOnlyList<(string Id, string Name, string Glyph)> Views =>
    [
        ("list", "List", IconChars.List), ("table", "Table", IconChars.Table), ("gantt", "Timeline", IconChars.Gantt),
        .. (_project?.KanbanView is not null ? new[] { ("board", "Board", IconChars.Board) } : Array.Empty<(string, string, string)>()),
    ];

    /// <summary>All open tasks of the list once (subtasks included), for Table and Timeline.</summary>
    public IReadOnlyList<TaskInfo> FlatOpen
    {
        get
        {
            var seen = new HashSet<long>();
            var list = new List<TaskInfo>();
            void Walk(TaskNode n) { if (!n.Info.Task.Done && seen.Add(n.Id)) list.Add(n.Info); foreach (var c in n.Children) Walk(c); }
            foreach (var n in _tree.Open) Walk(n);
            return list;
        }
    }

    public string ServerUrl => session.ServerUrl ?? "";
    public DocketService? Service => session.Service;

    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        if (query.TryGetValue("key", out var k)) _key = Uri.UnescapeDataString(k.ToString() ?? _key);
        ViewMode = Prefs.ViewFor(_key);
    }

    [RelayCommand]
    public async Task RefreshAsync()
    {
        if (session.Service is not { } svc) return;
        IsBusy = true;
        Error = null;
        try
        {
            var sort = Prefs.SortFor(_key);
            var serverSort = Prefs.SortByPriority(_key) ? SortMode.Custom : sort;
            _project = null;
            _style = new();
            if (Search is { } q)
            {
                SetHeader($"“{q}”", "Search", IconChars.Search);
                var open = await svc.ListAsync(TaskView.Open, search: q, limit: 200);
                var done = (await svc.ListAsync(TaskView.Done, search: q, limit: 200)).Where(t => t.Task.Done).ToList();
                _tree = await svc.AsTreeAsync(new ListContent(open, done));
            }
            else if (_key == "view:upcoming")
            {
                var days = Prefs.UpcomingDays;
                SetHeader("Upcoming", $"Next {days} days", IconChars.CalendarNext);
                var list = await svc.UpcomingAsync(days);
                _tree = await svc.AsTreeAsync(new ListContent(list, []));
            }
            else if (_key == "view:open")
            {
                SetHeader("Open tasks", "Every unfinished task, in every list", IconChars.TaskList);
                var open = await svc.ListAsync(TaskView.Open, limit: 500);
                var sorted = DocketService.Sort(open.Select(o => o.Task), serverSort).Select(t => open.First(o => o.Id == t.Id)).ToList();
                _tree = await svc.AsTreeAsync(new ListContent(sorted, []));
            }
            else if (Smart is { } s)
            {
                SetHeader(Titles.Of(s), s == SmartList.MyDay ? DateTime.Now.ToString("dddd, d MMMM", CultureInfo.CurrentCulture) : "", s switch
                {
                    SmartList.MyDay => IconChars.Sun, SmartList.Important => IconChars.Star, SmartList.Planned => IconChars.Calendar,
                    SmartList.All => IconChars.All, SmartList.Completed => IconChars.Done, _ => IconChars.Home,
                });
                if (s == SmartList.Tasks)
                {
                    var inbox = await svc.DefaultProjectAsync(Preferences.Default.Get("default.project", 0L));
                    _tree = await svc.ProjectTreeAsync(inbox.Id, serverSort);
                }
                else _tree = await svc.AsTreeAsync(await svc.SmartAsync(s, serverSort, Preferences.Default.Get("default.project", 0L)));
            }
            else
            {
                _project = (await svc.ProjectsAsync()).FirstOrDefault(p => p.Id == ProjectId);
                _style = ListStyle.Parse(_project?.Description);
                SetHeader(_project?.Title ?? "List", "", Icons.Display(_style.Icon, IconChars.List), Icons.IsEmoji(_style.Icon));
                Tint = _project is { HexColor.Length: 6 } p ? TaskRow.ColorOf(p.HexColor) : null;
                _tree = await svc.ProjectTreeAsync(ProjectId, serverSort);
            }
            if (Prefs.SortByPriority(_key)) _tree = new TreeContent(ByPriority(_tree.Open), _tree.Completed);
            if (ViewMode == "board" && _project?.KanbanView is null) ViewMode = "list";
            Board = ViewMode == "board" && _project is not null ? await svc.BoardOfAsync(_project.Id) : null;
            OnPropertyChanged(nameof(Views));

            IsMyDay = Smart == SmartList.MyDay;
            CanAdd = Smart != SmartList.Completed && Search is null;
            Layout();
            await ApplyBackgroundAsync(svc);
            DataChanged?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex) when (U.IsExpected(ex)) { Error = U.Friendly(ex); }
        finally { IsBusy = false; }
    }

    private static List<TaskNode> ByPriority(IEnumerable<TaskNode> nodes) =>
        nodes.OrderByDescending(n => n.Info.Task.Priority).ThenBy(n => n.Info.Task.Due ?? DateTimeOffset.MaxValue).ToList();

    private void SetHeader(string title, string subtitle, string glyph, bool emoji = false)
    {
        Title = title; Subtitle = subtitle; Glyph = glyph; GlyphIsEmoji = emoji; Tint = null;
    }

    /// <summary>Lays the tree out as rows, honouring which parents are expanded.</summary>
    private void Layout()
    {
        var showList = _project is null && Smart != SmartList.Tasks;
        var reserve = HasAnyChildren(_tree.Open) || HasAnyChildren(_tree.Completed);
        TaskRow Make(Core.TaskRow r) => new(r.Node.Info.Task, r.Node.Info.Project, showList, r.Node, r.Expanded) { ReserveChevron = reserve };
        Rows.Clear();
        var expanded = Expanded;
        Func<TaskNode, string>? group = Smart == SmartList.Planned ? n => DocketService.PlannedGroup(n.Info.Task, DateTimeOffset.Now)
            : IsUpcoming ? n => UpcomingGroup(n.Info.Task) : null;
        if (group is not null)
        {
            foreach (var g in _tree.Open.GroupBy(group))
            {
                Rows.Add(new GroupHeaderRow(g.Key, g.Count()) { OnImage = HasBackground });
                foreach (var r in DocketService.Flatten(g, expanded)) Rows.Add(Make(r));
            }
        }
        else foreach (var r in DocketService.Flatten(_tree.Open, expanded)) Rows.Add(Make(r));

        Done.Clear();
        foreach (var r in DocketService.Flatten(_tree.Completed, expanded)) Done.Add(Make(r));
        ShowDone = Done.Count > 0 && !Prefs.HideCompleted(_key) && Smart != SmartList.Completed;
        OnPropertyChanged(nameof(DoneHeader));
        IsEmpty = Rows.Count == 0 && !ShowDone;
        EmptyText = Search is not null ? "No tasks match." : IsUpcoming ? "Nothing due in this period." : Smart switch
        {
            SmartList.MyDay => "Focus on your day. Add a task, or tap the bulb for suggestions.",
            SmartList.Important => "Star a task to see it here.",
            SmartList.Planned => "Tasks with due dates show up here.",
            SmartList.Completed => "Completed tasks show up here.",
            _ => "No tasks yet. Type below to add one, or “/” for commands.",
        };
    }

    private static bool HasAnyChildren(IEnumerable<TaskNode> nodes) => nodes.Any(n => n.HasChildren);

    private static string UpcomingGroup(VTask t)
    {
        if (t.Due is not { } due) return "No date";
        if (t.IsOverdue(DateTimeOffset.Now)) return "Overdue";
        var d = due.ToLocalTime().Date;
        return d == DateTime.Today ? "Today" : d == DateTime.Today.AddDays(1) ? "Tomorrow" : d.ToString("dddd, d MMMM", CultureInfo.CurrentCulture);
    }

    private async Task ApplyBackgroundAsync(DocketService svc)
    {
        BackgroundImage = null;
        BackgroundBrush = Icons.BackgroundBrush(_style);
        if (_style.Background == "image" && _project is not null)
        {
            var stamp = _project.Description;
            if (!BackgroundCache.TryGetValue(_project.Id, out var cached) || cached.Stamp != stamp)
            {
                try
                {
                    var bytes = await svc.Client.GetBackgroundAsync(_project.Id);
                    if (bytes is not null) BackgroundCache[_project.Id] = cached = (stamp, bytes);
                }
                catch (Exception ex) when (U.IsExpected(ex)) { }
            }
            if (cached.Bytes is { Length: > 0 } b) BackgroundImage = ImageSource.FromStream(() => new MemoryStream(b));
        }
        HasBackground = BackgroundBrush is not null || BackgroundImage is not null;
        if (HasBackground) foreach (var i in Rows.OfType<GroupHeaderRow>().ToList()) Rows[Rows.IndexOf(i)] = i with { OnImage = true };
    }

    /// <summary>Forgets a list's cached background (after a new picture is uploaded).</summary>
    public static void ForgetBackground(long projectId) => BackgroundCache.Remove(projectId);

    internal async Task RunAsync(Func<DocketService, Task> action, bool refresh = true)
    {
        if (session.Service is not { } svc) return;
        try { await action(svc); if (refresh) await RefreshAsync(); }
        catch (Exception ex) when (U.IsExpected(ex)) { Error = U.Friendly(ex); }
    }

    [RelayCommand]
    private void ToggleExpand(TaskRow row)
    {
        if (!row.HasChildren) return;
        if (!Expanded.Remove(row.Key)) Expanded.Add(row.Key);
        Layout();
    }

    [RelayCommand]
    private Task ToggleDoneAsync(TaskRow row) => RunAsync(async svc =>
    {
        HapticFeedback.Default.Perform(HapticFeedbackType.Click);
        var r = await svc.SetDoneAsync(row.Id, !row.IsDone);
        row.Replace(r.Task);
        await Task.Delay(250);
    });

    [RelayCommand]
    private Task ToggleStarAsync(TaskRow row) => RunAsync(async svc => row.Replace((await svc.SetImportantAsync(row.Id, !row.IsImportant)).Task), refresh: Smart == SmartList.Important);

    [RelayCommand]
    private Task ToggleMyDayAsync(TaskRow row) => RunAsync(async svc => row.Replace((await svc.SetMyDayAsync(row.Id, !row.InMyDay)).Task), refresh: Smart == SmartList.MyDay);

    [RelayCommand]
    private async Task DeleteAsync(TaskRow row) => await DeleteTaskAsync(row.Id, row.Title);

    internal async Task DeleteTaskAsync(long id, string title)
    {
        if (Prefs.ConfirmDelete && !await Shell.Current.DisplayAlertAsync("Delete task?", $"“{title}” will be permanently deleted.", "Delete", "Cancel")) return;
        await RunAsync(svc => svc.DeleteTaskAsync(id));
    }

    [RelayCommand]
    private Task OpenAsync(TaskRow row) => OpenTaskAsync(row.Id);

    internal static Task OpenTaskAsync(long id) => Shell.Current.GoToAsync($"task?id={id}");

    [RelayCommand]
    private void ToggleDoneSection() => DoneExpanded = !DoneExpanded;

    [RelayCommand]
    private Task BackAsync() => Shell.Current.GoToAsync("..");

    public async Task SetViewAsync(string view)
    {
        if (ViewMode == view) return;
        ViewMode = view;
        Prefs.SetView(_key, view);
        await RefreshAsync();
    }

    public async Task SetUpcomingDaysAsync(int days)
    {
        Prefs.UpcomingDays = days;
        await RefreshAsync();
    }

    [RelayCommand]
    private async Task MoreAsync()
    {
        var sort = Prefs.SortFor(_key);
        var byPrio = Prefs.SortByPriority(_key);
        var choices = new List<Choice>
        {
            new("My order", IconChars.Sort, Selected: !byPrio && sort == SortMode.Custom),
            new("Importance", IconChars.Star, "Starred first, then priority", !byPrio && sort == SortMode.Importance),
            new("Priority", IconChars.Flag, "Do now, urgent, high...", byPrio),
            new("Due date", IconChars.Calendar, Selected: !byPrio && sort == SortMode.DueDate),
            new("A to Z", IconChars.TaskList, Selected: !byPrio && sort == SortMode.Alphabetical),
            new("Newest first", IconChars.Clock, Selected: !byPrio && sort == SortMode.CreationDate),
            new(Prefs.HideCompleted(_key) ? "Show completed tasks" : "Hide completed tasks", IconChars.Done, Section: "Show"),
        };
        if (_project is not null)
        {
            choices.Add(new("Icon and background", IconChars.Paint, Section: "List"));
            choices.Add(new("Share list", IconChars.Share, "People, teams and public links"));
            choices.Add(new("Rename list", IconChars.Rename));
            choices.Add(new("Delete list", IconChars.Delete, Danger: true));
        }
        var pick = await ChoiceSheet.PickAsync(Title, choices, "Sort by");
        switch (pick)
        {
            case < 0: return;
            case <= 5:
                Prefs.SetSortByPriority(_key, pick == 2);
                if (pick != 2) Prefs.SetSort(_key, pick switch { 1 => SortMode.Importance, 3 => SortMode.DueDate, 4 => SortMode.Alphabetical, 5 => SortMode.CreationDate, _ => SortMode.Custom });
                break;
            case 6:
                Prefs.SetHideCompleted(_key, !Prefs.HideCompleted(_key));
                break;
            case 7:
                await Shell.Current.GoToAsync($"liststyle?id={_project!.Id}");
                return;
            case 8:
                await Shell.Current.GoToAsync($"share?id={_project!.Id}");
                return;
            case 9:
                var name = await Shell.Current.DisplayPromptAsync("Rename list", "List name", "Rename", "Cancel", initialValue: _project!.Title);
                if (!string.IsNullOrWhiteSpace(name)) await RunAsync(svc => svc.RenameListAsync(_project.Id, title: name), refresh: false);
                break;
            case 10:
                if (await Shell.Current.DisplayAlertAsync("Delete list?", $"“{_project!.Title}” and all its tasks will be permanently deleted.", "Delete", "Cancel"))
                {
                    await RunAsync(svc => svc.DeleteListAsync(_project.Id), refresh: false);
                    await Shell.Current.GoToAsync("..");
                    return;
                }
                break;
        }
        await RefreshAsync();
    }

    [RelayCommand]
    private async Task SuggestionsAsync()
    {
        if (session.Service is not { } svc) return;
        try
        {
            var items = await svc.MyDaySuggestionsAsync();
            if (items.Count == 0) { await Shell.Current.DisplayAlertAsync("Suggestions", "No suggestions right now.", "OK"); return; }
            var i = await ChoiceSheet.PickAsync("Add to My Day", items.Select(s => new Choice(s.Task.Task.Title, IconChars.Sun, $"{s.Group} · {s.Task.Project}")).ToList());
            if (i >= 0) await RunAsync(s => s.SetMyDayAsync(items[i].Task.Id, true));
        }
        catch (Exception ex) when (U.IsExpected(ex)) { Error = U.Friendly(ex); }
    }
}
