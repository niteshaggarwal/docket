using System.Globalization;
using Docket.App.Pages;
using Docket.App.Services;
using Docket.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.ApplicationModel.DataTransfer;
using Windows.System;

namespace Docket.App;

public sealed partial class MainWindow : Window
{
    private readonly List<NavigationViewItemBase> _listItems = [];
    private readonly HashSet<long> _shared = [];
    private Func<Task>? _undo;
    private CancellationTokenSource? _undoTimer;

    public List<VProject> Projects { get; private set; } = [];
    public VProject? DefaultProject { get; private set; }

    public MainWindow()
    {
        InitializeComponent();
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(TitleBar);
        SizeForDisplay(1440, 920);
        AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "Docket.ico"));
        ApplyTheme();

        AddKey(VirtualKey.F, VirtualKeyModifiers.Control, () => SearchBox.Focus(FocusState.Keyboard));
        AddKey(VirtualKey.L, VirtualKeyModifiers.Control, () => _ = NewListAsync(group: false));
        AddKey(VirtualKey.R, VirtualKeyModifiers.Control, () => _ = RefreshAllAsync());
        AddKey(VirtualKey.F5, VirtualKeyModifiers.None, () => _ = RefreshAllAsync());

        Nav.Loaded += async (_, _) => await StartAsync();
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(nint hwnd);

    /// <summary>Opens at a comfortable size in effective pixels, kept inside the screen's work area.</summary>
    private void SizeForDisplay(int width, int height)
    {
        var scale = GetDpiForWindow(WinRT.Interop.WindowNative.GetWindowHandle(this)) / 96.0;
        if (scale <= 0) scale = 1;
        var area = Microsoft.UI.Windowing.DisplayArea.GetFromWindowId(AppWindow.Id, Microsoft.UI.Windowing.DisplayAreaFallback.Primary).WorkArea;
        var w = Math.Min((int)(width * scale), area.Width - 40);
        var h = Math.Min((int)(height * scale), area.Height - 40);
        AppWindow.MoveAndResize(new Windows.Graphics.RectInt32(area.X + (area.Width - w) / 2, area.Y + (area.Height - h) / 2, w, h));
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
        // DOCKET_START picks the first page (used for development and screenshots): overview, people, upcoming, open,
        // settings, important, planned, or list:<id>[:List|Table|Gantt|Board].
        var start = Environment.GetEnvironmentVariable("DOCKET_START") ?? "";
        if (start.StartsWith("list:", StringComparison.Ordinal))
        {
            var parts = start.Split(':');
            if (parts.Length > 2 && Enum.TryParse<ListViewMode>(parts[2], true, out var mode)) ViewPrefs.Set($"list:{parts[1]}", mode);
            start = $"list:{parts[1]}";
        }
        Nav.SelectedItem = start switch
        {
            "overview" => OverviewItem,
            "people" => PeopleItem,
            "upcoming" => UpcomingItem,
            "open" => OpenItem,
            "important" => ImportantItem,
            "planned" => PlannedItem,
            "settings" => Nav.SettingsItem,
            { Length: > 5 } s when s.StartsWith("list:", StringComparison.Ordinal) && long.TryParse(s[5..], CultureInfo.InvariantCulture, out var id) => FindListItem(id) ?? MyDayItem,
            _ => MyDayItem,
        };
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
                var children = Projects.Where(c => c.ParentProjectId == p.Id).ToList();
                var item = MakeListItem(p, isGroup: children.Count > 0);
                foreach (var child in children) item.MenuItems.Add(MakeListItem(child, isGroup: false));
                _listItems.Add(item);
                Nav.MenuItems.Add(item);
            }
            ListsHeader.Visibility = _listItems.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            if (selectedKey is string key && key.StartsWith("list:", StringComparison.Ordinal))
                Nav.SelectedItem = FindListItem(long.Parse(key[5..], CultureInfo.InvariantCulture)) ?? Nav.SelectedItem;
            await RefreshCountsAsync();
            SetStatus("");
            _ = UpdateSharedAsync(svc);
        }
        catch (HttpRequestException ex) when (LocalServer.IsConnectionRefused(ex) && LocalServer.IsLocal(App.Settings.ServerUrl) && !_retryingLists)
        {
            // The server on this PC isn't running: start it and load again, before showing anything.
            _retryingLists = true;
            try
            {
                if (await RestartServerAsync()) await LoadListsAsync();
                else ShowServerDown("Can't reach your Docket server");
            }
            finally { _retryingLists = false; }
        }
        catch (Exception ex) when (ex is HttpRequestException or VikunjaException or DocketException or TaskCanceledException)
        {
            ShowError("Can't reach your Docket server", ex.Message);
        }
    }

    private bool _retryingLists;

    private NavigationViewItem MakeListItem(VProject p, bool isGroup)
    {
        var style = ListStyle.Parse(p.Description);
        var tint = Ui.IsHex(p.HexColor) ? Ui.Hex(p.HexColor) : null;
        var title = new TextBlock { Text = p.Title, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
        var people = new FontIcon { Glyph = "", FontSize = 12, Opacity = 0.6, Visibility = _shared.Contains(p.Id) ? Visibility.Visible : Visibility.Collapsed, Margin = new Thickness(8, 0, 4, 0) };
        ToolTipService.SetToolTip(people, "Shared list");
        var content = new Grid();
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(people, 1);
        content.Children.Add(title);
        content.Children.Add(people);
        var item = new NavigationViewItem
        {
            Content = content, Tag = $"list:{p.Id}", AllowDrop = true,
            Icon = Icons.NavIcon(style.Icon, tint, isGroup ? "" : Icons.ListGlyph),
        };
        AutomationProperties.SetName(item, _shared.Contains(p.Id) ? $"{p.Title}, shared" : p.Title);
        item.DragOver += ListItem_DragOver;
        item.Drop += ListItem_Drop;
        return item;
    }

    /// <summary>Marks lists shared with others (people, teams or a public link) with a small people icon.</summary>
    private async Task UpdateSharedAsync(DocketService svc)
    {
        var found = new HashSet<long>();
        var checks = Projects.Select(async p =>
        {
            try
            {
                var s = await svc.SharingAsync(p.Id);
                if (s.People.Count + s.Teams.Count + s.Links.Count > 0) lock (found) found.Add(p.Id);
            }
            catch (Exception ex) when (ex is HttpRequestException or VikunjaException or TaskCanceledException) { }
        });
        await Task.WhenAll(checks);
        if (found.SetEquals(_shared)) return;
        _shared.Clear();
        _shared.UnionWith(found);
        foreach (var item in AllListItems())
            if (item.Tag is string tag && long.TryParse(tag.AsSpan(5), CultureInfo.InvariantCulture, out var id) && item.Content is Grid { Children: [_, FontIcon icon] })
            {
                icon.Visibility = _shared.Contains(id) ? Visibility.Visible : Visibility.Collapsed;
                var name = Projects.FirstOrDefault(p => p.Id == id)?.Title ?? "";
                AutomationProperties.SetName(item, _shared.Contains(id) ? $"{name}, shared" : name);
            }
    }

    private IEnumerable<NavigationViewItem> AllListItems()
    {
        foreach (var top in _listItems.OfType<NavigationViewItem>())
        {
            yield return top;
            foreach (var child in top.MenuItems.OfType<NavigationViewItem>()) yield return child;
        }
    }

    private NavigationViewItem? FindListItem(long id) => AllListItems().FirstOrDefault(i => (string)i.Tag == $"list:{id}");

    public bool IsShared(long projectId) => _shared.Contains(projectId);

    public async Task RefreshCountsAsync()
    {
        if (App.Service is not { } svc) return;
        try
        {
            await svc.EnsureMyDayFreshAsync();
            var open = await svc.Client.GetTasksAsync("done = false");
            var now = DateTimeOffset.Now;
            SetBadge(MyDayBadge, open.Count(DocketService.InMyDay));
            SetBadge(ImportantBadge, open.Count(t => t.IsFavorite));
            SetBadge(PlannedBadge, open.Count(t => t.Due is not null));
            SetBadge(UpcomingBadge, open.Count(t => t.Due is { } d && d.ToOffset(now.Offset).Date >= now.Date && d < now.Date.AddDays(8)));
            SetBadge(OverdueBadge, open.Count(t => t.IsOverdue(now)));
            SetBadge(AllBadge, open.Count);
            SetBadge(TasksBadge, open.Count(t => t.ProjectId == DefaultProject?.Id));
            ToolTipService.SetToolTip(OverviewItem, OverdueBadge.Visibility == Visibility.Visible ? $"Overview: {OverdueBadge.Value} overdue" : "Overview");
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
        if (tag == "page:Overview") ContentFrame.Navigate(typeof(OverviewPage));
        else if (tag == "page:People") ContentFrame.Navigate(typeof(PeoplePage));
        else if (tag.StartsWith("view:", StringComparison.Ordinal))
            ContentFrame.Navigate(typeof(ListPage), ListKey.Of(Enum.Parse<ExtraView>(tag[5..])));
        else if (tag.StartsWith("smart:", StringComparison.Ordinal))
            ContentFrame.Navigate(typeof(ListPage), ListKey.Of(Enum.Parse<SmartList>(tag[6..])));
        else if (tag.StartsWith("list:", StringComparison.Ordinal))
            ContentFrame.Navigate(typeof(ListPage), ListKey.Project(long.Parse(tag[5..], CultureInfo.InvariantCulture)));
    }

    public void SelectList(long projectId)
    {
        if (projectId == DefaultProject?.Id) Nav.SelectedItem = TasksItem;
        else if (FindListItem(projectId) is { } item) Nav.SelectedItem = item;
    }

    public void ShowPeople() => Nav.SelectedItem = PeopleItem;

    /// <summary>Opens a smart list or extra view by key (used by the Overview cards).</summary>
    public void Show(ListKey key)
    {
        NavigationViewItem? item = key.Extra switch
        {
            ExtraView.Upcoming => UpcomingItem,
            ExtraView.Open => OpenItem,
            _ => key.Smart switch
            {
                SmartList.MyDay => MyDayItem, SmartList.Important => ImportantItem, SmartList.Planned => PlannedItem,
                SmartList.All => AllItem, SmartList.Completed => CompletedItem, SmartList.Tasks => TasksItem, _ => null,
            },
        };
        if (item is not null) Nav.SelectedItem = item;
        else if (key.IsProject) SelectList(key.ProjectId);
    }

    // ---------- drag a task onto a list ----------
    private string TitleOf(NavigationViewItem item) =>
        (string)item.Tag == "smart:Tasks" ? "Tasks" : Projects.FirstOrDefault(p => $"list:{p.Id}" == (string)item.Tag)?.Title ?? "list";

    private void ListItem_DragOver(object sender, DragEventArgs e)
    {
        if (e.DataView.Contains(StandardDataFormats.Text)) { e.AcceptedOperation = DataPackageOperation.Move; e.DragUIOverride.Caption = "Move to " + TitleOf((NavigationViewItem)sender); }
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
            await svc.UpdateAsync(id, project: target.Id.ToString(CultureInfo.InvariantCulture));
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
            if (group) await svc.CreateProjectAsync("Untitled list", p.Id.ToString(CultureInfo.InvariantCulture));
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

    /// <summary>Opens Docket in the browser: the Docket-styled web app (docket-web) when it runs on this PC, else the server itself.</summary>
    private async void OpenWeb_Tapped(object sender, TappedRoutedEventArgs e)
    {
        var url = App.Settings.ServerUrl;
        var web = Environment.GetEnvironmentVariable("DOCKET_WEB_URL") ?? "http://localhost:3457";
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
            if ((await http.GetAsync(web.TrimEnd('/') + "/docket-web/health")).IsSuccessStatusCode) url = web;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException) { }
        await Launcher.LaunchUriAsync(new Uri(url));
    }

    // ---------- messages ----------
    public void ShowError(string title, string message)
    {
        ErrorBar.ActionButton = null;
        ErrorBar.Title = title;
        ErrorBar.Message = message.Replace("Vikunja: ", "", StringComparison.Ordinal).Replace("Vikunja", "the server", StringComparison.Ordinal);
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

    /// <summary>Runs an action and shows any server error in the error bar.</summary>
    public async Task RunAsync(string failTitle, Func<Task> action)
    {
        try { await action(); }
        catch (HttpRequestException ex) when (LocalServer.IsConnectionRefused(ex) && LocalServer.IsLocal(App.Settings.ServerUrl))
        {
            // The server on this PC has stopped: start it again and retry once, before bothering the person.
            if (await RestartServerAsync())
            {
                try { await action(); return; }
                catch (Exception again) when (again is HttpRequestException or VikunjaException or DocketException or TaskCanceledException)
                { ShowError(failTitle, again.Message); return; }
            }
            ShowServerDown(failTitle);
        }
        catch (Exception ex) when (ex is HttpRequestException or VikunjaException or DocketException or TaskCanceledException)
        { ShowError(failTitle, ex.Message); }
    }

    private Task<bool>? _restarting;

    /// <summary>Starts the local task server (once, however many screens ask) and reports while it does.</summary>
    public async Task<bool> RestartServerAsync()
    {
        var first = _restarting is null;
        if (first)
        {
            SetStatus("Starting your task server…");
            _restarting = LocalServer.EnsureRunningAsync(App.Settings.ServerUrl, App.Settings.VikunjaExe);
        }
        var ok = await _restarting!;
        if (first)
        {
            _restarting = null;
            SetStatus(ok ? "Task server restarted" : "");
            if (ok) ErrorBar.IsOpen = false;
        }
        return ok;
    }

    /// <summary>The failsafe: the server couldn't be started, so offer a manual retry instead of a dead end.</summary>
    private void ShowServerDown(string failTitle)
    {
        ShowError(failTitle, LocalServer.StillDownMessage(App.Settings.ServerUrl));
        var retry = new Button { Content = "Start server and retry" };
        retry.Click += async (_, _) =>
        {
            retry.IsEnabled = false;
            if (await RestartServerAsync()) { ErrorBar.IsOpen = false; await RefreshAllAsync(); }
            else retry.IsEnabled = true;
        };
        ErrorBar.ActionButton = retry;
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
