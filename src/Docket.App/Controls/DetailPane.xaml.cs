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

/// <summary>The right-hand task details: title, steps, My Day, reminder, due, repeat, categories, files, list, notes.</summary>
public sealed partial class DetailPane : UserControl
{
    private VTask? _task;
    private TaskNotes _notes = new("", []);
    private bool _loading;

    public DetailPane() => InitializeComponent();

    public long? TaskId => _task?.Id;
    public event EventHandler? Closed;
    public event EventHandler<TaskChange>? Changed;

    private static DocketService? Svc => App.Service;
    private static MainWindow? Win => App.Window;
    private static readonly CultureInfo Ui = CultureInfo.CurrentCulture;

    public void Clear() => _task = null;

    public async Task LoadAsync(long id)
    {
        if (Svc is not { } svc || Win is null) return;
        Busy.IsActive = true;
        await Win.RunAsync("Couldn't open the task", async () =>
        {
            _task = await svc.GetAsync(id);
            _task.Attachments = await svc.Client.GetAttachmentsAsync(id);
            Render();
        });
        Busy.IsActive = false;
    }

    private void Render()
    {
        if (_task is not { } t) return;
        _loading = true;
        _notes = TaskNotes.Parse(t.Description);
        DoneBox.IsChecked = t.Done;
        TitleBox.Text = t.Title;
        StarIcon.Glyph = t.IsFavorite ? "" : "";
        AutomationProperties.SetName(StarButton, t.IsFavorite ? "Remove importance" : "Mark as important");

        RenderSteps();

        var inMyDay = DocketService.InMyDay(t);
        MyDayText.Text = inMyDay ? "Added to My Day (click to remove)" : "Add to My Day";

        var next = t.NextReminder(DateTimeOffset.Now);
        RemindText.Text = next is { } r ? $"Remind me at {r.ToLocalTime().ToString("t", Ui)}" : "Remind me";
        RemindSub.Text = next is { } r2 ? r2.ToLocalTime().ToString("dddd, d MMMM", Ui) : "";
        RemindSub.Visibility = next is null ? Visibility.Collapsed : Visibility.Visible;

        DueText.Text = t.Due is { } d ? $"Due {d.ToLocalTime().ToString("ddd, d MMMM", Ui)}" : "Add due date";
        if (t.IsOverdue(DateTimeOffset.Now)) DueText.Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["DocketOverdueBrush"];
        else DueText.ClearValue(TextBlock.ForegroundProperty);
        var rule = RepeatRule.From(t);
        RepeatText.Text = rule is null ? "Repeat" : rule.Describe();

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
        CreatedText.Text = t.Done && VDates.OrNull(t.DoneAt) is { } da
            ? $"Completed {da.ToLocalTime().ToString("ddd, d MMMM", Ui)}"
            : $"Created {t.Created.ToLocalTime().ToString("ddd, d MMMM", Ui)}";
        _loading = false;
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
        Busy.IsActive = true;
        await Win.RunAsync($"Couldn't change the {what}", async () =>
        {
            await action(svc, id);
            await LoadAsync(id);
            Changed?.Invoke(this, TaskChange.Updated);
        });
        Busy.IsActive = false;
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
        if (e.Key == VirtualKey.Enter) { e.Handled = true; NewStepBox.Focus(FocusState.Programmatic); }
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

    // ---------- steps ----------
    private void RenderSteps()
    {
        StepsPanel.Children.Clear();
        for (var i = 0; i < _notes.Steps.Count; i++)
        {
            var index = i;
            var step = _notes.Steps[i];
            var row = new Grid { ColumnSpacing = 4 };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var box = new CheckBox { IsChecked = step.Done, MinWidth = 0, VerticalAlignment = VerticalAlignment.Center };
            AutomationProperties.SetName(box, $"Step {index + 1} done");
            box.Click += async (_, _) => await SaveAsync("step", (s, id) => s.SetStepDoneAsync(id, index, box.IsChecked == true));
            var text = new TextBox { Text = step.Title, BorderThickness = new Thickness(0), Background = null, TextWrapping = TextWrapping.Wrap, Opacity = step.Done ? 0.6 : 1 };
            AutomationProperties.SetName(text, $"Step {index + 1}");
            text.LostFocus += async (_, _) =>
            {
                var v = text.Text.Trim();
                if (v.Length > 0 && v != step.Title)
                    await SaveAsync("step", (s, id) => s.SetNotesAndStepsAsync(id, n => n with { Steps = n.Steps.Select((x, j) => j == index ? x with { Title = v } : x).ToList() }));
            };
            var more = new Button { Content = new FontIcon { Glyph = "", FontSize = 12 }, Background = null, BorderThickness = new Thickness(0), Padding = new Thickness(6) };
            AutomationProperties.SetName(more, $"Step {index + 1} options");
            var menu = new MenuFlyout();
            var promote = new MenuFlyoutItem { Text = "Promote to task", Icon = new FontIcon { Glyph = "" } };
            promote.Click += async (_, _) => await SaveAsync("step", async (s, id) =>
            {
                await s.AddAsync(step.Title, _task!.ProjectId.ToString(CultureInfo.InvariantCulture));
                await s.RemoveStepAsync(id, index);
            });
            var remove = new MenuFlyoutItem { Text = "Delete step", Icon = new FontIcon { Glyph = "" } };
            remove.Click += async (_, _) => await SaveAsync("step", (s, id) => s.RemoveStepAsync(id, index));
            menu.Items.Add(promote);
            menu.Items.Add(remove);
            more.Flyout = menu;
            Grid.SetColumn(text, 1);
            Grid.SetColumn(more, 2);
            row.Children.Add(box);
            row.Children.Add(text);
            row.Children.Add(more);
            StepsPanel.Children.Add(row);
        }
    }

    private async void NewStep_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != VirtualKey.Enter || NewStepBox.Text.Trim().Length == 0) return;
        e.Handled = true;
        var text = NewStepBox.Text.Trim();
        NewStepBox.Text = "";
        await SaveAsync("steps", (s, id) => s.AddStepAsync(id, text));
        NewStepBox.Focus(FocusState.Programmatic);
    }

    // ---------- reminder, due, repeat menus ----------
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
        var nextWeek = At(now.Date.AddDays(((int)DayOfWeek.Monday - (int)now.DayOfWeek + 7) % 7 is 0 ? 7 : ((int)DayOfWeek.Monday - (int)now.DayOfWeek + 7) % 7), 9);
        RemindMenu.Items.Clear();
        if (later.Date == now.Date) RemindMenu.Items.Add(Item("Later today", "", later.ToString("t", Ui), () => SetReminder(later)));
        RemindMenu.Items.Add(Item("Tomorrow", "", tomorrow.ToString("ddd t", Ui), () => SetReminder(tomorrow)));
        RemindMenu.Items.Add(Item("Next week", "", nextWeek.ToString("ddd t", Ui), () => SetReminder(nextWeek)));
        RemindMenu.Items.Add(new MenuFlyoutSeparator());
        RemindMenu.Items.Add(Item("Pick a date & time", "", null, async () =>
        {
            if (await Dialogs.PickDateTimeAsync(XamlRoot, "Remind me", _task?.NextReminder(now) ?? later, withTime: true) is { } at) await SetReminder(at);
        }));
        if (_task?.Reminders is { Count: > 0 })
        {
            RemindMenu.Items.Add(new MenuFlyoutSeparator());
            RemindMenu.Items.Add(Item("Remove reminder", "", null, () => SaveAsync("reminder", (s, id) => s.SetRemindersAsync(id, []))));
        }
    }

    private Task SetReminder(DateTimeOffset at) => SaveAsync("reminder", (s, id) => s.SetRemindersAsync(id, [at]));

    private static DateTimeOffset At(DateTime day, int hour) => new(day.Year, day.Month, day.Day, hour, 0, 0, DateTimeOffset.Now.Offset);
    private static DateTimeOffset RoundUp(DateTimeOffset t) => new DateTimeOffset(t.Year, t.Month, t.Day, t.Hour, 0, 0, t.Offset).AddHours(t.Minute > 0 ? 1 : 0);

    private void DueMenu_Opening(object sender, object e)
    {
        var now = DateTimeOffset.Now;
        DueMenu.Items.Clear();
        DueMenu.Items.Add(Item("Today", "", now.ToString("ddd", Ui), () => SetDue("today")));
        DueMenu.Items.Add(Item("Tomorrow", "", now.AddDays(1).ToString("ddd", Ui), () => SetDue("tomorrow")));
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

    private void RepeatMenu_Opening(object sender, object e)
    {
        RepeatMenu.Items.Clear();
        foreach (var (name, rule) in new[] { ("Daily", RepeatRule.Daily), ("Weekdays", RepeatRule.Weekdays), ("Weekly", RepeatRule.Weekly), ("Monthly", RepeatRule.Monthly), ("Yearly", RepeatRule.Yearly) })
            RepeatMenu.Items.Add(Item(name, "", null, () => SetRepeat(rule)));
        RepeatMenu.Items.Add(new MenuFlyoutSeparator());
        RepeatMenu.Items.Add(Item("Custom", "", null, CustomRepeatAsync));
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
                content.Children.Add(new Microsoft.UI.Xaml.Shapes.Ellipse { Width = 10, Height = 10, Fill = TaskItem.ColorBrush(cat.HexColor, 1), VerticalAlignment = VerticalAlignment.Center });
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
        await Win.RunAsync("Couldn't delete the task", async () =>
        {
            await svc.DeleteTaskAsync(id);
            Changed?.Invoke(this, TaskChange.Deleted);
        });
    }
}
