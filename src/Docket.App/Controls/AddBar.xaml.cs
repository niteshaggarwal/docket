using Docket.App.Services;
using Docket.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.System;

namespace Docket.App.Controls;

/// <summary>
/// The add-task bar at the bottom of a list, To Do style. Plain text adds a task (quick-add words work).
/// "/" offers commands: /s searches live, /x /m /i act on the matches after a confirm, /ai plans tasks.
/// The sparkle button keeps AI planning on, so Enter sends the text to the AI and shows the plan for review.
/// </summary>
public sealed partial class AddBar : UserControl
{
    private CancellationTokenSource? _aiCts;
    private CancellationTokenSource? _searchCts;
    private SlashCommand? _pending;
    private List<TaskInfo> _matches = [];
    private List<PlannedTask> _plan = [];
    private readonly Dictionary<PlannedTask, CheckBox> _planBoxes = [];
    private string _planRequest = "";
    private string _placeholder = "Add a task";
    private bool _busy;

    public AddBar()
    {
        InitializeComponent();
        Box.PlaceholderText = _placeholder;
    }

    /// <summary>Adds a plain task; the page knows the list (My Day, Important, a project...).</summary>
    public Func<string, Task>? AddTask { get; set; }

    /// <summary>The list AI-planned tasks go to when the plan doesn't name one.</summary>
    public Func<long>? TargetProject { get; set; }

    /// <summary>Raised with the text to search for, or null when the bar leaves search.</summary>
    public event EventHandler<string?>? SearchChanged;

    /// <summary>Raised after the bar changed tasks (commands, AI plan), so the page reloads.</summary>
    public event EventHandler? TasksChanged;

    public string Placeholder
    {
        get => _placeholder;
        set { _placeholder = value; UpdateMode(); }
    }

    public void FocusBox() => Box.Focus(FocusState.Keyboard);

    private static DocketService? Svc => App.Service;
    private static MainWindow? Win => App.Window;
    private bool AiOn => AiToggle.IsChecked == true;

    // ---------- typing ----------
    private void Box_TextChanged(object sender, TextChangedEventArgs e)
    {
        var text = Box.Text;
        var cmd = SlashCommand.Parse(text);
        var typingVerb = text.StartsWith('/') && !text.Contains(' ');
        if (typingVerb) ShowSlash(text);
        else if (SlashPanel.Visibility == Visibility.Visible) HidePanel();

        if (_pending is not null && (cmd.Kind != _pending.Kind || cmd.Argument != _pending.Argument)) { _pending = null; if (ConfirmPanel.Visibility == Visibility.Visible) HidePanel(); }

        var query = cmd.Kind is SlashKind.Search or SlashKind.Done or SlashKind.MyDay or SlashKind.Important && cmd.Argument.Length > 0 ? cmd.Argument : null;
        DebouncedSearch(query);
        UpdateMode();
    }

    private void DebouncedSearch(string? query)
    {
        _searchCts?.Cancel();
        if (query is null) { SearchChanged?.Invoke(this, null); return; }
        var cts = _searchCts = new CancellationTokenSource();
        _ = Task.Delay(250, cts.Token).ContinueWith(t =>
        {
            if (!t.IsCanceled) DispatcherQueue.TryEnqueue(() => { if (!cts.IsCancellationRequested) SearchChanged?.Invoke(this, query); });
        }, TaskScheduler.Default);
    }

    private void UpdateMode()
    {
        var cmd = SlashCommand.Parse(Box.Text);
        var ai = AiOn || cmd.Kind == SlashKind.Ai;
        PlusIcon.Visibility = ai ? Visibility.Collapsed : Visibility.Visible;
        PlusSparkle.Visibility = ai ? Visibility.Visible : Visibility.Collapsed;
        AiGlyph.Fill = Ui.Brush(AiOn ? "TextOnAccentFillColorPrimaryBrush" : "TextFillColorPrimaryBrush");
        AiChip.Visibility = ai ? Visibility.Visible : Visibility.Collapsed;
        AiChipText.Text = _aiLabel ?? App.Settings.Ai.Label;
        if (ai && _aiLabel is null) _ = ResolveAiLabelAsync();
        Box.PlaceholderText = AiOn ? "Describe what you need, and AI plans the tasks (Enter)" : $"{_placeholder}    (type / for commands)";
        var name = ai ? "Plan with AI" : "Add task";
        AutomationProperties.SetName(PlusButton, name);
        ToolTipService.SetToolTip(PlusButton, ai ? "Plan with AI (Enter)" : "Add task (Enter)");
    }

