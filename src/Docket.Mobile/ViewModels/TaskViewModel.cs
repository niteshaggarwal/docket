using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Docket.Core;
using Docket.Mobile.Services;

namespace Docket.Mobile.ViewModels;

public sealed partial class StepRow(int index, Step step) : ObservableObject
{
    public int Index { get; } = index;
    public string Title { get; } = step.Title;
    public bool Done { get; } = step.Done;
    public double Opacity => Done ? 0.55 : 1;
    public string CheckGlyph => Done ? "" : "";
}

public sealed record FileRow(VAttachment Attachment)
{
    public string Name => Attachment.File.Name;
    public string Size => Attachment.File.Size switch
    {
        < 1024 => $"{Attachment.File.Size} B",
        < 1048576 => $"{Attachment.File.Size / 1024.0:0.#} KB",
        _ => $"{Attachment.File.Size / 1048576.0:0.#} MB",
    };
}

/// <summary>Task details: title, steps, My Day, reminder, due, repeat, categories, files, list, notes.</summary>
public sealed partial class TaskViewModel(Session session) : ObservableObject, IQueryAttributable
{
    private static readonly CultureInfo Ui = CultureInfo.CurrentCulture;
    private long _id;
    private VTask? _task;
    private TaskNotes _notes = new("", []);

    public ObservableCollection<StepRow> Steps { get; } = [];
    public ObservableCollection<FileRow> Files { get; } = [];

    [ObservableProperty] public partial string TaskTitle { get; set; } = "";
    [ObservableProperty] public partial bool IsDone { get; set; }
    [ObservableProperty] public partial bool IsImportant { get; set; }
    [ObservableProperty] public partial bool InMyDay { get; set; }
    [ObservableProperty] public partial string ReminderText { get; set; } = "Remind me";
    [ObservableProperty] public partial string DueText { get; set; } = "Add due date";
    [ObservableProperty] public partial bool IsOverdue { get; set; }
    [ObservableProperty] public partial string RepeatText { get; set; } = "Repeat";
    [ObservableProperty] public partial string CategoryText { get; set; } = "Pick a category";
    [ObservableProperty] public partial string ListText { get; set; } = "";
    [ObservableProperty] public partial string Notes { get; set; } = "";
    [ObservableProperty] public partial string NewStep { get; set; } = "";
    [ObservableProperty] public partial string Footer { get; set; } = "";
    [ObservableProperty] public partial bool IsBusy { get; set; }
    [ObservableProperty] public partial string? Error { get; set; }

