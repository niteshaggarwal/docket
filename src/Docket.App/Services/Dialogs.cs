using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Docket.App.Services;

/// <summary>Small content dialogs: prompt for text, pick one of several, confirm.</summary>
public static class Dialogs
{
    public static async Task<string?> PromptAsync(XamlRoot root, string title, string label, string initial = "", string primary = "Create")
    {
        var box = new TextBox { Header = label, Text = initial, SelectionStart = 0, SelectionLength = initial.Length, MinWidth = 320 };
        var d = new ContentDialog { XamlRoot = root, Title = title, Content = box, PrimaryButtonText = primary, CloseButtonText = "Cancel", DefaultButton = ContentDialogButton.Primary };
        box.Loaded += (_, _) => { box.Focus(FocusState.Programmatic); box.SelectAll(); };
        return await d.ShowAsync() == ContentDialogResult.Primary ? box.Text.Trim() : null;
    }

    public static async Task<string?> PickAsync(XamlRoot root, string title, IReadOnlyList<string> options)
    {
        var list = new RadioButtons { ItemsSource = options, SelectedIndex = 0 };
        var d = new ContentDialog { XamlRoot = root, Title = title, Content = list, PrimaryButtonText = "OK", CloseButtonText = "Cancel", DefaultButton = ContentDialogButton.Primary };
        return await d.ShowAsync() == ContentDialogResult.Primary ? list.SelectedItem as string : null;
    }

    public static async Task<bool> ConfirmAsync(XamlRoot root, string title, string message, string primary = "Delete")
    {
        var d = new ContentDialog { XamlRoot = root, Title = title, Content = message, PrimaryButtonText = primary, CloseButtonText = "Cancel", DefaultButton = ContentDialogButton.Close };
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
}