    private void Box_PreviewKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Tab && SlashPanel.Visibility == Visibility.Visible && SlashList.Items.Count > 0)
        {
            e.Handled = true;
            Complete((SlashList.SelectedItem ?? SlashList.Items[0]) as ListViewItem);
        }
        else if (e.Key is VirtualKey.Down or VirtualKey.Up && SlashPanel.Visibility == Visibility.Visible && SlashList.Items.Count > 0)
        {
            e.Handled = true;
            var i = SlashList.SelectedIndex + (e.Key == VirtualKey.Down ? 1 : -1);
            SlashList.SelectedIndex = Math.Clamp(i, 0, SlashList.Items.Count - 1);
        }
    }

    private async void Box_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Enter)
        {
            e.Handled = true;
            if (SlashPanel.Visibility == Visibility.Visible && SlashList.SelectedItem is ListViewItem sel) { Complete(sel); return; }
            await SubmitAsync();
        }
        else if (e.Key == VirtualKey.Escape)
        {
            if (Panel.Visibility == Visibility.Visible) { e.Handled = true; CancelAi(); HidePanel(); }
            else if (Box.Text.Length > 0) { e.Handled = true; Box.Text = ""; }
        }
    }

    private async void Submit_Click(object sender, RoutedEventArgs e) => await SubmitAsync();

    private async Task SubmitAsync()
    {
        var text = Box.Text.Trim();
        if (text.Length == 0 || _busy) return;
        var cmd = SlashCommand.Parse(text);
        switch (cmd.Kind)
        {
            case SlashKind.Help: ShowSlash("/"); return;
            case SlashKind.Search:
                SearchChanged?.Invoke(this, cmd.Argument.Length > 0 ? cmd.Argument : null);
                return;
            case SlashKind.Done or SlashKind.MyDay or SlashKind.Important:
                await PrepareConfirmAsync(cmd);
                return;
            case SlashKind.Ai:
                if (cmd.Argument.Length > 0) await PlanAsync(cmd.Argument);
                return;
        }
        if (AiOn) { await PlanAsync(text); return; }
        if (AddTask is null) return;
        _busy = true;
        try { await AddTask(text); Box.Text = ""; }
        finally { _busy = false; }
        Box.Focus(FocusState.Programmatic);
    }

    // ---------- slash suggestions ----------
    private void ShowSlash(string typed)
    {
        SlashList.Items.Clear();
        foreach (var (command, help) in SlashCommand.All)
        {
            if (typed.Length > 1 && !command.StartsWith(typed, StringComparison.OrdinalIgnoreCase)) continue;
            var row = new Grid { ColumnSpacing = 16, Padding = new Thickness(0, 4, 0, 4) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(44) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var c = new TextBlock { Text = command, FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Cascadia Mono, Consolas"), Foreground = Ui.Brush("AccentTextFillColorPrimaryBrush"), VerticalAlignment = VerticalAlignment.Center };
            var h = Ui.Text(help, "BodyTextBlockStyle");
            h.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(h, 1);
            row.Children.Add(c);
            row.Children.Add(h);
            var item = new ListViewItem { Content = row, Tag = command, MinHeight = 36, Padding = new Thickness(8, 0, 8, 0) };
            AutomationProperties.SetName(item, $"{command}: {help}");
            SlashList.Items.Add(item);
        }
        if (SlashList.Items.Count == 0) { if (SlashPanel.Visibility == Visibility.Visible) HidePanel(); return; }
        SlashList.SelectedIndex = 0;
        Show(SlashPanel);
    }

    private void SlashList_ItemClick(object sender, ItemClickEventArgs e) => Complete(e.ClickedItem as ListViewItem);

    private void Complete(ListViewItem? item)
    {
        if (item?.Tag is not string command) return;
        HidePanel();
        if (command == "/?") { ShowSlash("/"); return; }
        Box.Text = command + " ";
        Box.SelectionStart = Box.Text.Length;
        Box.Focus(FocusState.Programmatic);
    }

    // ---------- commands on matches ----------
    private async Task PrepareConfirmAsync(SlashCommand cmd)
    {
        if (Svc is not { } svc || Win is null) return;
        if (cmd.Argument.Length == 0) { ShowMessage("", "Type what to look for after the command, for example \"/x invoice\"."); return; }
        await Win.RunAsync("Couldn't search", async () =>
        {
            var found = await svc.ListAsync(TaskView.Open, search: cmd.Argument, limit: 200);
            _matches = cmd.Kind switch
            {
                SlashKind.MyDay => found.Where(t => !DocketService.InMyDay(t.Task)).ToList(),
                SlashKind.Important => found.Where(t => !t.Task.IsFavorite).ToList(),
                _ => found,
            };
            SearchChanged?.Invoke(this, cmd.Argument);
            if (_matches.Count == 0) { ShowMessage("", $"No open tasks match \"{cmd.Argument}\"{(found.Count > 0 ? " that need this" : "")}."); return; }
            _pending = cmd;
            var n = _matches.Count;
            var noun = n == 1 ? "task" : "tasks";
            (ConfirmIcon.Glyph, ConfirmTitle.Text, ConfirmButton.Content) = cmd.Kind switch
            {
                SlashKind.Done => ("", $"Mark {n} {noun} done?", "Mark done"),
                SlashKind.MyDay => ("", $"Add {n} {noun} to My Day?", "Add to My Day"),
                _ => ("", $"Mark {n} {noun} as important?", "Mark important"),
            };
            ConfirmItems.Children.Clear();
            foreach (var m in _matches.Take(30))
            {
                var line = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
                line.Children.Add(Ui.Text(m.Task.Title, "BodyTextBlockStyle"));
                line.Children.Add(Ui.Text(m.Project, "CaptionTextBlockStyle", 0.6));
                ((TextBlock)line.Children[1]).VerticalAlignment = VerticalAlignment.Center;
                ConfirmItems.Children.Add(line);
            }
            if (_matches.Count > 30) ConfirmItems.Children.Add(Ui.Text($"and {_matches.Count - 30} more", "CaptionTextBlockStyle", 0.6));
            Show(ConfirmPanel);
            ConfirmButton.Focus(FocusState.Programmatic);
        });
    }

    private async void Confirm_Click(object sender, RoutedEventArgs e)
    {
        if (_pending is not { } cmd || Svc is not { } svc || Win is null) return;
        var ids = _matches.Select(m => m.Id).ToList();
        ConfirmButton.IsEnabled = false;
        await Win.RunAsync("Couldn't change the tasks", async () =>
        {
            var n = await svc.ApplyToMatchesAsync(cmd, ids);
            var what = cmd.Kind switch { SlashKind.Done => "Completed", SlashKind.MyDay => "Added to My Day:", _ => "Starred" };
            Win.OfferUndo($"{what} {n} {(n == 1 ? "task" : "tasks")}", async () =>
            {
                foreach (var id in ids)
                {
                    if (cmd.Kind == SlashKind.Done) await svc.SetDoneAsync(id, false);
                    else if (cmd.Kind == SlashKind.MyDay) await svc.SetMyDayAsync(id, false);
                    else await svc.SetImportantAsync(id, false);
                }
            });
            _pending = null;
            HidePanel();
            Box.Text = "";
            TasksChanged?.Invoke(this, EventArgs.Empty);
        });
        ConfirmButton.IsEnabled = true;
    }

    private void ConfirmCancel_Click(object sender, RoutedEventArgs e) { _pending = null; HidePanel(); Box.Focus(FocusState.Programmatic); }

    // ---------- AI ----------
    private void AiToggle_Click(object sender, RoutedEventArgs e)
    {
        UpdateMode();
        if (AiOn && !App.Settings.Ai.IsReady && App.Settings.AiProvider != "ollama")
            ShowMessage("", "AI is off or has no API key. Choose a provider in Settings > AI.");
        Box.Focus(FocusState.Programmatic);
    }

    private async Task PlanAsync(string request)
    {
        if (Svc is not { } svc || Win is null) return;
        var config = App.Settings.Ai;
        if (config.Provider == "off" || (!config.IsReady && config.Provider != "ollama"))
        {
            ShowMessage("", "AI is off. Turn on Local AI (Ollama) or add an API key for Claude, ChatGPT or Gemini in Settings > AI.");
            return;
        }
        CancelAi();
        var cts = _aiCts = new CancellationTokenSource();
        _busy = true;
        if (_aiLabel is null) await ResolveAiLabelAsync();
        var label = _aiLabel ?? config.Label;
        ThinkingTitle.Text = $"Thinking with {label}…";
        ThinkingSub.Text = config.Provider == "ollama"
            ? "Local models run on this PC. The first plan after start-up can take up to a minute while the model loads."
            : "Your text is sent to the provider to plan the tasks.";
        ThinkingRing.IsActive = true;
        Show(ThinkingPanel);
        try
        {
            var lists = (await svc.ProjectsAsync()).Select(p => p.Title);
            _plan = await AiPlanner.PlanAsync(config, request, DateTimeOffset.Now, lists, ct: cts.Token);
            _planRequest = request;
            if (_plan.Count == 0) { ShowMessage("", "The AI didn't find any tasks in that. Try saying it differently."); return; }
            ShowPlan(label);
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested) { HidePanel(); }
        catch (Exception ex) when (ex is DocketException or HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
        {
            ShowMessage("", ex is TaskCanceledException ? "The AI took too long to answer. Try again." : ex.Message);
        }
        finally
        {
            ThinkingRing.IsActive = false;
            _busy = false;
            if (ReferenceEquals(_aiCts, cts)) _aiCts = null;
        }
    }

    private void CancelAi() => _aiCts?.Cancel();

    private static string? _aiLabel;

    /// <summary>The name of the AI that will answer: for local AI, the installed model Docket will actually use.</summary>
    private async Task ResolveAiLabelAsync()
    {
        var config = App.Settings.Ai;
        var label = config.Label;
        if (config.Provider == "ollama" && await AiPlanner.OllamaModelsAsync(config.OllamaUrl) is { Count: > 0 } installed
            && !installed.Contains(config.Model) && AiPlanner.PickLocalModel(installed) is { } picked)
            label = $"Local AI ({picked})";
        _aiLabel = label;
        AiChipText.Text = label;
    }

    /// <summary>Forget the cached AI name (after Settings change the provider or model).</summary>
    public static void ResetAiLabel() => _aiLabel = null;
    private void CancelAi_Click(object sender, RoutedEventArgs e) { CancelAi(); HidePanel(); }

    private void ShowPlan(string label)
    {
        PlanItems.Children.Clear();
        _planBoxes.Clear();
        foreach (var p in _plan) AddPlanRow(p, 0);
        PlanTitle.Text = $"{label} suggests";
        UpdatePlanCount();
        Show(PlanPanel);
        AddPlanButton.Focus(FocusState.Programmatic);
    }

    private void AddPlanRow(PlannedTask p, int depth)
    {
        var meta = new List<string>();
        if (!string.IsNullOrWhiteSpace(p.Due) && DocketService.TryParseIso(p.Due, DateTimeOffset.Now, out var due)) meta.Add("Due " + Ui.FriendlyDate(due));
        if (p.Priority > 0) meta.Add(DocketService.PriorityName(p.Priority));
        if (p.MyDay) meta.Add("My Day");
        if (p.Important) meta.Add("Important");
        if (!string.IsNullOrWhiteSpace(p.List)) meta.Add(p.List!);
        var content = new StackPanel();
        content.Children.Add(Ui.Text(p.Title, null, 1, TextWrapping.Wrap));
        if (meta.Count > 0) content.Children.Add(Ui.Text(string.Join("  ·  ", meta), "CaptionTextBlockStyle", 0.65));
        var box = new CheckBox { IsChecked = true, Content = content, Margin = new Thickness(depth * 24, 0, 0, 0) };
        AutomationProperties.SetName(box, p.Title);
        box.Click += (_, _) =>
        {
            SetChildren(p, box.IsChecked == true);
            UpdatePlanCount();
        };
        _planBoxes[p] = box;
        PlanItems.Children.Add(box);
        foreach (var s in p.Subtasks ?? []) AddPlanRow(s, depth + 1);
    }

    private void SetChildren(PlannedTask p, bool on)
    {
        foreach (var s in p.Subtasks ?? [])
        {
            if (_planBoxes.TryGetValue(s, out var b)) b.IsChecked = on;
            SetChildren(s, on);
        }
    }

    private int CountChecked(IEnumerable<PlannedTask> list) =>
        list.Sum(p => (_planBoxes.TryGetValue(p, out var b) && b.IsChecked == true ? 1 : 0) + CountChecked(p.Subtasks ?? []));

    private void UpdatePlanCount()
    {
        var n = CountChecked(_plan);
        AddPlanButton.Content = n == 1 ? "Add 1 task" : $"Add {n} tasks";
        AddPlanButton.IsEnabled = n > 0;
    }

    /// <summary>The plan with unticked tasks left out. An unticked parent's ticked subtasks move up a level.</summary>
    private List<PlannedTask> Picked(IEnumerable<PlannedTask> list)
    {
        var result = new List<PlannedTask>();
        foreach (var p in list)
        {
            var kids = Picked(p.Subtasks ?? []);
            if (_planBoxes.TryGetValue(p, out var b) && b.IsChecked == true)
                result.Add(new PlannedTask { Title = p.Title, Due = p.Due, Priority = p.Priority, Important = p.Important, MyDay = p.MyDay, List = p.List, Notes = p.Notes, Subtasks = kids });
            else result.AddRange(kids);
        }
        return result;
    }

    private async void AddPlan_Click(object sender, RoutedEventArgs e)
    {
        if (Svc is not { } svc || Win is null) return;
        var picked = Picked(_plan);
        var target = TargetProject?.Invoke() ?? 0;
        if (target == 0) target = (await svc.DefaultProjectAsync(App.Settings.DefaultProjectId)).Id;
        AddPlanButton.IsEnabled = false;
        await Win.RunAsync("Couldn't add the planned tasks", async () =>
        {
            var added = new List<TaskInfo>();
            foreach (var p in picked) added.Add(await svc.AddPlannedAsync(p, target));
            var n = CountChecked(_plan);
            Win.SetStatus($"Added {n} {(n == 1 ? "task" : "tasks")} from the plan");
            Win.OfferUndo($"Added {n} planned {(n == 1 ? "task" : "tasks")}", async () =>
            {
                foreach (var t in added) await DeleteTreeAsync(svc, t.Task);
            });
            HidePanel();
            if (Box.Text.Trim() == _planRequest || Box.Text.Trim() == "/ai " + _planRequest) Box.Text = "";
            TasksChanged?.Invoke(this, EventArgs.Empty);
        });
        AddPlanButton.IsEnabled = true;
    }

    private static async Task DeleteTreeAsync(DocketService svc, VTask t)
    {
        var fresh = await svc.Client.GetTaskAsync(t.Id);
        foreach (var s in fresh?.Subtasks ?? []) await DeleteTreeAsync(svc, s);
        await svc.DeleteTaskAsync(t.Id);
    }

    private void DiscardPlan_Click(object sender, RoutedEventArgs e) { HidePanel(); Box.Focus(FocusState.Programmatic); }

    // ---------- panel ----------
    private void ShowMessage(string glyph, string text)
    {
        MessageIcon.Glyph = glyph;
        MessageText.Text = text;
        Show(MessagePanel);
    }

    private void CloseMessage_Click(object sender, RoutedEventArgs e) => HidePanel();

    private void Show(FrameworkElement part)
    {
        foreach (var p in new FrameworkElement[] { SlashPanel, ConfirmPanel, ThinkingPanel, PlanPanel, MessagePanel })
            p.Visibility = ReferenceEquals(p, part) ? Visibility.Visible : Visibility.Collapsed;
        Panel.Visibility = Visibility.Visible;
    }

    private void HidePanel()
    {
        Panel.Visibility = Visibility.Collapsed;
        foreach (var p in new FrameworkElement[] { SlashPanel, ConfirmPanel, ThinkingPanel, PlanPanel, MessagePanel }) p.Visibility = Visibility.Collapsed;
    }
}
