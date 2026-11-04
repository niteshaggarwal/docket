using System.Collections.ObjectModel;
using System.Globalization;
using System.Text;
using Docket.App.Services;
using Docket.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
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

/// <summary>One list: a smart list (My Day, Important...), one of the user's lists, or search results.</summary>
public sealed partial class ListPage : Page, IRefreshable
{
    private static readonly (string Name, string Hex)[] Colors =
    [
        ("Default", ""), ("Blue", "2564cf"), ("Teal", "0e8a7e"), ("Green", "2f8f46"), ("Amber", "d18b00"),
        ("Orange", "e8590c"), ("Red", "d13438"), ("Pink", "c2185b"), ("Purple", "7a4fd6"), ("Grey", "6b6b6b"),
    ];

    private ListKey _key = ListKey.Of(SmartList.MyDay);
    private readonly ObservableCollection<object> _open = [];
    private readonly ObservableCollection<object> _done = [];
    private SortMode _sort;
    private VProject? _project;

    public ListPage()
    {
        InitializeComponent();
        OpenList.ItemsSource = _open;
        DoneList.ItemsSource = _done;
        AddKey(VirtualKey.N, VirtualKeyModifiers.Control, () => AddBox.Focus(FocusState.Keyboard));
        AddKey(VirtualKey.D, VirtualKeyModifiers.Control, () => _ = WithSelected(t => ToggleDoneAsync(t, !t.IsDone)));
        AddKey(VirtualKey.T, VirtualKeyModifiers.Control, () => _ = WithSelected(t => SetMyDayAsync(t, !t.InMyDay)));
        AddKey(VirtualKey.I, VirtualKeyModifiers.Control, () => _ = WithSelected(t => SetImportantAsync(t, !t.IsImportant)));
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

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        if (e.Parameter is ListKey k) _key = k;
        _sort = App.Settings.Sorts.GetValueOrDefault(_key.SettingsKey, _key.Smart == SmartList.Planned ? SortMode.DueDate : SortMode.Custom);
        await RefreshAsync();
    }

    // ---------- loading ----------
    public async Task RefreshAsync()
    {
        if (Svc is not { } svc) return;
        LoadingRing.IsActive = true;
        await (Win?.RunAsync("Couldn't load tasks", async () =>
        {
            _project = _key.IsProject ? (await svc.ProjectsAsync()).FirstOrDefault(p => p.Id == _key.ProjectId) : null;
            SetupHeader();
            ListContent content;
            if (_key.Search is { } q)
            {
                var open = await svc.ListAsync(TaskView.Open, search: q, limit: 200);
                var done = (await svc.ListAsync(TaskView.Done, search: q, limit: 200)).Where(t => t.Task.Done).ToList();
                content = new ListContent(open, done);
            }
            else if (_key.Smart is { } s) content = await svc.SmartAsync(s, _sort, App.Settings.DefaultProjectId);
            else content = await svc.ProjectListAsync(_key.ProjectId, _sort);

            var showList = !_key.IsProject && _key.Smart != SmartList.Tasks;
            _open.Clear();
            if (_key.Smart == SmartList.Planned)
            {
                var now = DateTimeOffset.Now;
                foreach (var g in content.Open.GroupBy(t => DocketService.PlannedGroup(t.Task, now)))
                {
                    _open.Add(new GroupHeader(g.Key, g.Count()));
                    foreach (var t in g) _open.Add(new TaskItem(t.Task, t.Project, showList));
                }
            }
            else foreach (var t in content.Open) _open.Add(new TaskItem(t.Task, t.Project, showList));

            _done.Clear();
            foreach (var t in content.Completed) _done.Add(new TaskItem(t.Task, t.Project, showList));
            var hide = App.Settings.HideCompleted.Contains(_key.SettingsKey);
            CompletedExpander.Visibility = _done.Count > 0 && !hide ? Visibility.Visible : Visibility.Collapsed;
            CompletedHeader.Text = $"Completed  {_done.Count}";
            if (_key.Smart == SmartList.Completed) { CompletedExpander.Visibility = Visibility.Collapsed; }

            OpenList.CanReorderItems = OpenList.AllowDrop = _key.IsProject && _sort == SortMode.Custom;
            ShowEmpty(_open.Count == 0 && (_done.Count == 0 || hide));
            await (Win?.RefreshCountsAsync() ?? Task.CompletedTask);
            if (Detail.TaskId is { } openId && Detail.Visibility == Visibility.Visible) await Detail.LoadAsync(openId);
        }) ?? Task.CompletedTask);
        LoadingRing.IsActive = false;
    }

