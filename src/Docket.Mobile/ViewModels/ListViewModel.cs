using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Docket.Core;
using Docket.Mobile.Services;

namespace Docket.Mobile.ViewModels;

/// <summary>One list: a smart list, one of the user's lists, or search results ("search:words").</summary>
public sealed partial class ListViewModel(Session session) : ObservableObject, IQueryAttributable
{
    private string _key = "smart:MyDay";
    private VProject? _project;

    public ObservableCollection<object> Rows { get; } = [];
    public ObservableCollection<TaskRow> Done { get; } = [];

    [ObservableProperty] public partial string Title { get; set; } = "";
    [ObservableProperty] public partial string Subtitle { get; set; } = "";
    [ObservableProperty] public partial string Glyph { get; set; } = "";
    [ObservableProperty] public partial Color? Tint { get; set; }
    [ObservableProperty] public partial bool IsBusy { get; set; }
    [ObservableProperty] public partial string? Error { get; set; }
    [ObservableProperty] public partial string NewTask { get; set; } = "";
    [ObservableProperty] public partial bool ShowDone { get; set; }
    [ObservableProperty] public partial bool DoneExpanded { get; set; }
    [ObservableProperty] public partial bool IsEmpty { get; set; }
    [ObservableProperty] public partial string EmptyText { get; set; } = "";
    [ObservableProperty] public partial bool CanAdd { get; set; } = true;
    [ObservableProperty] public partial bool IsMyDay { get; set; }

    public string DoneHeader => $"Completed  {Done.Count}";
    private SmartList? Smart => _key.StartsWith("smart:", StringComparison.Ordinal) ? Enum.Parse<SmartList>(_key[6..]) : null;
    private string? Search => _key.StartsWith("search:", StringComparison.Ordinal) ? _key[7..] : null;
    private long ProjectId => _key.StartsWith("list:", StringComparison.Ordinal) ? long.Parse(_key[5..], CultureInfo.InvariantCulture) : 0;

    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        if (query.TryGetValue("key", out var k)) _key = Uri.UnescapeDataString(k.ToString() ?? _key);
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
            ListContent content;
            if (Search is { } q)
            {
                Title = $"“{q}”"; Subtitle = "Search"; Glyph = ""; Tint = null;
                var open = await svc.ListAsync(TaskView.Open, search: q, limit: 200);
                var done = (await svc.ListAsync(TaskView.Done, search: q, limit: 200)).Where(t => t.Task.Done).ToList();
                content = new ListContent(open, done);
            }
            else if (Smart is { } s)
            {
                Title = Titles.Of(s); Tint = null;
                Glyph = s switch { SmartList.MyDay => "", SmartList.Important => "", SmartList.Planned => "", SmartList.All => "", SmartList.Completed => "", _ => "" };
                Subtitle = s == SmartList.MyDay ? DateTime.Now.ToString("dddd, d MMMM", CultureInfo.CurrentCulture) : "";
                content = await svc.SmartAsync(s, sort, Preferences.Default.Get("default.project", 0L));
            }
            else
            {
                _project = (await svc.ProjectsAsync()).FirstOrDefault(p => p.Id == ProjectId);
                Title = _project?.Title ?? "List"; Subtitle = ""; Glyph = "";
                Tint = _project is { HexColor.Length: 6 } p ? TaskRow.ColorOf(p.HexColor) : null;
                content = await svc.ProjectListAsync(ProjectId, sort);
            }

            IsMyDay = Smart == SmartList.MyDay;
            CanAdd = Smart != SmartList.Completed && Search is null;
            var showList = Smart is not null && Smart != SmartList.Tasks || Search is not null;
            Rows.Clear();
            if (Smart == SmartList.Planned)
            {
                var now = DateTimeOffset.Now;
                foreach (var g in content.Open.GroupBy(t => DocketService.PlannedGroup(t.Task, now)))
                {
                    Rows.Add(new GroupHeaderRow(g.Key, g.Count()));
                    foreach (var t in g) Rows.Add(new TaskRow(t.Task, t.Project, showList));
                }
            }
            else foreach (var t in content.Open) Rows.Add(new TaskRow(t.Task, t.Project, showList));

