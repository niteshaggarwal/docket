using Docket.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace Docket.App.Services;

/// <summary>Small content dialogs: prompt for text, pick one of several, confirm, pick a task.</summary>
public static class Dialogs
{
    public static async Task<string?> PromptAsync(XamlRoot root, string title, string label, string initial = "", string primary = "Create")
    {
        var box = new TextBox { Header = label, Text = initial, SelectionStart = 0, SelectionLength = initial.Length, MinWidth = 320 };
        var d = new ContentDialog { XamlRoot = root, Title = title, Content = box, PrimaryButtonText = primary, CloseButtonText = "Cancel", DefaultButton = ContentDialogButton.Primary };
        box.Loaded += (_, _) => { box.Focus(FocusState.Programmatic); box.SelectAll(); };
        return await d.ShowAsync() == ContentDialogResult.Primary ? box.Text.Trim() : null;
    }

    public static async Task<string?> PickAsync(XamlRoot root, string title, List<string> options)
    {
        var list = new RadioButtons { ItemsSource = options, SelectedIndex = 0 };
        var d = new ContentDialog { XamlRoot = root, Title = title, Content = list, PrimaryButtonText = "OK", CloseButtonText = "Cancel", DefaultButton = ContentDialogButton.Primary };
        return await d.ShowAsync() == ContentDialogResult.Primary ? list.SelectedItem as string : null;
    }

    public static async Task<bool> ConfirmAsync(XamlRoot root, string title, string message, string primary = "Delete")
    {
        var d = new ContentDialog { XamlRoot = root, Title = title, Content = new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap }, PrimaryButtonText = primary, CloseButtonText = "Cancel", DefaultButton = ContentDialogButton.Close };
        return await d.ShowAsync() == ContentDialogResult.Primary;
    }

    /// <summary>A date and time picker in a dialog.</summary>
    public static async Task<DateTimeOffset?> PickDateTimeAsync(XamlRoot root, string title, DateTimeOffset initial, bool withTime)
    {
        var date = new CalendarDatePicker { Header = "Date", Date = initial, MinWidth = 240 };
        var time = new TimePicker { Header = "Time", Time = initial.TimeOfDay, MinuteIncrement = 5 };
        var panel = new StackPanel { Spacing = 12 };
        panel.Children.Add(date);
        if (withTime) panel.Children.Add(time);
        var d = new ContentDialog { XamlRoot = root, Title = title, Content = panel, PrimaryButtonText = "Save", CloseButtonText = "Cancel", DefaultButton = ContentDialogButton.Primary };
        if (await d.ShowAsync() != ContentDialogResult.Primary || date.Date is not { } picked) return null;
        var day = picked.Date;
        var t = withTime ? time.Time : new TimeSpan(18, 0, 0);
        return new DateTimeOffset(day.Year, day.Month, day.Day, t.Hours, t.Minutes, 0, DateTimeOffset.Now.Offset);
    }

    /// <summary>Search for a task and pick it (for "Make subtask of…" and "Add parent…").</summary>
    public static async Task<TaskInfo?> PickTaskAsync(XamlRoot root, string title, string primary, ICollection<long> exclude)
    {
        if (App.Service is not { } svc) return null;
        var search = new AutoSuggestBox { PlaceholderText = "Search tasks", QueryIcon = new SymbolIcon(Symbol.Find), MinWidth = 380 };
        AutomationProperties.SetName(search, "Search tasks");
        var list = new ListView { SelectionMode = ListViewSelectionMode.Single, Height = 300, Margin = new Thickness(0, 8, 0, 0) };
        var status = Ui.Text("", "CaptionTextBlockStyle", 0.65);
        var panel = new StackPanel { Spacing = 4, Width = 420 };
        panel.Children.Add(search);
        panel.Children.Add(list);
        panel.Children.Add(status);
        var d = new ContentDialog
        {
            XamlRoot = root, Title = title, Content = panel, PrimaryButtonText = primary, CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary, IsPrimaryButtonEnabled = false,
        };
        CancellationTokenSource? cts = null;
        async Task LoadAsync(string q)
        {
            cts?.Cancel();
            var my = cts = new CancellationTokenSource();
            try
            {
                await Task.Delay(200, my.Token);
                var found = q.Length == 0 ? await svc.ListAsync(TaskView.Open, limit: 60, ct: my.Token) : await svc.ListAsync(TaskView.Open, search: q, limit: 60, ct: my.Token);
                if (my.IsCancellationRequested) return;
                list.Items.Clear();
                foreach (var t in found.Where(t => !exclude.Contains(t.Id)))
                {
                    var row = new StackPanel { Padding = new Thickness(0, 6, 0, 6) };
                    row.Children.Add(Ui.Text(t.Task.Title, "BodyTextBlockStyle"));
                    row.Children.Add(Ui.Text(t.Project, "CaptionTextBlockStyle", 0.6));
                    var item = new ListViewItem { Content = row, Tag = t };
                    AutomationProperties.SetName(item, t.Task.Title);
                    list.Items.Add(item);
                }
                status.Text = list.Items.Count == 0 ? "No open tasks match." : "";
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) when (ex is HttpRequestException or VikunjaException or DocketException) { status.Text = ex.Message; }
        }
        search.TextChanged += async (s, _) => await LoadAsync(s.Text.Trim());
        list.SelectionChanged += (_, _) => d.IsPrimaryButtonEnabled = list.SelectedItem is not null;
        var doubleTapped = false;
        list.DoubleTapped += (_, _) => { if (list.SelectedItem is not null) { doubleTapped = true; d.Hide(); } };
        search.Loaded += (_, _) => search.Focus(FocusState.Programmatic);
        _ = LoadAsync("");
        var result = await d.ShowAsync();
        return (result == ContentDialogResult.Primary || doubleTapped) && list.SelectedItem is ListViewItem { Tag: TaskInfo pick } ? pick : null;
    }
}
