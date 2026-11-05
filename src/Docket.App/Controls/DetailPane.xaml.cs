using System.Globalization;
using Docket.App.Services;
using Docket.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.System;

namespace Docket.App.Controls;

public enum TaskChange { Updated, Deleted }

/// <summary>
/// The right-hand task details: title, subtasks (a nested tree), parents, My Day, reminder, due, repeat, start and end,
/// priority, assignees, categories, files, list, notes, and the task's history and comments.
/// </summary>
public sealed partial class DetailPane : UserControl
{
    private VTask? _task;
    private TaskNotes _notes = new("", []);
    private bool _loading;
    private readonly HashSet<long> _collapsed = [];

    public DetailPane() => InitializeComponent();

    public long? TaskId => _task?.Id;
    public event EventHandler? Closed;
    public event EventHandler<TaskChange>? Changed;
    /// <summary>Asks the host to show another task here (a subtask or a parent).</summary>
    public event EventHandler<long>? OpenTask;

    private static DocketService? Svc => App.Service;
    private static MainWindow? Win => App.Window;
    private static readonly CultureInfo Culture = CultureInfo.CurrentCulture;

    public void Clear() => _task = null;

    public async Task LoadAsync(long id)
    {
        if (Svc is not { } svc || Win is null) return;
        Busy.Visibility = Visibility.Visible;
        await Win.RunAsync("Couldn't open the task", async () =>
        {
            var t = await svc.GetAsync(id);
            t.Attachments = await svc.Client.GetAttachmentsAsync(id);
            var tree = await SubtreeAsync(svc, t, 1, [id]);
            var comments = await svc.Client.GetCommentsAsync(id);
            if (_task?.Id != id) AssignNote.IsOpen = false;
            _task = t;
            Render(tree, comments);
        });
        Busy.Visibility = Visibility.Collapsed;
    }

    /// <summary>The task's subtasks with their own subtasks, as (task, depth) rows. Loops are cut.</summary>
    private static async Task<List<(VTask Task, int Depth)>> SubtreeAsync(DocketService svc, VTask parent, int depth, HashSet<long> path)
    {
        var rows = new List<(VTask, int)>();
        if (depth > 6) return rows;
        var kids = parent.Subtasks.Where(s => !path.Contains(s.Id)).OrderBy(s => s.Done).ThenBy(s => s.Position).ThenBy(s => s.Id).ToList();
        var full = await Task.WhenAll(kids.Select(async k => (await svc.Client.GetTaskAsync(k.Id)) ?? k));
        foreach (var k in full)
        {
            rows.Add((k, depth));
            rows.AddRange(await SubtreeAsync(svc, k, depth + 1, [.. path, k.Id]));
        }
        return rows;
    }