            Done.Clear();
            foreach (var t in content.Completed) Done.Add(new TaskRow(t.Task, t.Project, showList));
            ShowDone = Done.Count > 0 && !Prefs.HideCompleted(_key) && Smart != SmartList.Completed;
            OnPropertyChanged(nameof(DoneHeader));
            IsEmpty = Rows.Count == 0 && !ShowDone;
            EmptyText = Search is not null ? "No tasks match." : Smart switch
            {
                SmartList.MyDay => "Focus on your day. Add a task, or tap the bulb for suggestions.",
                SmartList.Important => "Star a task to see it here.",
                SmartList.Planned => "Tasks with due dates show up here.",
                SmartList.Completed => "Completed tasks show up here.",
                _ => "No tasks yet.",
            };
        }
        catch (Exception ex) when (ex is HttpRequestException or VikunjaException or DocketException or TaskCanceledException)
        {
            Error = ex is HttpRequestException ? $"Can't reach {session.ServerUrl}." : ex.Message;
        }
        finally { IsBusy = false; }
    }

    private async Task RunAsync(Func<DocketService, Task> action, bool refresh = true)
    {
        if (session.Service is not { } svc) return;
        try { await action(svc); if (refresh) await RefreshAsync(); }
        catch (Exception ex) when (ex is HttpRequestException or VikunjaException or DocketException or TaskCanceledException) { Error = ex.Message; }
    }

    [RelayCommand]
    private Task AddAsync()
    {
        var text = NewTask.Trim();
        if (text.Length == 0) return Task.CompletedTask;
        NewTask = "";
        return RunAsync(async svc =>
        {
            var project = ProjectId > 0 ? ProjectId.ToString(CultureInfo.InvariantCulture) : null;
            var due = Smart == SmartList.Planned && QuickAdd.Parse(text, DateTimeOffset.Now).Due is null ? "today" : null;
            var t = await svc.AddAsync(text, project, due, defaultProjectId: Preferences.Default.Get("default.project", 0L));
            if (Smart == SmartList.MyDay) await svc.SetMyDayAsync(t.Id, true);
            if (Smart == SmartList.Important) await svc.SetImportantAsync(t.Id, true);
            await Reminders.RescheduleAsync(svc);
        });
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
    private async Task DeleteAsync(TaskRow row)
    {
        if (Prefs.ConfirmDelete && !await Shell.Current.DisplayAlertAsync("Delete task?", $"\"{row.Title}\" will be permanently deleted.", "Delete", "Cancel")) return;
        await RunAsync(svc => svc.DeleteTaskAsync(row.Id));
    }

    [RelayCommand]
    private Task OpenAsync(TaskRow row) => Shell.Current.GoToAsync($"task?id={row.Id}");

    [RelayCommand]
    private void ToggleDoneSection() => DoneExpanded = !DoneExpanded;

    [RelayCommand]
    private async Task MoreAsync()
    {
        var options = new List<string> { "Sort by: my order", "Sort by: importance", "Sort by: due date", "Sort by: A to Z", "Sort by: newest" };
        options.Add(Prefs.HideCompleted(_key) ? "Show completed tasks" : "Hide completed tasks");
        if (_project is not null) options.AddRange(["Rename list", "Delete list"]);
        var pick = await Shell.Current.DisplayActionSheetAsync(Title, "Cancel", null, [.. options]);
        switch (pick)
        {
            case null or "Cancel": return;
            case var p when p.StartsWith("Sort by", StringComparison.Ordinal):
                Prefs.SetSort(_key, options.IndexOf(p) switch { 1 => SortMode.Importance, 2 => SortMode.DueDate, 3 => SortMode.Alphabetical, 4 => SortMode.CreationDate, _ => SortMode.Custom });
                break;
            case "Show completed tasks" or "Hide completed tasks":
                Prefs.SetHideCompleted(_key, !Prefs.HideCompleted(_key));
                break;
            case "Rename list":
                var name = await Shell.Current.DisplayPromptAsync("Rename list", "List name", "Rename", "Cancel", initialValue: _project!.Title);
                if (!string.IsNullOrWhiteSpace(name)) await RunAsync(svc => svc.RenameListAsync(_project.Id, title: name), refresh: false);
                break;
            case "Delete list":
                if (await Shell.Current.DisplayAlertAsync("Delete list?", $"\"{_project!.Title}\" and all its tasks will be permanently deleted.", "Delete", "Cancel"))
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
            var labels = items.Select(s => $"{s.Group}: {s.Task.Task.Title}").ToArray();
            var pick = await Shell.Current.DisplayActionSheetAsync("Add to My Day", "Close", null, labels);
            var i = Array.IndexOf(labels, pick);
            if (i >= 0) await RunAsync(s => s.SetMyDayAsync(items[i].Task.Id, true));
        }
        catch (Exception ex) when (ex is HttpRequestException or VikunjaException or DocketException) { Error = ex.Message; }
    }
}
