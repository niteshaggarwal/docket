using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Docket.Core;
using Docket.Mobile.Services;
using Docket.Mobile.Views;

namespace Docket.Mobile.ViewModels;

public sealed record SlashItem(string Command, string Help);

/// <summary>The add bar: quick add, slash commands ("/s", "/x", "/m", "/i", "/ai", "/") and AI planning.</summary>
public sealed partial class ListViewModel
{
    public ObservableCollection<SlashItem> SlashItems { get; } = [];

    [ObservableProperty] public partial string NewTask { get; set; } = "";
    [ObservableProperty] public partial bool AiMode { get; set; }
    [ObservableProperty] public partial bool ShowSlash { get; set; }

    public string AddPlaceholder => AiMode ? "Describe what you need; AI plans the tasks" : "Add a task, or / for commands";
    public Color AiColor => AiMode ? Th.Accent.Now : Th.Text2.Now;
    public string AiFont => AiMode ? "FluentIconsFilled" : "FluentIcons";
    partial void OnAiModeChanged(bool value) { OnPropertyChanged(nameof(AddPlaceholder)); OnPropertyChanged(nameof(AiColor)); OnPropertyChanged(nameof(AiFont)); }

    partial void OnNewTaskChanged(string value)
    {
        var t = value ?? "";
        var open = t.StartsWith('/') && !t.Contains(' ');
        SlashItems.Clear();
        if (open)
            foreach (var (cmd, help) in SlashCommand.All.Where(c => c.Command.StartsWith(t, StringComparison.OrdinalIgnoreCase) || t == "/"))
                SlashItems.Add(new SlashItem(cmd, help));
        ShowSlash = SlashItems.Count > 0;
    }

    [RelayCommand]
    private void PickSlash(SlashItem item) => NewTask = item.Command == "/?" ? "/" : item.Command + " ";

    [RelayCommand]
    private async Task ToggleAiAsync()
    {
        if (!AiSettings.IsOn)
        {
            if (await Shell.Current.DisplayAlertAsync("Plan with AI", "Pick an AI first: local AI on your PC, Claude, ChatGPT or Gemini.", "Set up AI", "Not now"))
                await Shell.Current.GoToAsync("aisettings");
            return;
        }
        AiMode = !AiMode;
    }

    [RelayCommand]
    private async Task AddAsync()
    {
        var text = NewTask.Trim();
        if (text.Length == 0 || session.Service is not { } svc) return;
        var cmd = SlashCommand.Parse(text);
        switch (cmd.Kind)
        {
            case SlashKind.Help:
                NewTask = "/";
                return;
            case SlashKind.Search:
                if (cmd.Argument.Length == 0) return;
                NewTask = "";
                await Shell.Current.GoToAsync($"list?key={Uri.EscapeDataString("search:" + cmd.Argument)}");
                return;
            case SlashKind.Ai:
                if (cmd.Argument.Length == 0) { AiMode = true; NewTask = ""; return; }
                NewTask = "";
                await PlanAsync(svc, cmd.Argument);
                return;
            case SlashKind.Done or SlashKind.MyDay or SlashKind.Important:
                if (cmd.Argument.Length == 0) { Error = "Add words after the command, e.g. “/x invoice”."; return; }
                NewTask = "";
                await ActOnMatchesAsync(svc, cmd);
                return;
        }
        NewTask = "";
        if (AiMode) { await PlanAsync(svc, text); return; }
        await RunAsync(async s =>
        {
            var project = TargetProjectId;
            var due = Smart == SmartList.Planned && QuickAdd.Parse(text, DateTimeOffset.Now).Due is null ? "today" : IsUpcoming && QuickAdd.Parse(text, DateTimeOffset.Now).Due is null ? "today" : null;
            var t = await s.AddAsync(text, project > 0 ? project.ToString(CultureInfo.InvariantCulture) : null, due, defaultProjectId: Preferences.Default.Get("default.project", 0L));
            if (Smart == SmartList.MyDay) await s.SetMyDayAsync(t.Id, true);
            if (Smart == SmartList.Important) await s.SetImportantAsync(t.Id, true);
            await Reminders.RescheduleAsync(s);
        });
    }

    private long TargetProjectId => _project?.Id ?? 0;

    private async Task ActOnMatchesAsync(DocketService svc, SlashCommand cmd)
    {
        try
        {
            var matches = await svc.ListAsync(TaskView.Open, search: cmd.Argument, limit: 100);
            if (matches.Count == 0) { await Shell.Current.DisplayAlertAsync("No matches", $"No open tasks match “{cmd.Argument}”.", "OK"); return; }
            var (title, verb) = cmd.Kind switch
            {
                SlashKind.Done => ("Mark as done?", matches.Count == 1 ? "Mark done" : $"Mark {matches.Count} done"),
                SlashKind.MyDay => ("Add to My Day?", "Add to My Day"),
                _ => ("Star these tasks?", "Star"),
            };
            if (await Sheet<bool>.ShowAsync(new ConfirmTasksSheet(title, verb, matches)) != true) return;
            IsBusy = true;
            await svc.ApplyToMatchesAsync(cmd, matches.Select(m => m.Id));
            HapticFeedback.Default.Perform(HapticFeedbackType.Click);
            await RefreshAsync();
        }
        catch (Exception ex) when (U.IsExpected(ex)) { Error = U.Friendly(ex); }
        finally { IsBusy = false; }
    }

    private async Task PlanAsync(DocketService svc, string request)
    {
        var config = await AiSettings.ConfigAsync(session.ServerUrl);
        if (!config.IsReady)
        {
            if (await Shell.Current.DisplayAlertAsync("Plan with AI", "The AI isn't set up yet (it needs a model, and an API key for Claude, ChatGPT or Gemini).", "Set up AI", "Cancel"))
                await Shell.Current.GoToAsync("aisettings");
            return;
        }
        var fallback = _project?.Id ?? (await svc.DefaultProjectAsync(Preferences.Default.Get("default.project", 0L))).Id;
        var added = await Sheet<int?>.ShowAsync(new AiPlanSheet(svc, config, request, fallback, Smart == SmartList.MyDay));
        if (added is > 0) await RefreshAsync();
    }
}
