using Docket.Core;
using Docket.Mobile.Services;
using Microsoft.Maui.Controls.Shapes;

namespace Docket.Mobile.Views;

/// <summary>
/// A bottom sheet: a transparent modal page with a dimmed scrim and a card that slides up.
/// Tapping the scrim or Back closes it with the default result.
/// </summary>
public abstract class Sheet<T> : ContentPage
{
    private readonly TaskCompletionSource<T?> _tcs = new();
    private readonly Border _card;
    private bool _closing;

    protected Sheet(string? title = null, double maxHeightFraction = 0.85)
    {
        BackgroundColor = Color.FromArgb("#66000000");
        Shell.SetPresentationMode(this, PresentationMode.ModalNotAnimated);
        var body = new VerticalStackLayout { Spacing = 8 };
        if (title is not null) body.Add(U.Text(title, 20, true));
        Body = body;
        var grab = new BoxView { WidthRequest = 36, HeightRequest = 4, CornerRadius = 2, HorizontalOptions = LayoutOptions.Center, Margin = new Thickness(0, 0, 0, 8) }.Themed(BoxView.ColorProperty, Th.Stroke);
        _card = new Border
        {
            StrokeThickness = 0, Padding = new Thickness(16, 10, 16, 20), VerticalOptions = LayoutOptions.End,
            StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(14, 14, 0, 0) },
            MaximumHeightRequest = DeviceDisplay.Current.MainDisplayInfo.Height / DeviceDisplay.Current.MainDisplayInfo.Density * maxHeightFraction,
        }.Themed(VisualElement.BackgroundColorProperty, Th.Card);
        var layout = new Grid { RowDefinitions = [new(GridLength.Auto), new(GridLength.Auto)], Children = { grab } };
        var scroll = new ScrollView { Content = body, MaximumHeightRequest = _card.MaximumHeightRequest - 50 };
        Grid.SetRow(scroll, 1);
        layout.Add(scroll);
        _card.Content = layout;
        var scrim = new BoxView { Color = Colors.Transparent }.Tap(() => Close(default));
        Content = new Grid { Children = { scrim, _card } };
    }

    protected VerticalStackLayout Body { get; }

    public Task<T?> Result => _tcs.Task;

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        _card.TranslationY = 500;
        await _card.TranslateToAsync(0, 0, 180, Easing.CubicOut);
    }

    protected async void Close(T? result)
    {
        if (_closing) return;
        _closing = true;
        // Hand back the result only once the sheet is gone, so the caller can navigate straight away.
        try
        {
            await _card.TranslateToAsync(0, 500, 140, Easing.CubicIn);
            SheetState.ClosedAt = DateTime.UtcNow;
            if (Navigation.ModalStack.Contains(this)) await Navigation.PopModalAsync(false);
        }
        finally { _tcs.TrySetResult(result); }
    }

    protected override bool OnBackButtonPressed() { Close(default); return true; }

    /// <summary>Shows a sheet over the current page and waits for its result.</summary>
    public static async Task<T?> ShowAsync(Sheet<T> sheet)
    {
        await Shell.Current.Navigation.PushModalAsync(sheet, false);
        return await sheet.Result;
    }
}

/// <summary>Lets pages tell a sheet closing (which makes them appear again) from real navigation, so they don't reload for nothing.</summary>
public static class SheetState
{
    public static DateTime ClosedAt { get; set; }
    public static bool JustClosed => DateTime.UtcNow - ClosedAt < TimeSpan.FromSeconds(1.5);
}

/// <summary>One option in a <see cref="ChoiceSheet"/>.</summary>
public sealed record Choice(string Text, string? Glyph = null, string? Detail = null, bool Selected = false, bool Danger = false, string? Emoji = null, string? Section = null);

/// <summary>A list of options (a nicer action sheet). Returns the index picked, or -1.</summary>
public sealed class ChoiceSheet : Sheet<int?>
{
    public ChoiceSheet(string title, IReadOnlyList<Choice> choices, string? note = null) : base(title)
    {
        if (note is not null) Body.Add(U.Caption(note, 13));
        for (var i = 0; i < choices.Count; i++)
        {
            var c = choices[i];
            var index = i;
            if (c.Section is not null) { var sec = U.Caption(c.Section, 13); sec.Margin = new Thickness(0, i == 0 ? 0 : 10, 0, 0); Body.Add(sec); }
            var color = c.Danger ? Th.Danger : c.Selected ? Th.Accent : Th.Text;
            var g = new Grid { ColumnDefinitions = [new(40), new(GridLength.Star), new(GridLength.Auto)], MinimumHeightRequest = 48 };
            if (c.Emoji is not null) g.Add(new Label { Text = c.Emoji, FontSize = 18, HorizontalTextAlignment = TextAlignment.Center, VerticalTextAlignment = TextAlignment.Center }, 0);
            else if (c.Glyph is not null) g.Add(U.Glyph(c.Glyph, 20, color), 0);
            var texts = new VerticalStackLayout { VerticalOptions = LayoutOptions.Center, Spacing = 0, Padding = new Thickness(0, 6) };
            texts.Add(U.Text(c.Text, 15, c.Selected, color));
            if (c.Detail is not null) texts.Add(U.Caption(c.Detail));
            g.Add(texts, 1);
            if (c.Selected) g.Add(U.Glyph(IconChars.Check, 18, Th.Accent), 2);
            g.Tap(() => Close(index));
            Body.Add(g);
        }
    }