    public string StarGlyph => IsImportant ? "" : "";
    public string StarFont => IsImportant ? "FluentIconsFilled" : "FluentIcons";
    public string DoneGlyph => IsDone ? "" : "";
    public string MyDayText => InMyDay ? "Added to My Day" : "Add to My Day";
    partial void OnIsImportantChanged(bool value) { OnPropertyChanged(nameof(StarGlyph)); OnPropertyChanged(nameof(StarFont)); }
    partial void OnIsDoneChanged(bool value) => OnPropertyChanged(nameof(DoneGlyph));
    partial void OnInMyDayChanged(bool value) => OnPropertyChanged(nameof(MyDayText));

    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        if (query.TryGetValue("id", out var v) && long.TryParse(v.ToString(), out var id)) _id = id;
    }

    [RelayCommand]
    public async Task LoadAsync()
    {
        if (session.Service is not { } svc || _id == 0) return;
        IsBusy = true;
        try
        {
            _task = await svc.GetAsync(_id);
            _task.Attachments = await svc.Client.GetAttachmentsAsync(_id);
            Render(svc);
        }
        catch (Exception ex) when (ex is HttpRequestException or VikunjaException or DocketException or TaskCanceledException) { Error = ex.Message; }
        finally { IsBusy = false; }
    }

    private void Render(DocketService svc)
    {
        if (_task is not { } t) return;
        _notes = TaskNotes.Parse(t.Description);
        TaskTitle = t.Title;
        IsDone = t.Done;
        IsImportant = t.IsFavorite;
        InMyDay = DocketService.InMyDay(t);
        ReminderText = t.NextReminder(DateTimeOffset.Now) is { } r ? $"Remind me at {r.ToLocalTime().ToString("t", Ui)}, {r.ToLocalTime().ToString("ddd d MMM", Ui)}" : "Remind me";
        DueText = t.Due is { } d ? $"Due {d.ToLocalTime().ToString("ddd, d MMMM", Ui)}" : "Add due date";
        IsOverdue = t.IsOverdue(DateTimeOffset.Now);
        RepeatText = RepeatRule.From(t)?.Describe() ?? "Repeat";
        var cats = (t.Labels ?? []).Where(DocketService.IsCategory).Select(l => l.Title).ToList();
        CategoryText = cats.Count > 0 ? string.Join(", ", cats) : "Pick a category";
        Notes = _notes.Notes;
        Steps.Clear();
        for (var i = 0; i < _notes.Steps.Count; i++) Steps.Add(new StepRow(i, _notes.Steps[i]));
        Files.Clear();
        foreach (var a in t.Attachments ?? []) Files.Add(new FileRow(a));
        _ = SetListTextAsync(svc, t.ProjectId);
        Footer = t.Done && VDates.OrNull(t.DoneAt) is { } da ? $"Completed {da.ToLocalTime().ToString("ddd, d MMMM", Ui)}" : $"Created {t.Created.ToLocalTime().ToString("ddd, d MMMM", Ui)}";
    }

    private async Task SetListTextAsync(DocketService svc, long projectId) => ListText = await svc.ProjectNameAsync(projectId);

    private async Task SaveAsync(Func<DocketService, Task> action)
    {
        if (session.Service is not { } svc) return;
        IsBusy = true;
        Error = null;
        try { await action(svc); await LoadAsync(); _ = Reminders.RescheduleAsync(svc); }
        catch (Exception ex) when (ex is HttpRequestException or VikunjaException or DocketException or TaskCanceledException) { Error = ex.Message; }
        finally { IsBusy = false; }
    }

    [RelayCommand] private Task ToggleDoneAsync() { HapticFeedback.Default.Perform(HapticFeedbackType.Click); return SaveAsync(s => s.SetDoneAsync(_id, !IsDone)); }
    [RelayCommand] private Task ToggleStarAsync() => SaveAsync(s => s.SetImportantAsync(_id, !IsImportant));
    [RelayCommand] private Task ToggleMyDayAsync() => SaveAsync(s => s.SetMyDayAsync(_id, !InMyDay));

    [RelayCommand]
    private Task SaveTitleAsync() => _task is null || TaskTitle.Trim().Length == 0 || TaskTitle.Trim() == _task.Title ? Task.CompletedTask
        : SaveAsync(s => s.UpdateAsync(_id, title: TaskTitle.Trim()));

    [RelayCommand]
    private Task SaveNotesAsync() => _task is null || Notes == _notes.Notes ? Task.CompletedTask
        : SaveAsync(s => s.SetNotesAndStepsAsync(_id, n => n with { Notes = Notes }));

    [RelayCommand]
    private Task AddStepAsync()
    {
        var text = NewStep.Trim();
        if (text.Length == 0) return Task.CompletedTask;
        NewStep = "";
        return SaveAsync(s => s.AddStepAsync(_id, text));
    }

    [RelayCommand] private Task ToggleStepAsync(StepRow step) => SaveAsync(s => s.SetStepDoneAsync(_id, step.Index, !step.Done));

    [RelayCommand]
    private async Task StepMenuAsync(StepRow step)
    {
        var pick = await Shell.Current.DisplayActionSheetAsync(step.Title, "Cancel", "Delete step", "Promote to task");
        if (pick == "Delete step") await SaveAsync(s => s.RemoveStepAsync(_id, step.Index));
        else if (pick == "Promote to task" && _task is not null)
            await SaveAsync(async s => { await s.AddAsync(step.Title, _task.ProjectId.ToString(CultureInfo.InvariantCulture)); await s.RemoveStepAsync(_id, step.Index); });
    }

    [RelayCommand]
    private async Task ReminderAsync()
    {
        var now = DateTime.Now;
        var later = new DateTime(now.Year, now.Month, now.Day, now.Hour, 0, 0).AddHours(now.Minute > 0 ? 4 : 3);
        var tomorrow = now.Date.AddDays(1).AddHours(9);
        var nextWeek = now.Date.AddDays(((int)DayOfWeek.Monday - (int)now.DayOfWeek + 7) % 7 is 0 ? 7 : ((int)DayOfWeek.Monday - (int)now.DayOfWeek + 7) % 7).AddHours(9);
        var options = new List<string>();
        if (later.Date == now.Date) options.Add($"Later today ({later.ToString("t", Ui)})");
        options.Add($"Tomorrow ({tomorrow.ToString("ddd t", Ui)})");
        options.Add($"Next week ({nextWeek.ToString("ddd t", Ui)})");
        options.Add("Pick a date and time");
        var hasReminder = _task?.Reminders is { Count: > 0 };
        var pick = await Shell.Current.DisplayActionSheetAsync("Remind me", "Cancel", hasReminder ? "Remove reminder" : null, [.. options]);
        DateTime? at = pick switch
        {
            null or "Cancel" => null,
            "Remove reminder" => DateTime.MinValue,
            var p when p.StartsWith("Later", StringComparison.Ordinal) => later,
            var p when p.StartsWith("Tomorrow", StringComparison.Ordinal) => tomorrow,
            var p when p.StartsWith("Next week", StringComparison.Ordinal) => nextWeek,
            _ => await PickDateTimeAsync(withTime: true),
        };
        if (at is null) return;
        await Reminders.EnsurePermissionAsync();
        await SaveAsync(s => s.SetRemindersAsync(_id, at == DateTime.MinValue ? [] : [new DateTimeOffset(at.Value)]));
    }

    [RelayCommand]
    private async Task DueAsync()
    {
        var pick = await Shell.Current.DisplayActionSheetAsync("Due date", "Cancel", _task?.Due is null ? null : "Remove due date", "Today", "Tomorrow", "Next week", "Pick a date");
        string? due = pick switch
        {
            "Today" => "today", "Tomorrow" => "tomorrow", "Next week" => "monday", "Remove due date" => "none",
            "Pick a date" => await PickDateTimeAsync(withTime: false) is { } d ? d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : null,
            _ => null,
        };
        if (due is not null) await SaveAsync(s => s.UpdateAsync(_id, due: due));
    }

    [RelayCommand]
    private async Task RepeatAsync()
    {
        var hasRepeat = _task is { } t && RepeatRule.From(t) is not null;
        var pick = await Shell.Current.DisplayActionSheetAsync("Repeat", "Cancel", hasRepeat ? "Never repeat" : null, "Daily", "Weekdays", "Weekly", "Monthly", "Yearly", "Custom");
        RepeatRule? rule = pick switch
        {
            "Daily" => RepeatRule.Daily, "Weekdays" => RepeatRule.Weekdays, "Weekly" => RepeatRule.Weekly,
            "Monthly" => RepeatRule.Monthly, "Yearly" => RepeatRule.Yearly, _ => null,
        };
        if (pick == "Custom")
        {
            var text = await Shell.Current.DisplayPromptAsync("Repeat", "For example: every 3 weeks", "Save", "Cancel", "every 2 weeks");
            if (text is null) return;
            if (!RepeatRule.TryParse(text, out var custom)) { Error = "Try something like \"every 3 weeks\" or \"every 2 months\"."; return; }
            rule = custom;
        }
        if (rule is not null || pick == "Never repeat") await SaveAsync(s => s.SetRepeatAsync(_id, rule));
    }

    [RelayCommand]
    private async Task CategoriesAsync()
    {
        if (session.Service is not { } svc || _task is null) return;
        var all = await svc.CategoriesAsync();
        var current = (_task.Labels ?? []).Where(DocketService.IsCategory).Select(l => l.Title).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var options = all.Select(c => (current.Contains(c.Title) ? "✓ " : "    ") + c.Title).Append("New category").ToArray();
        var pick = await Shell.Current.DisplayActionSheetAsync("Categories", "Done", null, options);
        if (pick is null or "Done") return;
        if (pick == "New category")
        {
            var name = await Shell.Current.DisplayPromptAsync("New category", "Name", "Add", "Cancel");
            if (string.IsNullOrWhiteSpace(name)) return;
            current.Add(name.Trim());
        }
        else
        {
            var title = pick[4..].Trim();
            if (!current.Remove(title)) current.Add(title);
        }
        await SaveAsync(s => s.SetCategoriesAsync(_id, current.ToList()));
    }

    [RelayCommand]
    private async Task MoveAsync()
    {
        if (session.Service is not { } svc || _task is null) return;
        var lists = (await svc.ProjectsAsync()).Where(p => p.Id != _task.ProjectId).ToList();
        var pick = await Shell.Current.DisplayActionSheetAsync("Move to list", "Cancel", null, lists.Select(p => p.Title).ToArray());
        var target = lists.FirstOrDefault(p => p.Title == pick);
        if (target is not null) await SaveAsync(s => s.UpdateAsync(_id, project: target.Id.ToString(CultureInfo.InvariantCulture)));
    }

    [RelayCommand]
    private async Task AddFileAsync()
    {
        var file = await FilePicker.Default.PickAsync();
        if (file is null) return;
        await SaveAsync(s => s.Client.UploadAttachmentAsync(_id, file.FullPath));
    }

    [RelayCommand]
    private async Task OpenFileAsync(FileRow f)
    {
        if (session.Service is not { } svc) return;
        try
        {
            var path = Path.Combine(FileSystem.CacheDirectory, $"{f.Attachment.Id}_{Path.GetFileName(f.Name)}");
            await File.WriteAllBytesAsync(path, await svc.Client.DownloadAttachmentAsync(f.Attachment.TaskId, f.Attachment.Id));
            await Launcher.Default.OpenAsync(new OpenFileRequest(f.Name, new ReadOnlyFile(path)));
        }
        catch (Exception ex) when (ex is HttpRequestException or VikunjaException or IOException) { Error = ex.Message; }
    }

    [RelayCommand]
    private async Task DeleteAsync()
    {
        if (Prefs.ConfirmDelete && !await Shell.Current.DisplayAlertAsync("Delete task?", $"\"{TaskTitle}\" will be permanently deleted.", "Delete", "Cancel")) return;
        if (session.Service is not { } svc) return;
        try { await svc.DeleteTaskAsync(_id); await Shell.Current.GoToAsync(".."); }
        catch (Exception ex) when (ex is HttpRequestException or VikunjaException or DocketException) { Error = ex.Message; }
    }

    /// <summary>Native Android date (and time) pickers, shown in a small sheet page.</summary>
    private static async Task<DateTime?> PickDateTimeAsync(bool withTime)
    {
        var page = new Views.DateTimeSheet(withTime);
        await Shell.Current.Navigation.PushModalAsync(page);
        return await page.Result;
    }
}
