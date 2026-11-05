using System.Diagnostics;
using System.Globalization;
using Docket.Core;
using Docket.Mobile.Services;

namespace Docket.Mobile.Views;

/// <summary>
/// Asks the AI to plan tasks, shows a clear "thinking" state (local models can take a minute) with Cancel,
/// then a preview to confirm. Nothing is added until the person taps Add. Returns how many tasks were added.
/// </summary>
public sealed class AiPlanSheet : Sheet<int?>
{
    private readonly DocketService _svc;
    private readonly AiConfig _config;
    private readonly string _request;
    private readonly long _fallbackProject;
    private readonly bool _myDay;
    private CancellationTokenSource? _cts;
    private List<PlannedTask> _plan = [];
    private readonly HashSet<PlannedTask> _skip = [];

    public AiPlanSheet(DocketService svc, AiConfig config, string request, long fallbackProject, bool myDay) : base("Plan with AI")
    {
        _svc = svc;
        _config = config;
        _request = request;
        _fallbackProject = fallbackProject;
        _myDay = myDay;
        _ = RunAsync();
    }

    private async Task RunAsync()
    {
        _cts = new CancellationTokenSource();
        ShowThinking();
        try
        {
            var lists = (await _svc.ProjectsAsync()).Select(p => p.Title).ToList();
            _plan = await AiPlanner.PlanAsync(_config, _request, DateTimeOffset.Now, lists, ct: _cts.Token);
            if (_myDay) foreach (var p in _plan) p.MyDay = true;
            ShowPreview();
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) when (U.IsExpected(ex) || ex is System.Text.Json.JsonException or KeyNotFoundException) { ShowError(U.Friendly(ex)); }
    }

    private void Reset(string title)
    {
        Body.Clear();
        Body.Add(U.Text(title, 20, true));
    }

    private void ShowThinking()
    {
        Reset("Planning…");
        var who = _config.Provider == "ollama" ? $"Local AI on your PC ({_config.Model})" : _config.Label;
        Body.Add(U.Caption($"“{_request}”", 14));
        var elapsed = U.Caption("0 s", 13);
        var spinner = new ActivityIndicator { IsRunning = true, WidthRequest = 28, HeightRequest = 28 };
        var row = new HorizontalStackLayout { Spacing = 12, Margin = new Thickness(0, 12) };
        row.Add(spinner);
        var col = new VerticalStackLayout { VerticalOptions = LayoutOptions.Center };
        col.Add(U.Text($"{who} is thinking", 15, true));
        col.Add(elapsed);
        row.Add(col);
        Body.Add(row);
        if (_config.Provider == "ollama") Body.Add(U.Caption("A local model can take up to a minute. You can keep this open or cancel.", 13));
        Body.Add(U.Plain("Cancel", () => { _cts?.Cancel(); Close(null); return Task.CompletedTask; }));
        var sw = Stopwatch.StartNew();
        Dispatcher.StartTimer(TimeSpan.FromSeconds(1), () =>
        {
            elapsed.Text = $"{(int)sw.Elapsed.TotalSeconds} s";
            return spinner.IsRunning && Body.Contains(row);
        });
    }

    private void ShowError(string message)
    {
        Reset("Couldn't plan that");
        Body.Add(U.Text(message, 15, false, Th.Danger));
        var buttons = new HorizontalStackLayout { Spacing = 8 };
        buttons.Add(U.Accent("Try again", RunAsync));
        buttons.Add(U.Plain("Close", () => { Close(null); return Task.CompletedTask; }));
        Body.Add(buttons);
    }

    private void ShowPreview()
    {
        if (_plan.Count == 0) { ShowError("The AI didn't suggest any tasks. Try describing it differently."); return; }
        Reset(_plan.Count == 1 ? "1 task planned" : $"{_plan.Count} tasks planned");
        Body.Add(U.Caption("Untick anything you don't want, then add.", 13));
        foreach (var p in _plan) AddPlanRow(p, 0);
        var count = _plan.Count(p => !_skip.Contains(p));
        var buttons = new HorizontalStackLayout { Spacing = 8, Margin = new Thickness(0, 10, 0, 0) };
        buttons.Add(U.Accent(count == 1 ? "Add 1 task" : $"Add {count} tasks", AddAsync));
        buttons.Add(U.Plain("Cancel", () => { Close(null); return Task.CompletedTask; }));
        Body.Add(buttons);
    }

    private void AddPlanRow(PlannedTask p, int depth)
    {
        var included = !_skip.Contains(p);
        var g = new Grid { ColumnDefinitions = [new(36), new(GridLength.Star)], Padding = new Thickness(depth * 22, 4, 0, 4) };
        var check = new FluentCheck { IsChecked = included, Scale = depth > 0 ? 0.8 : 1 };
        if (depth == 0) check.Command = new Command(() => { if (!_skip.Remove(p)) _skip.Add(p); ShowPreview(); });
        else check.Opacity = 0.5;
        g.Add(check, 0);
        var col = new VerticalStackLayout { Spacing = 1, VerticalOptions = LayoutOptions.Center, Opacity = included ? 1 : 0.5 };
        col.Add(U.Text(p.Title, depth == 0 ? 15 : 14, depth == 0));
        var meta = new List<string>();
        if (!string.IsNullOrWhiteSpace(p.Due) && DocketService.TryParseIso(p.Due, DateTimeOffset.Now, out var due))
            meta.Add(due.ToString(due.TimeOfDay == TimeSpan.Zero ? "ddd d MMM" : "ddd d MMM, HH:mm", CultureInfo.CurrentCulture));
        if (p.Priority > 0) meta.Add(DocketService.PriorityName(p.Priority));
        if (p.Important) meta.Add("★ Important");
        if (p.MyDay) meta.Add("My Day");
        if (!string.IsNullOrWhiteSpace(p.List)) meta.Add(p.List!);
        if (meta.Count > 0) col.Add(U.Caption(string.Join("  ·  ", meta)));
        if (!string.IsNullOrWhiteSpace(p.Notes)) col.Add(U.Caption(p.Notes!));
        g.Add(col, 1);
        Body.Add(g);
        foreach (var s in p.Subtasks ?? []) AddPlanRow(s, depth + 1);
    }

    private async Task AddAsync()
    {
        var pick = _plan.Where(p => !_skip.Contains(p)).ToList();
        Reset("Adding…");
        var bar = new ProgressBar { Progress = 0 }.Themed(ProgressBar.ProgressColorProperty, Th.Accent);
        Body.Add(bar);
        var n = 0;
        try
        {
            foreach (var p in pick)
            {
                await _svc.AddPlannedAsync(p, _fallbackProject);
                n++;
                bar.Progress = (double)n / pick.Count;
            }
            HapticFeedback.Default.Perform(HapticFeedbackType.Click);
            Close(n);
        }
        catch (Exception ex) when (U.IsExpected(ex)) { ShowError(U.Friendly(ex) + (n > 0 ? $" ({n} added before this.)" : "")); }
    }
}