    public static async Task<int> PickAsync(string title, IReadOnlyList<Choice> choices, string? note = null) =>
        await ShowAsync(new ChoiceSheet(title, choices, note)) ?? -1;
}

/// <summary>Colour swatches. Returns "#RRGGBB", "" for none, or null when closed.</summary>
public sealed class ColorSheet : Sheet<string>
{
    public ColorSheet(string title, string? current, bool allowNone = true) : base(title)
    {
        var flex = new FlexLayout { Wrap = Microsoft.Maui.Layouts.FlexWrap.Wrap, JustifyContent = Microsoft.Maui.Layouts.FlexJustify.Start };
        foreach (var (name, hex) in Icons.Colors)
        {
            var selected = string.Equals(current?.TrimStart('#'), hex.TrimStart('#'), StringComparison.OrdinalIgnoreCase);
            var dot = new Border
            {
                WidthRequest = 44, HeightRequest = 44, Margin = 6, BackgroundColor = Color.FromArgb(hex),
                StrokeShape = new RoundRectangle { CornerRadius = 22 }, StrokeThickness = selected ? 3 : 0,
                Content = selected ? new Label { Text = IconChars.Check, FontFamily = U.Icons, FontSize = 20, TextColor = Colors.White, HorizontalTextAlignment = TextAlignment.Center, VerticalTextAlignment = TextAlignment.Center } : null,
            }.Themed(Border.StrokeProperty, Th.Text);
            SemanticProperties.SetDescription(dot, name);
            flex.Add(dot.Tap(() => Close(hex)));
        }
        Body.Add(flex);
        if (allowNone) Body.Add(U.Row(IconChars.Dismiss, "No colour", () => { Close(""); return Task.CompletedTask; }));
    }
}

/// <summary>Fluent icons and emoji. Returns the icon name or emoji, "" for none, or null when closed.</summary>
public sealed class IconSheet : Sheet<string>
{
    public IconSheet(string title, string? current) : base(title)
    {
        Body.Add(U.Section("Icons"));
        var glyphs = new FlexLayout { Wrap = Microsoft.Maui.Layouts.FlexWrap.Wrap };
        foreach (var (name, glyph) in Icons.Catalog)
        {
            var sel = string.Equals(current, name, StringComparison.OrdinalIgnoreCase);
            var cell = new Border { WidthRequest = 44, HeightRequest = 44, Margin = 2, StrokeThickness = 0, StrokeShape = new RoundRectangle { CornerRadius = 8 }, Content = U.Glyph(glyph, 22, sel ? Th.OnAccent : Th.Text) };
            cell.Themed(VisualElement.BackgroundColorProperty, sel ? Th.Accent : Th.Subtle);
            SemanticProperties.SetDescription(cell, name);
            glyphs.Add(cell.Tap(() => Close(name)));
        }
        Body.Add(glyphs);
        Body.Add(U.Section("Emoji"));
        var emoji = new FlexLayout { Wrap = Microsoft.Maui.Layouts.FlexWrap.Wrap };
        foreach (var e in Icons.Emoji)
        {
            var cell = new Border { WidthRequest = 44, HeightRequest = 44, Margin = 2, StrokeThickness = 0, StrokeShape = new RoundRectangle { CornerRadius = 8 }, Content = new Label { Text = e, FontSize = 22, HorizontalTextAlignment = TextAlignment.Center, VerticalTextAlignment = TextAlignment.Center } };
            cell.Themed(VisualElement.BackgroundColorProperty, e == current ? Th.AccentTint : Th.Subtle);
            emoji.Add(cell.Tap(() => Close(e)));
        }
        Body.Add(emoji);
        var own = U.Entry("Or type any emoji");
        own.ReturnType = ReturnType.Done;
        own.Completed += (_, _) => { if (!string.IsNullOrWhiteSpace(own.Text)) Close(own.Text.Trim()); };
        Body.Add(U.Field(own));
        Body.Add(U.Row(IconChars.Dismiss, "No icon", () => { Close(""); return Task.CompletedTask; }));
    }
}

/// <summary>Search open tasks and pick one (for parents and existing subtasks).</summary>
public sealed class TaskPickerSheet : Sheet<TaskInfo>
{
    private readonly DocketService _svc;
    private readonly HashSet<long> _exclude;
    private readonly VerticalStackLayout _results = new() { Spacing = 0 };
    private CancellationTokenSource? _cts;

