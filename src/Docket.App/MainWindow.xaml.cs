using Docket.App.Pages;
using Docket.App.Services;
using Docket.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.ApplicationModel.DataTransfer;
using Windows.System;

namespace Docket.App;

public sealed partial class MainWindow : Window
{
    private readonly List<NavigationViewItemBase> _listItems = [];
    private Func<Task>? _undo;
    private CancellationTokenSource? _undoTimer;

    public List<VProject> Projects { get; private set; } = [];
    public VProject? DefaultProject { get; private set; }

    public MainWindow()
    {
        InitializeComponent();
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(TitleBar);
        AppWindow.Resize(new Windows.Graphics.SizeInt32(1360, 880));
        AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "Docket.ico"));
        ApplyTheme();

        AddKey(VirtualKey.F, VirtualKeyModifiers.Control, () => SearchBox.Focus(FocusState.Keyboard));
        AddKey(VirtualKey.L, VirtualKeyModifiers.Control, () => _ = NewListAsync(group: false));
        AddKey(VirtualKey.R, VirtualKeyModifiers.Control, () => _ = RefreshAllAsync());
        AddKey(VirtualKey.F5, VirtualKeyModifiers.None, () => _ = RefreshAllAsync());

        Nav.Loaded += async (_, _) => await StartAsync();
    }

    private void AddKey(VirtualKey key, VirtualKeyModifiers mods, Action action)
    {
        var a = new KeyboardAccelerator { Key = key, Modifiers = mods };
        a.Invoked += (_, e) => { e.Handled = true; action(); };
        Nav.KeyboardAccelerators.Add(a);
    }

    public void ApplyTheme() => Root.RequestedTheme = App.Settings.Theme switch
    {
        "light" => ElementTheme.Light, "dark" => ElementTheme.Dark, _ => ElementTheme.Default,
    };

    private async Task StartAsync()
    {
        if (App.Service is null) { Nav.SelectedItem = Nav.SettingsItem; return; }
        await LoadListsAsync();
        Nav.SelectedItem = MyDayItem;
    }

    public async Task RefreshAllAsync()
    {
        await LoadListsAsync();
        if (ContentFrame.Content is IRefreshable r) await r.RefreshAsync();
    }

    // ---------- sidebar ----------
    public async Task LoadListsAsync()
    {
        if (App.Service is not { } svc) return;
        try
        {
            Projects = await svc.ProjectsAsync(refresh: true);
            DefaultProject = await svc.DefaultProjectAsync(App.Settings.DefaultProjectId);
            var selectedKey = (Nav.SelectedItem as NavigationViewItem)?.Tag;

            foreach (var item in _listItems) Nav.MenuItems.Remove(item);
            _listItems.Clear();
            foreach (var p in Projects.Where(p => p.ParentProjectId == 0 && p.Id != DefaultProject.Id))
            {
                var item = MakeListItem(p);
                foreach (var child in Projects.Where(c => c.ParentProjectId == p.Id))
                    item.MenuItems.Add(MakeListItem(child));
                item.Icon = new FontIcon { Glyph = item.MenuItems.Count > 0 ? "" : "" };
                _listItems.Add(item);
                Nav.MenuItems.Add(item);
            }
            ListsHeader.Visibility = _listItems.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            if (selectedKey is string key && key.StartsWith("list:", StringComparison.Ordinal))
                Nav.SelectedItem = FindListItem(long.Parse(key[5..], System.Globalization.CultureInfo.InvariantCulture)) ?? Nav.SelectedItem;
            await RefreshCountsAsync();
            SetStatus("");
        }
        catch (Exception ex) when (ex is HttpRequestException or VikunjaException or DocketException or TaskCanceledException)
        {
            ShowError("Can't reach Vikunja", ex.Message);
        }
    }

    private NavigationViewItem MakeListItem(VProject p)
    {
        var item = new NavigationViewItem
        {
            Content = p.Title, Tag = $"list:{p.Id}", AllowDrop = true,
            Icon = new FontIcon { Glyph = "", Foreground = string.IsNullOrEmpty(p.HexColor) ? null : TaskItem.ColorBrush(p.HexColor, 1) },
        };
        item.DragOver += ListItem_DragOver;
        item.Drop += ListItem_Drop;
        return item;
    }

    private NavigationViewItem? FindListItem(long id)
    {
        foreach (var top in _listItems.OfType<NavigationViewItem>())
        {
            if ((string)top.Tag == $"list:{id}") return top;
            foreach (var child in top.MenuItems.OfType<NavigationViewItem>()) if ((string)child.Tag == $"list:{id}") return child;
        }
        return null;
    }

    public async Task RefreshCountsAsync()
    {
        if (App.Service is not { } svc) return;
        try
        {
            await svc.EnsureMyDayFreshAsync();
            var open = await svc.Client.GetTasksAsync("done = false");
            SetBadge(MyDayBadge, open.Count(DocketService.InMyDay));
            SetBadge(ImportantBadge, open.Count(t => t.IsFavorite));
            SetBadge(PlannedBadge, open.Count(t => t.Due is not null));
            SetBadge(AllBadge, open.Count);
            SetBadge(TasksBadge, open.Count(t => t.ProjectId == DefaultProject?.Id));
            var s = App.Settings;
            ImportantItem.Visibility = Vis(s.ShowImportant && !(s.HideEmptySmartLists && !open.Any(t => t.IsFavorite)));
            PlannedItem.Visibility = Vis(s.ShowPlanned && !(s.HideEmptySmartLists && !open.Any(t => t.Due is not null)));
            AllItem.Visibility = Vis(s.ShowAll);
            CompletedItem.Visibility = Vis(s.ShowCompletedList);
        }
        catch (Exception ex) when (ex is HttpRequestException or VikunjaException or TaskCanceledException) { /* counts are cosmetic */ }
    }

    private static Visibility Vis(bool b) => b ? Visibility.Visible : Visibility.Collapsed;

    private static void SetBadge(InfoBadge badge, int n)
    {
        badge.Value = n;
        badge.Visibility = n > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void Nav_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        ErrorBar.IsOpen = false;
        if (args.IsSettingsSelected) { ContentFrame.Navigate(typeof(SettingsPage)); return; }
        if ((args.SelectedItem as NavigationViewItem)?.Tag is not string tag) return;
        if (tag.StartsWith("smart:", StringComparison.Ordinal))
            ContentFrame.Navigate(typeof(ListPage), ListKey.Of(Enum.Parse<SmartList>(tag[6..])));
        else if (tag.StartsWith("list:", StringComparison.Ordinal))
            ContentFrame.Navigate(typeof(ListPage), ListKey.Project(long.Parse(tag[5..], System.Globalization.CultureInfo.InvariantCulture)));
    }

    public void SelectList(long projectId)
    {
        if (projectId == DefaultProject?.Id) Nav.SelectedItem = TasksItem;
        else if (FindListItem(projectId) is { } item) Nav.SelectedItem = item;
    }

    // ---------- drag a task onto a list ----------
    private void ListItem_DragOver(object sender, DragEventArgs e)
    {
        if (e.DataView.Contains(StandardDataFormats.Text)) { e.AcceptedOperation = DataPackageOperation.Move; e.DragUIOverride.Caption = "Move to " + ((NavigationViewItem)sender).Content; }
    }

    private async void ListItem_Drop(object sender, DragEventArgs e)
    {
        if (App.Service is not { } svc || sender is not NavigationViewItem item) return;
        var text = await e.DataView.GetTextAsync();
        if (!text.StartsWith("docket-task:", StringComparison.Ordinal) || !long.TryParse(text[12..], out var id)) return;
        var target = (string)item.Tag == "smart:Tasks" ? DefaultProject : Projects.FirstOrDefault(p => $"list:{p.Id}" == (string)item.Tag);
        if (target is null) return;
        await RunAsync("Couldn't move the task", async () =>
        {
            await svc.UpdateAsync(id, project: target.Id.ToString(System.Globalization.CultureInfo.InvariantCulture));
            SetStatus($"Moved to {target.Title}");
            await RefreshAllAsync();
        });
    }

    // ---------- new list / group ----------
    private async void NewList_Tapped(object sender, TappedRoutedEventArgs e) => await NewListAsync(group: false);
    private async void NewGroup_Tapped(object sender, TappedRoutedEventArgs e) => await NewListAsync(group: true);

    public async Task NewListAsync(bool group)
    {
        if (App.Service is not { } svc) return;
        var name = await Dialogs.PromptAsync(Content.XamlRoot, group ? "New group" : "New list", group ? "Group name" : "List name", group ? "Untitled group" : "Untitled list");
        if (string.IsNullOrWhiteSpace(name)) return;
        string? parent = null;
        if (!group)
        {
            var groups = Projects.Where(p => p.ParentProjectId == 0 && p.Id != DefaultProject?.Id && Projects.Any(c => c.ParentProjectId == p.Id)).ToList();
            if (groups.Count > 0) parent = await Dialogs.PickAsync(Content.XamlRoot, "Put it in a group?", ["No group", .. groups.Select(g => g.Title)]) is { } pick && pick != "No group" ? pick : null;
        }
        await RunAsync("Couldn't create the list", async () =>
        {
            var p = await svc.CreateProjectAsync(name, parent);
            if (group) await svc.CreateProjectAsync("Untitled list", p.Id.ToString(System.Globalization.CultureInfo.InvariantCulture));
            await LoadListsAsync();
            SelectList(p.Id);
        });
    }

    // ---------- search ----------
    private void Search_QuerySubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args)
    {
        if (string.IsNullOrWhiteSpace(args.QueryText)) return;
        Nav.SelectedItem = null;
        ContentFrame.Navigate(typeof(ListPage), ListKey.Find(args.QueryText.Trim()));
    }

    private void Search_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        if (args.Reason == AutoSuggestionBoxTextChangeReason.UserInput && sender.Text.Trim().Length >= 2)
            ContentFrame.Navigate(typeof(ListPage), ListKey.Find(sender.Text.Trim()));
    }

    private async void OpenWeb_Tapped(object sender, TappedRoutedEventArgs e) =>
        await Launcher.LaunchUriAsync(new Uri(App.Settings.ServerUrl));

    // ---------- messages ----------
    public void ShowError(string title, string message)
    {
        ErrorBar.Title = title;
        ErrorBar.Message = message;
        ErrorBar.IsOpen = true;
    }

    public void SetStatus(string text) => StatusText.Text = text;

    /// <summary>Shows a short message with an Undo button for a few seconds.</summary>
    public void OfferUndo(string message, Func<Task> undo)
    {
        _undo = undo;
        UndoBar.Message = message;
        UndoBar.IsOpen = true;
        _undoTimer?.Cancel();
        var cts = _undoTimer = new CancellationTokenSource();
        _ = Task.Delay(6000, cts.Token).ContinueWith(t => { if (!t.IsCanceled) DispatcherQueue.TryEnqueue(() => { UndoBar.IsOpen = false; _undo = null; }); }, TaskScheduler.Default);
    }

    private async void Undo_Click(object sender, RoutedEventArgs e)
    {
        UndoBar.IsOpen = false;
        if (_undo is { } u) { _undo = null; await RunAsync("Couldn't undo", async () => { await u(); await RefreshAllAsync(); }); }
    }

    /// <summary>Runs an action and shows any Vikunja error in the error bar.</summary>
    public async Task RunAsync(string failTitle, Func<Task> action)
    {
        try { await action(); }
        catch (Exception ex) when (ex is HttpRequestException or VikunjaException or DocketException or TaskCanceledException)
        { ShowError(failTitle, ex.Message); }
    }

    public async Task OnConnectedAsync()
    {
        await LoadListsAsync();
        Nav.SelectedItem = MyDayItem;
    }
}

/// <summary>Pages that can reload their data (F5, after changes elsewhere).</summary>
public interface IRefreshable
{
    Task RefreshAsync();
}