    private void Render(List<(VTask Task, int Depth)> tree, List<VComment> comments)
    {
        if (_task is not { } t) return;
        _loading = true;
        _notes = TaskNotes.Parse(t.Description);
        DoneBox.IsChecked = t.Done;
        TitleBox.Text = t.Title;
        StarIcon.Glyph = t.IsFavorite ? "" : "";
        AutomationProperties.SetName(StarButton, t.IsFavorite ? "Remove importance" : "Mark as important");
        ToolTipService.SetToolTip(StarButton, t.IsFavorite ? "Remove importance" : "Mark as important");
        Breadcrumb.Text = t.Parents.Count > 0 ? "Subtask of " + string.Join(", ", t.Parents.Select(p => p.Title)) : "";
        Breadcrumb.Visibility = t.Parents.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

        RenderSubtasks(tree);
        RenderParents(t);
        RenderSteps();

        var inMyDay = DocketService.InMyDay(t);
        MyDayText.Text = inMyDay ? "Added to My Day" : "Add to My Day";
        MyDayText.Foreground = inMyDay ? Ui.Brush("AccentTextFillColorPrimaryBrush") : Ui.Brush("TextFillColorPrimaryBrush");
        MyDayIcon.Foreground = MyDayText.Foreground;
        ToolTipService.SetToolTip(MyDayButton, inMyDay ? "Remove from My Day" : "Add to My Day");

        var next = t.NextReminder(DateTimeOffset.Now);
        RemindText.Text = next is { } r ? $"Remind me at {r.ToLocalTime().ToString("t", Culture)}" : "Remind me";
        RemindSub.Text = next is { } r2 ? r2.ToLocalTime().ToString("dddd, d MMMM", Culture) : "";
        RemindSub.Visibility = next is null ? Visibility.Collapsed : Visibility.Visible;

        DueText.Text = t.Due is { } d ? $"Due {d.ToLocalTime().ToString("ddd, d MMMM", Culture)}" : "Add due date";
        if (t.IsOverdue(DateTimeOffset.Now)) DueText.Foreground = Ui.Brush("DocketOverdueBrush");
        else DueText.ClearValue(TextBlock.ForegroundProperty);
        RepeatText.Text = RepeatRule.From(t) is { } rule ? rule.Describe() : "Repeat";
        StartText.Text = VDates.OrNull(t.StartDate) is { } s ? $"Starts {s.ToLocalTime().ToString("ddd, d MMMM", Culture)}" : "Add start date";
        EndText.Text = VDates.OrNull(t.EndDate) is { } e ? $"Ends {e.ToLocalTime().ToString("ddd, d MMMM", Culture)}" : "Add end date";

        PriorityText.Text = t.Priority > 0 ? "Priority" : "Set priority";
        PriorityBadge.Content = t.Priority > 0 ? Ui.PriorityBadge(t.Priority) : null;
        PriorityIcon.Foreground = t.Priority > 0 ? Ui.Hex(Ui.PriorityHex(t.Priority)) : Ui.Brush("TextFillColorPrimaryBrush");

        AssigneesPanel.Children.Clear();
        foreach (var u in t.Assignees ?? [])
        {
            var row = new Grid { ColumnSpacing = 10, Padding = new Thickness(4, 4, 0, 4) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var name = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            name.Children.Add(Ui.Text(DocketService.DisplayName(u)));
            name.Children.Add(Ui.Text("@" + u.Username, "CaptionTextBlockStyle", 0.6));
            var user = u;
            var remove = Ui.IconButton("", $"Unassign {DocketService.DisplayName(u)}", async (_, _) => await SaveAsync("assignees", (s, id) => s.UnassignAsync(id, user.Id)), 12);
            Grid.SetColumn(name, 1);
            Grid.SetColumn(remove, 2);
            row.Children.Add(Ui.Avatar(u, 30));
            row.Children.Add(name);
            row.Children.Add(remove);
            AssigneesPanel.Children.Add(row);
        }
        if (t.Assignees is not { Count: > 0 }) AssigneesPanel.Children.Add(new TextBlock { Text = "Nobody yet", Style = Ui.TextStyle("CaptionTextBlockStyle"), Opacity = 0.6, Margin = new Thickness(4, 0, 0, 4) });

        var cats = (t.Labels ?? []).Where(DocketService.IsCategory).Select(l => l.Title).ToList();
        CategoryText.Text = cats.Count > 0 ? string.Join(", ", cats) : "Pick a category";

        FilesPanel.Children.Clear();
        foreach (var a in t.Attachments ?? [])
        {
            var link = new HyperlinkButton { Content = $"{a.File.Name}  ({FormatSize(a.File.Size)})", Padding = new Thickness(0, 4, 0, 4) };
            var att = a;
            link.Click += async (_, _) => await OpenFileAsync(att);
            FilesPanel.Children.Add(link);
        }

        var projects = Win?.Projects ?? [];
        ListBox.ItemsSource = projects;
        ListBox.SelectedItem = projects.FirstOrDefault(p => p.Id == t.ProjectId);
        NotesBox.Text = _notes.Notes;

        HistoryPanel.Children.Clear();
        foreach (var (what, when) in DocketService.History(t))
            HistoryPanel.Children.Add(HistoryLine(what.StartsWith("Created", StringComparison.Ordinal) ? "" : what == "Completed" ? "" : "", what, when));
        CommentsPanel.Children.Clear();
        CommentsPanel.Children.Add(HistoryLine("", comments.Count switch { 0 => "No comments", 1 => "1 comment", var n => $"{n} comments" }, null));
        foreach (var c in comments.TakeLast(5))
        {
            var g = new Grid { ColumnSpacing = 10 };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var body = new StackPanel();
            body.Children.Add(Ui.Text($"{(c.Author is { } au ? DocketService.DisplayName(au) : "Someone")} · {Ui.FriendlyDate(c.Created)}", "CaptionTextBlockStyle", 0.65));
            body.Children.Add(Ui.Text(TaskFormat.StripHtml(c.Comment), "BodyTextBlockStyle", 1, TextWrapping.Wrap));
            Grid.SetColumn(body, 1);
            g.Children.Add(c.Author is { } a2 ? Ui.Avatar(a2, 24) : new Border { Width = 24 });
            g.Children.Add(body);
            CommentsPanel.Children.Add(g);
        }

        CreatedText.Text = t.Done && VDates.OrNull(t.DoneAt) is { } da
            ? $"Completed {da.ToLocalTime().ToString("ddd, d MMMM", Culture)}"
            : $"Created {t.Created.ToLocalTime().ToString("ddd, d MMMM", Culture)}";
        _loading = false;
    }

    private static FrameworkElement HistoryLine(string glyph, string what, DateTimeOffset? when)
    {
        var p = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        p.Children.Add(Ui.Glyph(glyph, 12, Ui.Brush("TextFillColorSecondaryBrush")));
        p.Children.Add(Ui.Text(when is { } w ? $"{what} · {Ui.FriendlyDate(w)}" : what, "CaptionTextBlockStyle", 0.8));
        if (when is { } at) ToolTipService.SetToolTip(p, at.ToLocalTime().ToString("F", Culture));
        return p;
    }

    private static string FormatSize(long bytes) => bytes switch
    {
        < 1024 => $"{bytes} B", < 1024 * 1024 => $"{bytes / 1024.0:0.#} KB", _ => $"{bytes / 1048576.0:0.#} MB",
    };

    // ---------- saving ----------
    private async Task SaveAsync(string what, Func<DocketService, long, Task> action)
    {
        if (_task is null || Svc is not { } svc || Win is null) return;
        var id = _task.Id;
        Busy.Visibility = Visibility.Visible;
        await Win.RunAsync($"Couldn't change the {what}", async () =>
        {
            await action(svc, id);
            await LoadAsync(id);
            Changed?.Invoke(this, TaskChange.Updated);
        });
        Busy.Visibility = Visibility.Collapsed;
    }

    private async void Done_Click(object sender, RoutedEventArgs e) =>
        await SaveAsync("status", (s, id) => s.SetDoneAsync(id, DoneBox.IsChecked == true));

    private async void Star_Click(object sender, RoutedEventArgs e) =>
        await SaveAsync("importance", (s, id) => s.SetImportantAsync(id, !(_task?.IsFavorite ?? false)));

    private async void Title_LostFocus(object sender, RoutedEventArgs e)
    {
        if (_loading || _task is null) return;
        var title = TitleBox.Text.Trim();
        if (title.Length == 0) { TitleBox.Text = _task.Title; return; }
        if (title != _task.Title) await SaveAsync("title", (s, id) => s.UpdateAsync(id, title: title));
    }

    private void Title_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Enter) { e.Handled = true; NewSubtaskBox.Focus(FocusState.Programmatic); }
    }

    private async void Notes_LostFocus(object sender, RoutedEventArgs e)
    {
        if (_loading || _task is null || NotesBox.Text == _notes.Notes) return;
        var text = NotesBox.Text;
        await SaveAsync("note", (s, id) => s.SetNotesAndStepsAsync(id, n => n with { Notes = text }));
    }

    private async void MyDay_Click(object sender, RoutedEventArgs e) =>
        await SaveAsync("My Day", (s, id) => s.SetMyDayAsync(id, !DocketService.InMyDay(_task!)));

    private async void List_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || _task is null || ListBox.SelectedItem is not VProject p || p.Id == _task.ProjectId) return;
        await SaveAsync("list", (s, id) => s.UpdateAsync(id, project: p.Id.ToString(CultureInfo.InvariantCulture)));
    }

    // ---------- subtasks ----------
    private void RenderSubtasks(List<(VTask Task, int Depth)> tree)
    {
        SubtasksPanel.Children.Clear();
        var hideBelow = int.MaxValue;
        for (var i = 0; i < tree.Count; i++)
        {
            var (sub, depth) = tree[i];
            if (depth > hideBelow) continue;
            hideBelow = int.MaxValue;
            var hasKids = i + 1 < tree.Count && tree[i + 1].Depth > depth;
            var collapsed = _collapsed.Contains(sub.Id);
            if (hasKids && collapsed) hideBelow = depth;
            SubtasksPanel.Children.Add(SubtaskRow(sub, depth, hasKids, collapsed));
        }
        SubtasksLabel.Text = tree.Count == 0 ? "Subtasks" : $"Subtasks  ·  {tree.Count(r => r.Depth == 1 && r.Task.Done)} of {tree.Count(r => r.Depth == 1)} done";
    }

    private Grid SubtaskRow(VTask sub, int depth, bool hasKids, bool collapsed)
    {
        var row = new Grid { ColumnSpacing = 2, Padding = new Thickness((depth - 1) * 20, 0, 0, 0), MinHeight = 36 };
        foreach (var w in new[] { GridLength.Auto, GridLength.Auto, new GridLength(1, GridUnitType.Star), GridLength.Auto, GridLength.Auto })
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = w });
        var chevron = new Button
        {
            Content = Ui.Glyph(collapsed ? "" : "", 9), Width = 24, Height = 24, Padding = new Thickness(0),
            Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Transparent), BorderThickness = new Thickness(0),
            Opacity = hasKids ? 1 : 0, IsEnabled = hasKids, VerticalAlignment = VerticalAlignment.Center,
        };
        AutomationProperties.SetName(chevron, collapsed ? $"Expand {sub.Title}" : $"Collapse {sub.Title}");
        chevron.Click += async (_, _) =>
        {
            if (!_collapsed.Remove(sub.Id)) _collapsed.Add(sub.Id);
            if (_task is { } t) await LoadAsync(t.Id);
        };
        var box = new CheckBox { IsChecked = sub.Done, MinWidth = 0, VerticalAlignment = VerticalAlignment.Center, Padding = new Thickness(0) };
        AutomationProperties.SetName(box, $"{sub.Title} done");
        box.Click += async (_, _) => await SaveAsync("subtask", (s, _) => s.SetDoneAsync(sub.Id, box.IsChecked == true));
        var title = new TextBlock
        {
            Text = sub.Title, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 0, 0),
            Opacity = sub.Done ? 0.6 : 1, TextDecorations = sub.Done ? Windows.UI.Text.TextDecorations.Strikethrough : Windows.UI.Text.TextDecorations.None,
        };
        title.Tapped += (_, _) => OpenTask?.Invoke(this, sub.Id);
        var star = Ui.IconButton(sub.IsFavorite ? "" : "", sub.IsFavorite ? $"Remove importance from {sub.Title}" : $"Mark {sub.Title} as important",
            async (_, _) => await SaveAsync("subtask", (s, _) => s.SetImportantAsync(sub.Id, !sub.IsFavorite)), 13);
        ((FontIcon)star.Content).Foreground = Ui.Brush("AccentTextFillColorPrimaryBrush");
        var open = Ui.IconButton("", $"Open {sub.Title}", (_, _) => OpenTask?.Invoke(this, sub.Id), 12);
        var menu = new MenuFlyout();
        var unlink = new MenuFlyoutItem { Text = "Remove from this task", Icon = new FontIcon { Glyph = "" } };
        var parentId = depth == 1 ? _task?.Id : null;
        unlink.IsEnabled = parentId is not null;
        unlink.Click += async (_, _) => { if (parentId is { } pid) await SaveAsync("subtasks", (s, _) => s.UnlinkSubtaskAsync(pid, sub.Id)); };
        var openItem = new MenuFlyoutItem { Text = "Open", Icon = new FontIcon { Glyph = "" } };
        openItem.Click += (_, _) => OpenTask?.Invoke(this, sub.Id);
        menu.Items.Add(openItem);
        menu.Items.Add(unlink);
        row.ContextFlyout = menu;
        Grid.SetColumn(box, 1);
        Grid.SetColumn(title, 2);
        Grid.SetColumn(star, 3);
        Grid.SetColumn(open, 4);
        row.Children.Add(chevron);
        row.Children.Add(box);
        row.Children.Add(title);
        row.Children.Add(star);
        row.Children.Add(open);
        return row;
    }

    private async void NewSubtask_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != VirtualKey.Enter || NewSubtaskBox.Text.Trim().Length == 0) return;
        e.Handled = true;
        var text = NewSubtaskBox.Text.Trim();
        NewSubtaskBox.Text = "";
        await SaveAsync("subtasks", (s, id) => s.AddSubtaskAsync(id, text));
        NewSubtaskBox.Focus(FocusState.Programmatic);
    }

    // ---------- parents ----------
    private void RenderParents(VTask t)
    {
        ParentsPanel.Children.Clear();
        foreach (var p in t.Parents)
        {
            var row = new Grid { ColumnSpacing = 8, Padding = new Thickness(12, 2, 0, 2) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var link = new HyperlinkButton { Content = p.Title, Padding = new Thickness(2, 2, 2, 2) };
            var parent = p;
            link.Click += (_, _) => OpenTask?.Invoke(this, parent.Id);
            var remove = Ui.IconButton("", $"Remove from {p.Title}", async (_, _) => await SaveAsync("parents", (s, id) => s.UnlinkSubtaskAsync(parent.Id, id)), 11);
            Grid.SetColumn(link, 1);
            Grid.SetColumn(remove, 2);
            row.Children.Add(Ui.Glyph("", 12, Ui.Brush("TextFillColorSecondaryBrush")));
            row.Children.Add(link);
            row.Children.Add(remove);
            ParentsPanel.Children.Add(row);
        }
        if (t.Parents.Count == 0) ParentsPanel.Children.Add(new TextBlock { Text = "A top-level task", Style = Ui.TextStyle("CaptionTextBlockStyle"), Opacity = 0.6, Margin = new Thickness(12, 0, 0, 2) });
    }

    private async void AddParent_Click(object sender, RoutedEventArgs e)
    {
        if (_task is not { } t) return;
        var exclude = new HashSet<long>(t.Parents.Select(p => p.Id)) { t.Id };
        var pick = await Dialogs.PickTaskAsync(XamlRoot, $"Put \"{t.Title}\" under…", "Add parent", exclude);
        if (pick is null) return;
        await SaveAsync("parents", (s, id) => s.LinkSubtaskAsync(pick.Id, id));
    }

    // ---------- old checklist steps ----------
    private void RenderSteps()
    {
        StepsPanel.Children.Clear();
        StepsSection.Visibility = _notes.Steps.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        for (var i = 0; i < _notes.Steps.Count; i++)
        {
            var index = i;
            var step = _notes.Steps[i];
            var box = new CheckBox { IsChecked = step.Done, Content = step.Title, Opacity = step.Done ? 0.6 : 1, Margin = new Thickness(8, 0, 0, 0) };
            AutomationProperties.SetName(box, $"Step {index + 1}: {step.Title}");
            box.Click += async (_, _) => await SaveAsync("step", (s, id) => s.SetStepDoneAsync(id, index, box.IsChecked == true));
            StepsPanel.Children.Add(box);
        }
    }

    private async void ConvertSteps_Click(object sender, RoutedEventArgs e)
    {
        var n = _notes.Steps.Count;
        await SaveAsync("checklist", (s, id) => s.ConvertStepsToSubtasksAsync(id));
        Win?.SetStatus($"Turned {n} {(n == 1 ? "step" : "steps")} into subtasks");
    }

    // ---------- priority ----------
    private void PriorityMenu_Opening(object sender, object e)
    {
        PriorityMenu.Items.Clear();
        for (var p = 0; p <= 5; p++)
        {
            var level = p;
            var item = new RadioMenuFlyoutItem { Text = DocketService.PriorityName(p), GroupName = "prio", IsChecked = _task?.Priority == p };
            item.Icon = new FontIcon { Glyph = p == 0 ? "" : "", Foreground = p == 0 ? null : Ui.Hex(Ui.PriorityHex(p)) };
            item.Click += async (_, _) => await SaveAsync("priority", (s, id) => s.SetPriorityAsync(id, level));
            PriorityMenu.Items.Add(item);
        }
    }

    // ---------- people ----------
    private async void AssignFlyout_Opening(object sender, object e)
    {
        if (Svc is not { } svc || Win is null || _task is null) return;
        PeoplePickPanel.Children.Clear();
        PeoplePickPanel.Children.Add(new ProgressRing { IsActive = true, Width = 20, Height = 20 });
        await Win.RunAsync("Couldn't load people", async () =>
        {
            var people = await svc.PeopleAsync();
            PeoplePickPanel.Children.Clear();
            var assigned = (_task.Assignees ?? []).Select(a => a.Id).ToHashSet();
            foreach (var u in people)
            {
                var content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
                content.Children.Add(Ui.Avatar(u, 26));
                var names = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
                names.Children.Add(Ui.Text(DocketService.DisplayName(u)));
                names.Children.Add(Ui.Text("@" + u.Username, "CaptionTextBlockStyle", 0.6));
                content.Children.Add(names);
                var box = new CheckBox { Content = content, IsChecked = assigned.Contains(u.Id) };
                AutomationProperties.SetName(box, DocketService.DisplayName(u));
                var person = u;
                box.Click += async (_, _) =>
                {
                    if (box.IsChecked == true) await AssignAsync(person);
                    else await SaveAsync("assignees", (s, id) => s.UnassignAsync(id, person.Id));
                };
                PeoplePickPanel.Children.Add(box);
            }
        });
    }

    private async Task AssignAsync(VUser person)
    {
        if (_task is not { } t || Svc is not { } svc) return;
        var me = await svc.MeAsync();
        var willShare = false;
        if (person.Id != me.Id)
        {
            try { willShare = (await svc.Client.GetProjectPeopleAsync(t.ProjectId)).All(u => u.Id != person.Id); }
            catch (VikunjaException) { }
        }
        await SaveAsync("assignees", (s, id) => s.AssignAsync(id, person));
        if (willShare)
        {
            var list = (Win?.Projects ?? []).FirstOrDefault(p => p.Id == t.ProjectId)?.Title ?? "this list";
            AssignNote.Title = "List shared";
            AssignNote.Message = $"{DocketService.DisplayName(person)} couldn't see \"{list}\", so it's now shared with them (can edit).";
            AssignNote.IsOpen = true;
        }
    }

    private void GoPeople_Click(object sender, RoutedEventArgs e)
    {
        AssignFlyout.Hide();
        Win?.ShowPeople();
    }

    // ---------- reminder, due, repeat, start, end ----------
    private static MenuFlyoutItem Item(string text, string glyph, string? sub, Func<Task> action)
    {
        var i = new MenuFlyoutItem { Text = sub is null ? text : $"{text}    {sub}", Icon = new FontIcon { Glyph = glyph } };
        i.Click += async (_, _) => await action();
        return i;
    }

    private void RemindMenu_Opening(object sender, object e)
    {
        var now = DateTimeOffset.Now;
        var later = RoundUp(now.AddHours(3));
        var tomorrow = At(now.Date.AddDays(1), 9);
        var toMonday = ((int)DayOfWeek.Monday - (int)now.DayOfWeek + 7) % 7;
        var nextWeek = At(now.Date.AddDays(toMonday == 0 ? 7 : toMonday), 9);
        RemindMenu.Items.Clear();
        if (later.Date == now.Date) RemindMenu.Items.Add(Item("Later today", "", later.ToString("t", Culture), () => SetReminder(later)));
        RemindMenu.Items.Add(Item("Tomorrow", "", tomorrow.ToString("ddd t", Culture), () => SetReminder(tomorrow)));
        RemindMenu.Items.Add(Item("Next week", "", nextWeek.ToString("ddd t", Culture), () => SetReminder(nextWeek)));
        RemindMenu.Items.Add(new MenuFlyoutSeparator());
        RemindMenu.Items.Add(Item("Pick a date & time", "", null, async () =>
        {
            if (await Dialogs.PickDateTimeAsync(XamlRoot, "Remind me", _task?.NextReminder(now) ?? later, withTime: true) is { } at) await SetReminder(at);
        }));
        if (_task?.Reminders is { Count: > 0 })
        {
            RemindMenu.Items.Add(new MenuFlyoutSeparator());
            RemindMenu.Items.Add(Item("Remove reminder", "", null, () => SaveAsync("reminder", (s, id) => s.SetRemindersAsync(id, Array.Empty<DateTimeOffset>()))));
        }
    }

    private Task SetReminder(DateTimeOffset at) => SaveAsync("reminder", (s, id) => s.SetRemindersAsync(id, new[] { at }));

    private static DateTimeOffset At(DateTime day, int hour) => new(day.Year, day.Month, day.Day, hour, 0, 0, DateTimeOffset.Now.Offset);
    private static DateTimeOffset RoundUp(DateTimeOffset t) => new DateTimeOffset(t.Year, t.Month, t.Day, t.Hour, 0, 0, t.Offset).AddHours(t.Minute > 0 ? 1 : 0);

    private void DueMenu_Opening(object sender, object e)
    {
        var now = DateTimeOffset.Now;
        DueMenu.Items.Clear();
        DueMenu.Items.Add(Item("Today", "", now.ToString("ddd", Culture), () => SetDue("today")));
        DueMenu.Items.Add(Item("Tomorrow", "", now.AddDays(1).ToString("ddd", Culture), () => SetDue("tomorrow")));
        DueMenu.Items.Add(Item("Next week", "", "Mon", () => SetDue("monday")));
        DueMenu.Items.Add(new MenuFlyoutSeparator());
        DueMenu.Items.Add(Item("Pick a date", "", null, async () =>
        {
            if (await Dialogs.PickDateTimeAsync(XamlRoot, "Due date", _task?.Due ?? now, withTime: false) is { } d)
                await SetDue(d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        }));
        if (_task?.Due is not null)
        {
            DueMenu.Items.Add(new MenuFlyoutSeparator());
            DueMenu.Items.Add(Item("Remove due date", "", null, () => SetDue("none")));
        }
    }

    private Task SetDue(string due) => SaveAsync("due date", (s, id) => s.UpdateAsync(id, due: due));

    private void StartMenu_Opening(object sender, object e) => FillSpanMenu(StartMenu, start: true);
    private void EndMenu_Opening(object sender, object e) => FillSpanMenu(EndMenu, start: false);

    private void FillSpanMenu(MenuFlyout menu, bool start)
    {
        var now = DateTimeOffset.Now;
        var current = _task is null ? null : VDates.OrNull(start ? _task.StartDate : _task.EndDate);
        var hour = start ? 9 : 18;
        Task Set(DateTimeOffset? d) => SaveAsync(start ? "start date" : "end date", (s, id) => s.SetSpanAsync(id, start ? d ?? VDates.Empty : null, start ? null : d ?? VDates.Empty));
        menu.Items.Clear();
        menu.Items.Add(Item("Today", "", now.ToString("ddd", Culture), () => Set(At(now.Date, hour))));
        menu.Items.Add(Item("Tomorrow", "", now.AddDays(1).ToString("ddd", Culture), () => Set(At(now.Date.AddDays(1), hour))));
        menu.Items.Add(Item("In a week", "", now.AddDays(7).ToString("ddd d MMM", Culture), () => Set(At(now.Date.AddDays(7), hour))));
        menu.Items.Add(new MenuFlyoutSeparator());
        menu.Items.Add(Item("Pick a date", "", null, async () =>
        {
            if (await Dialogs.PickDateTimeAsync(XamlRoot, start ? "Start date" : "End date", current ?? now, withTime: false) is { } d)
                await Set(At(d.Date, hour));
        }));
        if (current is not null)
        {
            menu.Items.Add(new MenuFlyoutSeparator());
            menu.Items.Add(Item(start ? "Remove start date" : "Remove end date", "", null, () => Set(null)));
        }
    }

    private void RepeatMenu_Opening(object sender, object e)
    {
        RepeatMenu.Items.Clear();
        foreach (var (name, rule) in new[] { ("Daily", RepeatRule.Daily), ("Weekdays", RepeatRule.Weekdays), ("Weekly", RepeatRule.Weekly), ("Monthly", RepeatRule.Monthly), ("Yearly", RepeatRule.Yearly) })
            RepeatMenu.Items.Add(Item(name, "", null, () => SetRepeat(rule)));
        RepeatMenu.Items.Add(new MenuFlyoutSeparator());
        RepeatMenu.Items.Add(Item("Custom", "", null, CustomRepeatAsync));
        if (_task is { } t && RepeatRule.From(t) is not null)
        {
            RepeatMenu.Items.Add(new MenuFlyoutSeparator());
            RepeatMenu.Items.Add(Item("Never repeat", "", null, () => SetRepeat(null)));
        }
    }

    private Task SetRepeat(RepeatRule? rule) => SaveAsync("repeat", (s, id) => s.SetRepeatAsync(id, rule));

    private async Task CustomRepeatAsync()
    {
        var every = new NumberBox { Header = "Every", Value = 2, Minimum = 1, Maximum = 999, SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Inline, MinWidth = 140 };
        var unit = new ComboBox { Header = "Unit", ItemsSource = new[] { "days", "weeks", "months", "years" }, SelectedIndex = 1, MinWidth = 140 };
        var panel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
        panel.Children.Add(every);
        panel.Children.Add(unit);
        var d = new ContentDialog { XamlRoot = XamlRoot, Title = "Repeat", Content = panel, PrimaryButtonText = "Save", CloseButtonText = "Cancel", DefaultButton = ContentDialogButton.Primary };
        if (await d.ShowAsync() != ContentDialogResult.Primary || double.IsNaN(every.Value)) return;
        if (RepeatRule.TryParse($"every {(int)every.Value} {unit.SelectedItem}", out var rule)) await SetRepeat(rule);
    }

    // ---------- categories ----------
    private async void CategoryFlyout_Opening(object sender, object e)
    {
        if (Svc is not { } svc || _task is null || Win is null) return;
        CategoryPanel.Children.Clear();
        await Win.RunAsync("Couldn't load categories", async () =>
        {
            var current = (_task.Labels ?? []).Where(DocketService.IsCategory).Select(l => l.Title).ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var cat in await svc.CategoriesAsync())
            {
                var box = new CheckBox { IsChecked = current.Contains(cat.Title) };
                var content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
                content.Children.Add(new Microsoft.UI.Xaml.Shapes.Ellipse { Width = 10, Height = 10, Fill = Ui.Hex(cat.HexColor), VerticalAlignment = VerticalAlignment.Center });
                content.Children.Add(new TextBlock { Text = cat.Title });
                box.Content = content;
                AutomationProperties.SetName(box, cat.Title);
                var title = cat.Title;
                box.Click += async (_, _) =>
                {
                    if (box.IsChecked == true) current.Add(title); else current.Remove(title);
                    var pick = current.ToList();
                    await SaveAsync("categories", (s, id) => s.SetCategoriesAsync(id, pick));
                };
                CategoryPanel.Children.Add(box);
            }
            if (CategoryPanel.Children.Count == 0) CategoryPanel.Children.Add(new TextBlock { Text = "No categories yet.", Opacity = 0.65 });
        });
    }

    private async void NewCategory_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != VirtualKey.Enter || NewCategoryBox.Text.Trim().Length == 0 || _task is null) return;
        e.Handled = true;
        var name = NewCategoryBox.Text.Trim();
        NewCategoryBox.Text = "";
        var current = (_task.Labels ?? []).Where(DocketService.IsCategory).Select(l => l.Title).Append(name).ToList();
        CategoryFlyout.Hide();
        await SaveAsync("categories", (s, id) => s.SetCategoriesAsync(id, current));
    }

    // ---------- files ----------
    private async void AddFile_Click(object sender, RoutedEventArgs e)
    {
        if (_task is null || App.Window is null) return;
        var picker = new FileOpenPicker();
        WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(App.Window));
        picker.FileTypeFilter.Add("*");
        var file = await picker.PickSingleFileAsync();
        if (file is null) return;
        await SaveAsync("files", (s, id) => s.Client.UploadAttachmentAsync(id, file.Path));
    }

    private async Task OpenFileAsync(VAttachment a)
    {
        if (Svc is not { } svc || Win is null) return;
        await Win.RunAsync("Couldn't open the file", async () =>
        {
            var dir = Path.Combine(Path.GetTempPath(), "Docket", a.TaskId.ToString(CultureInfo.InvariantCulture));
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, Path.GetFileName(a.File.Name));
            await File.WriteAllBytesAsync(path, await svc.Client.DownloadAttachmentAsync(a.TaskId, a.Id));
            await Launcher.LaunchFileAsync(await StorageFile.GetFileFromPathAsync(path));
        });
    }

    // ---------- footer ----------
    private void Close_Click(object sender, RoutedEventArgs e) => Closed?.Invoke(this, EventArgs.Empty);

    private async void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (_task is null || Svc is not { } svc || Win is null) return;
        if (App.Settings.ConfirmDelete && !await Dialogs.ConfirmAsync(XamlRoot, "Delete task?", $"\"{_task.Title}\" will be permanently deleted.")) return;
        var id = _task.Id;
        var snapshot = _task;
        await Win.RunAsync("Couldn't delete the task", async () =>
        {
            await svc.DeleteTaskAsync(id);
            Win.OfferUndo($"Deleted \"{snapshot.Title}\"", () => Pages.ListPage.RestoreAsync(svc, snapshot));
            Changed?.Invoke(this, TaskChange.Deleted);
        });
    }
}