    public TaskPickerSheet(DocketService svc, string title, IEnumerable<long> exclude) : base(title)
    {
        _svc = svc;
        _exclude = exclude.ToHashSet();
        var search = U.Entry("Search tasks");
        search.ReturnType = ReturnType.Search;
        search.TextChanged += async (_, e) => await LoadAsync(e.NewTextValue);
        Body.Add(U.Field(new Grid { ColumnDefinitions = [new(GridLength.Auto), new(GridLength.Star)], Children = { U.Glyph(IconChars.Search, 16, Th.Text2), WithColumn(search, 1) } }));
        Body.Add(_results);
        _ = LoadAsync("");
    }

    private static View WithColumn(View v, int col) { Grid.SetColumn(v, col); v.Margin = new Thickness(6, 0, 0, 0); return v; }

    private async Task LoadAsync(string? q)
    {
        _cts?.Cancel();
        var cts = _cts = new CancellationTokenSource();
        try
        {
            await Task.Delay(250, cts.Token);
            var found = await _svc.ListAsync(TaskView.Open, search: string.IsNullOrWhiteSpace(q) ? null : q.Trim(), limit: 40, ct: cts.Token);
            if (cts.IsCancellationRequested) return;
            _results.Clear();
            foreach (var t in found.Where(t => !_exclude.Contains(t.Id)).Take(30))
            {
                var row = new VerticalStackLayout { Padding = new Thickness(4, 8), Spacing = 0 };
                row.Add(U.Text(t.Task.Title));
                row.Add(U.Caption(t.Project));
                _results.Add(row.Tap(() => Close(t)));
                _results.Add(U.Divider());
            }
            if (_results.Count == 0) _results.Add(U.Caption("No open tasks match.", 14));
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) when (U.IsExpected(ex)) { _results.Clear(); _results.Add(U.Caption(U.Friendly(ex), 14)); }
    }
}

/// <summary>Lists the tasks a slash command will change and asks to go ahead.</summary>
public sealed class ConfirmTasksSheet : Sheet<bool>
{
    public ConfirmTasksSheet(string title, string verb, IReadOnlyList<TaskInfo> tasks) : base(title)
    {
        Body.Add(U.Caption(tasks.Count == 1 ? "This task matches:" : $"These {tasks.Count} tasks match:", 14));
        foreach (var t in tasks.Take(50))
        {
            var g = new Grid { ColumnDefinitions = [new(32), new(GridLength.Star)], Padding = new Thickness(0, 4) };
            g.Add(U.Glyph(IconChars.Circle, 18, Th.Text2), 0);
            var col = new VerticalStackLayout();
            col.Add(U.Text(t.Task.Title));
            col.Add(U.Caption(t.Project));
            g.Add(col, 1);
            Body.Add(g);
        }
        var buttons = new HorizontalStackLayout { Spacing = 8, Margin = new Thickness(0, 8, 0, 0) };
        buttons.Add(U.Accent(verb, () => { Close(true); return Task.CompletedTask; }));
        buttons.Add(U.Plain("Cancel", () => { Close(false); return Task.CompletedTask; }));
        Body.Add(buttons);
    }
}

/// <summary>Native Android date (and time) pickers in a sheet. Returns the local date-time, or null.</summary>
public sealed class DateTimeSheet : Sheet<DateTime?>
{
    public DateTimeSheet(bool withTime, DateTime? initial = null, string? title = null) : base(title ?? (withTime ? "Pick a date and time" : "Pick a date"), 0.6)
    {
        var start = initial ?? DateTime.Today.AddDays(1).AddHours(9);
        var date = new DatePicker { Date = start.Date, MinimumDate = DateTime.Today.AddYears(-2), FontFamily = "Selawik" }.Themed(DatePicker.TextColorProperty, Th.Text);
        var time = new TimePicker { Time = withTime ? start.TimeOfDay : new TimeSpan(9, 0, 0), IsVisible = withTime, FontFamily = "Selawik" }.Themed(TimePicker.TextColorProperty, Th.Text);
        Body.Add(U.Field(date));
        if (withTime) Body.Add(U.Field(time));
        var buttons = new HorizontalStackLayout { Spacing = 8, Margin = new Thickness(0, 8, 0, 0) };
        buttons.Add(U.Accent("Save", () =>
        {
            var d = date.Date ?? DateTime.Today;
            var t = withTime ? time.Time ?? TimeSpan.FromHours(9) : TimeSpan.FromHours(18);
            Close(d.Date + t);
            return Task.CompletedTask;
        }));
        buttons.Add(U.Plain("Cancel", () => { Close(null); return Task.CompletedTask; }));
        Body.Add(buttons);
    }
}