    private void SetupHeader()
    {
        string title, glyph, sub = "";
        if (_key.Search is { } q) { title = $"Searching for \"{q}\""; glyph = ""; }
        else if (_key.Smart is { } s)
        {
            title = s == SmartList.Tasks ? "Tasks" : ListKey.SmartTitle(s);
            glyph = ListKey.SmartGlyph(s);
            if (s == SmartList.MyDay) sub = DateTimeOffset.Now.ToString("dddd, d MMMM", CultureInfo.CurrentCulture);
        }
        else { title = _project?.Title ?? "List"; glyph = ""; }
        Heading.Text = title;
        Subheading.Text = sub;
        Subheading.Visibility = sub.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        HeadingIcon.Glyph = glyph;
        HeadingIcon.Foreground = _project is { HexColor.Length: 6 } p ? TaskItem.ColorBrush(p.HexColor, 1) : (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["AccentTextFillColorPrimaryBrush"];

        SuggestButton.Visibility = _key.Smart == SmartList.MyDay ? Visibility.Visible : Visibility.Collapsed;
        BoardButton.Visibility = _key.IsProject || _key.Smart == SmartList.Tasks ? Visibility.Visible : Visibility.Collapsed;
        AddBar.Visibility = _key.Smart == SmartList.Completed || _key.Search is not null ? Visibility.Collapsed : Visibility.Visible;
        SortButton.Visibility = _key.Smart == SmartList.Completed ? Visibility.Collapsed : Visibility.Visible;
        SortText.Text = _sort switch { SortMode.Importance => "Importance", SortMode.DueDate => "Due date", SortMode.Alphabetical => "A–Z", SortMode.CreationDate => "Newest", _ => "Sort" };
        foreach (var item in new[] { SortCustom, SortImportance, SortDue, SortAlpha, SortCreated }) item.IsChecked = (string)item.Tag == _sort.ToString();
    }

    private void ShowEmpty(bool empty)
    {
        EmptyPanel.Visibility = empty ? Visibility.Visible : Visibility.Collapsed;
        (EmptyIcon.Glyph, EmptyText.Text) = _key.Search is not null ? ("", "No tasks match your search.")
            : _key.Smart switch
            {
                SmartList.MyDay => ("", "Focus on your day. Add tasks here, or pick from Suggestions."),
                SmartList.Important => ("", "Star a task to see it here."),
                SmartList.Planned => ("", "Tasks with due dates show up here."),
                SmartList.Completed => ("", "Completed tasks show up here."),
                _ => ("", "No tasks yet. Add one above."),
            };
    }

    // ---------- adding ----------
    private async void AddBox_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != VirtualKey.Enter) return;
        e.Handled = true;
        await AddAsync();
    }

    private async void Add_Click(object sender, RoutedEventArgs e) => await AddAsync();

    private async Task AddAsync()
    {
        var text = AddBox.Text.Trim();
        if (text.Length == 0 || Svc is not { } svc || Win is null) return;
        AddBox.IsEnabled = false;
        await Win.RunAsync("Couldn't add the task", async () =>
        {
            var project = _key.IsProject ? _key.ProjectId.ToString(CultureInfo.InvariantCulture) : null;
            var due = _key.Smart == SmartList.Planned && QuickAdd.Parse(text, DateTimeOffset.Now).Due is null ? "today" : null;
            var t = await svc.AddAsync(text, project, due, defaultProjectId: App.Settings.DefaultProjectId);
            if (_key.Smart == SmartList.MyDay) await svc.SetMyDayAsync(t.Id, true);
            if (_key.Smart == SmartList.Important) await svc.SetImportantAsync(t.Id, true);
            AddBox.Text = "";
            Win.SetStatus($"Added to {t.Project}");
            await RefreshAsync();
        });
        AddBox.IsEnabled = true;
        AddBox.Focus(FocusState.Programmatic);
    }

    // ---------- row actions ----------
    private async void Done_Click(object sender, RoutedEventArgs e)
    {
        if (sender is CheckBox { Tag: TaskItem t } box) await ToggleDoneAsync(t, box.IsChecked == true);
    }

    private async Task ToggleDoneAsync(TaskItem t, bool done)
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
            await Task.Delay(350);
            await RefreshAsync();
        });
    }

    private async void Star_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: TaskItem t }) await SetImportantAsync(t, !t.IsImportant);
    }

    private async Task SetImportantAsync(TaskItem t, bool on)
    {
        if (Svc is not { } svc || Win is null) return;
        await Win.RunAsync("Couldn't update the task", async () => { t.Replace((await svc.SetImportantAsync(t.Id, on)).Task); if (_key.Smart == SmartList.Important) await RefreshAsync(); else await Win.RefreshCountsAsync(); });
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

    /// <summary>Undo for delete: recreates the task with its fields (comments and files can't come back).</summary>
    private static async Task RestoreAsync(DocketService svc, VTask s)
    {
        var created = await svc.Client.CreateTaskAsync(s.ProjectId, s.Title, s.Due, s.Priority) ?? throw new DocketException("Couldn't restore the task.");
        await svc.Client.ModifyTaskAsync(created.Id, x =>
        {
            x.Description = s.Description; x.IsFavorite = s.IsFavorite; x.Reminders = s.Reminders;
            x.RepeatAfter = s.RepeatAfter; x.RepeatMode = s.RepeatMode; x.Done = s.Done;
        });
        if (s.Labels is { Count: > 0 } ls) await svc.Client.SetTaskLabelsAsync(created.Id, ls.Select(l => l.Id));
    }

    private async Task WithSelected(Func<TaskItem, Task> action)
    {
        if ((OpenList.SelectedItem ?? DoneList.SelectedItem) is TaskItem t) await action(t);
    }

    private async void List_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Delete && ((ListView)sender).SelectedItem is TaskItem t) { e.Handled = true; await DeleteAsync(t); }
        if (e.Key == VirtualKey.Enter && ((ListView)sender).SelectedItem is TaskItem t2) { e.Handled = true; await OpenDetailAsync(t2); }
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
        Add(t.IsImportant ? "Remove importance" : "Mark as important", "", () => SetImportantAsync(t, !t.IsImportant));
        Add(t.IsDone ? "Mark as not completed" : "Mark as completed", "", () => ToggleDoneAsync(t, !t.IsDone));
        m.Items.Add(new MenuFlyoutSeparator());
        Add("Due today", "", () => SetDueAsync(t, "today"));
        Add("Due tomorrow", "", () => SetDueAsync(t, "tomorrow"));
        if (t.HasDue) Add("Remove due date", "", () => SetDueAsync(t, "none"));
        var move = new MenuFlyoutSubItem { Text = "Move task to...", Icon = new FontIcon { Glyph = "" } };
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

    // ---------- drag: reorder and move to another list ----------
    private void Rows_DragItemsStarting(object sender, DragItemsStartingEventArgs e)
    {
        if (e.Items.FirstOrDefault() is TaskItem t) e.Data.SetText($"docket-task:{t.Id}");
        else e.Cancel = true;
    }

    private async void Rows_DragItemsCompleted(ListViewBase sender, DragItemsCompletedEventArgs args)
    {
        if (!_key.IsProject || _sort != SortMode.Custom || Svc is not { } svc || Win is null || args.Items.FirstOrDefault() is not TaskItem moved) return;
        var view = _project?.ListView;
        if (view is null) return;
        var tasks = _open.OfType<TaskItem>().ToList();
        var i = tasks.IndexOf(moved);
        if (i < 0) return;
        var before = i > 0 ? tasks[i - 1].Task.Position : 0;
        var after = i < tasks.Count - 1 ? tasks[i + 1].Task.Position : before + 131072;
        if (after <= before) after = before + 131072;
        var pos = (before + after) / 2;
        await Win.RunAsync("Couldn't reorder", async () => { await svc.Client.SetPositionAsync(moved.Id, view.Id, pos); moved.Task.Position = pos; });
    }

    // ---------- details ----------
    private async void Row_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is TaskItem t) await OpenDetailAsync(t);
    }

    private async Task OpenDetailAsync(TaskItem t)
    {
        DetailColumn.Width = new GridLength(380);
        Detail.Visibility = Visibility.Visible;
        await Detail.LoadAsync(t.Id);
    }

    private void CloseDetail()
    {
        Detail.Visibility = Visibility.Collapsed;
        DetailColumn.Width = new GridLength(0);
        Detail.Clear();
    }

    private void Detail_Closed(object? sender, EventArgs e) => CloseDetail();

    private async void Detail_Changed(object? sender, Controls.TaskChange change)
    {
        if (change == Controls.TaskChange.Deleted) CloseDetail();
        await RefreshAsync();
    }

    // ---------- header actions ----------
    /// <summary>When the list is narrow (details open), the header buttons move under the title.</summary>
    private void HeaderGrid_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        var narrow = e.NewSize.Width < 620;
        Grid.SetRow(HeaderActions, narrow ? 1 : 0);
        Grid.SetColumn(HeaderActions, narrow ? 0 : 1);
        HeaderActions.HorizontalAlignment = narrow ? HorizontalAlignment.Left : HorizontalAlignment.Right;
    }

    private async void Sort_Click(object sender, RoutedEventArgs e)
    {
        _sort = Enum.Parse<SortMode>((string)((FrameworkElement)sender).Tag);
        App.Settings.Sorts[_key.SettingsKey] = _sort;
        App.Settings.Save();
        await RefreshAsync();
    }

    private void Board_Click(object sender, RoutedEventArgs e)
    {
        var p = _project ?? Win?.DefaultProject;
        if (p is not null) Frame.Navigate(typeof(BoardPage), p);
    }

    private async void Heading_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e) => await RenameAsync();
    private async void Rename_Click(object sender, RoutedEventArgs e) => await RenameAsync();

    private async Task RenameAsync()
    {
        if (_project is null || Svc is not { } svc || Win is null) return;
        var name = await Dialogs.PromptAsync(XamlRoot, "Rename list", "List name", _project.Title, "Rename");
        if (string.IsNullOrWhiteSpace(name)) return;
        await Win.RunAsync("Couldn't rename the list", async () => { await svc.RenameListAsync(_project.Id, title: name); await Win.LoadListsAsync(); await RefreshAsync(); });
    }

    private void MoreMenu_Opening(object sender, object e)
    {
        var isList = _project is not null;
        RenameItem.Visibility = ColorItem.Visibility = GroupItem.Visibility = DeleteListItem.Visibility = DeleteSeparator.Visibility =
            isList ? Visibility.Visible : Visibility.Collapsed;
        ShowCompletedItem.IsChecked = !App.Settings.HideCompleted.Contains(_key.SettingsKey);
        ShowCompletedItem.Visibility = _key.Smart == SmartList.Completed ? Visibility.Collapsed : Visibility.Visible;
        if (!isList) return;

        ColorItem.Items.Clear();
        foreach (var (name, hex) in Colors)
        {
            var item = new MenuFlyoutItem { Text = name, Icon = new FontIcon { Glyph = "", Foreground = TaskItem.ColorBrush(hex.Length > 0 ? hex : "8a8886", 1) } };
            item.Click += async (_, _) => await Win!.RunAsync("Couldn't change the colour", async () => { await Svc!.RenameListAsync(_project!.Id, hexColor: hex); await Win.LoadListsAsync(); await RefreshAsync(); });
            ColorItem.Items.Add(item);
        }
        GroupItem.Items.Clear();
        var none = new MenuFlyoutItem { Text = "No group" };
        none.Click += async (_, _) => await MoveToGroupAsync(0);
        GroupItem.Items.Add(none);
        foreach (var g in (Win?.Projects ?? []).Where(p => p.ParentProjectId == 0 && p.Id != _project!.Id && p.Id != Win?.DefaultProject?.Id))
        {
            var item = new MenuFlyoutItem { Text = g.Title };
            item.Click += async (_, _) => await MoveToGroupAsync(g.Id);
            GroupItem.Items.Add(item);
        }
    }

    private async Task MoveToGroupAsync(long parentId)
    {
        if (_project is null || Svc is not { } svc || Win is null) return;
        await Win.RunAsync("Couldn't move the list", async () => { await svc.RenameListAsync(_project.Id, parentId: parentId); await Win.LoadListsAsync(); });
    }

    private async void ShowCompleted_Click(object sender, RoutedEventArgs e)
    {
        if (ShowCompletedItem.IsChecked) App.Settings.HideCompleted.Remove(_key.SettingsKey);
        else App.Settings.HideCompleted.Add(_key.SettingsKey);
        App.Settings.Save();
        await RefreshAsync();
    }

    private void CopyList_Click(object sender, RoutedEventArgs e)
    {
        var sb = new StringBuilder(Heading.Text).AppendLine();
        foreach (var o in _open)
            if (o is GroupHeader h) sb.AppendLine().AppendLine(h.Title);
            else if (o is TaskItem t) sb.AppendLine($"- [ ] {t.Title}{(t.HasDue ? $" ({t.DueText})" : "")}");
        foreach (var t in _done.OfType<TaskItem>()) sb.AppendLine($"- [x] {t.Title}");
        var pkg = new DataPackage();
        pkg.SetText(sb.ToString());
        Clipboard.SetContent(pkg);
        Win?.SetStatus("List copied");
    }

    private async void DeleteList_Click(object sender, RoutedEventArgs e)
    {
        if (_project is null || Svc is not { } svc || Win is null) return;
        if (!await Dialogs.ConfirmAsync(XamlRoot, "Delete list?", $"\"{_project.Title}\" and all its tasks will be permanently deleted.")) return;
        await Win.RunAsync("Couldn't delete the list", async () => { await svc.DeleteListAsync(_project.Id); await Win.LoadListsAsync(); Win.SelectList(Win.DefaultProject?.Id ?? 0); });
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
                SuggestPanel.Children.Add(new TextBlock { Text = g.Key, Style = (Style)Application.Current.Resources["BodyStrongTextBlockStyle"], Margin = new Thickness(4, 10, 0, 2) });
                foreach (var s in g)
                {
                    var row = new Grid { ColumnSpacing = 8, Padding = new Thickness(4) };
                    row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                    row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                    var text = new StackPanel();
                    text.Children.Add(new TextBlock { Text = s.Task.Task.Title, TextWrapping = TextWrapping.Wrap });
                    text.Children.Add(new TextBlock { Text = s.Task.Project, Opacity = 0.6, Style = (Style)Application.Current.Resources["CaptionTextBlockStyle"] });
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
