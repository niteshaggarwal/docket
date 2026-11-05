using System.Globalization;
using Docket.App.Pages;
using Docket.App.Services;
using Docket.Core;
using Microsoft.UI.Input;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;

namespace Docket.App.Controls;

/// <summary>
/// A list as a Gantt chart: each dated task is a bar from its start to its end (a due date alone is a one-day bar),
/// on a day or week timeline with a line for today. Drag a bar to move it, or its ends to change the dates.
/// Tasks without dates are listed on the side.
/// </summary>
public sealed partial class GanttView : UserControl, IListView
{
    private const double RowHeight = 40;
    private const double TitleWidth = 240;
    private const double HeaderHeight = 48;
    private static bool _weeks;

    private readonly Grid _titles = new();
    private readonly Canvas _canvas = new() { VerticalAlignment = VerticalAlignment.Top, HorizontalAlignment = HorizontalAlignment.Left };
    private readonly Canvas _headerCanvas = new() { Height = HeaderHeight };
    private readonly ScrollViewer _bodyScroll;
    private readonly ScrollViewer _headerScroll;
    private readonly StackPanel _unscheduled = new() { Spacing = 4 };
    private readonly TextBlock _unscheduledTitle = new();
    private readonly ToggleButton _dayButton = new() { Content = "Days" };
    private readonly ToggleButton _weekButton = new() { Content = "Weeks" };
    private List<(TaskInfo Info, DateTime Start, DateTime End, bool FromDue)> _rows = [];
    private DateTime _from;
    private int _days;
    private VProject? _project;
    private bool _scrolledToToday;

    public event EventHandler<long>? OpenTask;
    public event EventHandler? Changed;

    private double DayWidth => _weeks ? 18 : 44;
    private readonly TextBlock _month = new();

    /// <summary>Keeps the month of the first visible day pinned at the left of the header.</summary>
    private void UpdateMonth()
    {
        if (_days == 0) return;
        var first = _from.AddDays(Math.Floor(_bodyScroll.HorizontalOffset / DayWidth));
        var next = new DateTime(first.Year, first.Month, 1).AddMonths(1);
        _month.Text = first.ToString(first.Year == DateTime.Today.Year ? "MMMM" : "MMMM yyyy", CultureInfo.CurrentCulture);
        _month.Opacity = X(next) - _bodyScroll.HorizontalOffset < 110 ? 0 : 1;
    }

    public GanttView()
    {
        _bodyScroll = new ScrollViewer
        {
            HorizontalScrollMode = ScrollMode.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollMode = ScrollMode.Disabled, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled, Content = _canvas,
            VerticalAlignment = VerticalAlignment.Top, VerticalContentAlignment = VerticalAlignment.Top,
        };
        _headerScroll = new ScrollViewer
        {
            HorizontalScrollMode = ScrollMode.Disabled, HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden,
            VerticalScrollMode = ScrollMode.Disabled, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled, Content = _headerCanvas,
        };
        _bodyScroll.ViewChanged += (_, _) => { _headerScroll.ChangeView(_bodyScroll.HorizontalOffset, null, null, true); UpdateMonth(); };

        // toolbar
        _dayButton.Click += async (_, _) => { _weeks = false; await ReloadAsync(); };
        _weekButton.Click += async (_, _) => { _weeks = true; await ReloadAsync(); };
        var today = new Button { Content = "Today" };
        today.Click += (_, _) => ScrollToToday();
        var bar = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, Margin = new Thickness(0, 0, 0, 8) };
        bar.Children.Add(_dayButton);
        bar.Children.Add(_weekButton);
        bar.Children.Add(new Border { Width = 8 });
        bar.Children.Add(today);
        bar.Children.Add(new TextBlock { Text = "Drag a bar to move it, or drag its ends to change the dates.", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0), Style = Ui.TextStyle("CaptionTextBlockStyle"), Foreground = Ui.Brush("TextFillColorSecondaryBrush") });

        // chart: titles column + timeline, inside one vertical scroller
        var chartBody = new Grid();
        chartBody.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(TitleWidth) });
        chartBody.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        Grid.SetColumn(_bodyScroll, 1);
        chartBody.Children.Add(_titles);
        chartBody.Children.Add(_bodyScroll);
        var vertical = new ScrollViewer { Content = chartBody, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };

        var headerRow = new Grid { Height = HeaderHeight, BorderThickness = new Thickness(0, 0, 0, 1), BorderBrush = Ui.Brush("DividerStrokeColorDefaultBrush") };
        headerRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(TitleWidth) });
        headerRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var taskHead = new TextBlock { Text = "Task", Margin = new Thickness(16, 0, 0, 8), VerticalAlignment = VerticalAlignment.Bottom, FontWeight = FontWeights.SemiBold, FontSize = 12, Foreground = Ui.Brush("TextFillColorSecondaryBrush") };
        Grid.SetColumn(_headerScroll, 1);
        headerRow.Children.Add(taskHead);
        headerRow.Children.Add(_headerScroll);
        _month.Margin = new Thickness(6, 4, 0, 0);
        _month.VerticalAlignment = VerticalAlignment.Top;
        _month.HorizontalAlignment = HorizontalAlignment.Left;
        _month.FontWeight = FontWeights.SemiBold;
        _month.FontSize = 12;
        _month.IsHitTestVisible = false;
        Grid.SetColumn(_month, 1);
        headerRow.Children.Add(_month);

        var chart = new Grid();
        chart.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        chart.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        Grid.SetRow(vertical, 1);
        chart.Children.Add(headerRow);
        chart.Children.Add(vertical);
        var chartCard = new Border
        {
            Child = chart, CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(1), BorderBrush = Ui.Brush("CardStrokeColorDefaultBrush"),
            Background = Ui.Brush("CardBackgroundFillColorDefaultBrush"),
        };

        // unscheduled
        _unscheduledTitle.Style = Ui.TextStyle("BodyStrongTextBlockStyle");
        var side = new StackPanel { Spacing = 8 };
        side.Children.Add(_unscheduledTitle);
        side.Children.Add(new TextBlock { Text = "No dates yet. Open one to give it a start, end or due date.", Style = Ui.TextStyle("CaptionTextBlockStyle"), Foreground = Ui.Brush("TextFillColorSecondaryBrush"), TextWrapping = TextWrapping.Wrap });
        side.Children.Add(_unscheduled);
        var sideCard = Ui.Card(new ScrollViewer { Content = side }, 14);
        sideCard.Margin = new Thickness(12, 0, 0, 0);
        sideCard.VerticalAlignment = VerticalAlignment.Stretch;

        var main = new Grid();
        main.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        main.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        main.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        main.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(230) });
        Grid.SetColumnSpan(bar, 2);
        Grid.SetRow(chartCard, 1);
        Grid.SetRow(sideCard, 1);
        Grid.SetColumn(sideCard, 1);
        main.Children.Add(bar);
        main.Children.Add(chartCard);
        main.Children.Add(sideCard);
        main.Padding = new Thickness(0, 0, 0, 12);
        Content = main;
    }

    private static DocketService? Svc => App.Service;

    public async Task LoadAsync(VProject project, SortMode sort, bool prioritySort)
    {
        if (Svc is not { } svc) return;
        if (_project?.Id != project.Id) _scrolledToToday = false;
        _project = project;
        var content = await svc.ProjectListAsync(project.Id, SortMode.Custom);
        var all = content.Open.Concat(content.Completed.Take(50)).ToList();
        _rows = [];
        var undated = new List<TaskInfo>();
        foreach (var info in all)
        {
            var t = info.Task;
            DateTime? s = VDates.OrNull(t.StartDate)?.ToLocalTime().Date, e = VDates.OrNull(t.EndDate)?.ToLocalTime().Date;
            if (s is null && e is null)
            {
                if (t.Due is { } d) _rows.Add((info, d.ToLocalTime().Date, d.ToLocalTime().Date, true));
                else if (!t.Done) undated.Add(info);
                continue;
            }
            var start = s ?? e!.Value;
            var end = e ?? s!.Value;
            if (end < start) (start, end) = (end, start);
            _rows.Add((info, start, end, false));
        }
        _rows = _rows.OrderBy(r => r.Start).ThenBy(r => r.End).ToList();
        var today = DateTime.Today;
        _from = (_rows.Count > 0 ? new[] { _rows.Min(r => r.Start), today.AddDays(-7) }.Min() : today.AddDays(-7)).AddDays(-3);
        if (_weeks) _from = _from.AddDays(-(((int)_from.DayOfWeek + 6) % 7));
        var to = (_rows.Count > 0 ? new[] { _rows.Max(r => r.End), today.AddDays(30) }.Max() : today.AddDays(30)).AddDays(_weeks ? 28 : 10);
        _days = (int)(to - _from).TotalDays + 1;
        _dayButton.IsChecked = !_weeks;
        _weekButton.IsChecked = _weeks;
        Render(undated);
        UpdateMonth();
        if (!_scrolledToToday)
        {
            _scrolledToToday = true;
            _bodyScroll.UpdateLayout();
            ScrollToToday();
            DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, ScrollToToday);
        }
    }

    private Task ReloadAsync() => _project is null ? Task.CompletedTask : LoadAsync(_project, SortMode.Custom, false);

    private double X(DateTime day) => (day - _from).TotalDays * DayWidth;

    private void ScrollToToday() => _bodyScroll.ChangeView(Math.Max(0, X(DateTime.Today) - DayWidth * (_weeks ? 14 : 3)), null, null, true);

    private void Render(List<TaskInfo> undated)
    {
        var width = _days * DayWidth;
        var height = Math.Max(_rows.Count, 6) * RowHeight;
        _canvas.Children.Clear();
        _headerCanvas.Children.Clear();
        _titles.Children.Clear();
        _titles.RowDefinitions.Clear();
        _canvas.Width = _headerCanvas.Width = width;
        _canvas.Height = height;
        var c = CultureInfo.CurrentCulture;
        var stripe = Ui.Brush("SubtleFillColorTertiaryBrush");
        var divider = Ui.Brush("DividerStrokeColorDefaultBrush");
        var secondary = Ui.Brush("TextFillColorSecondaryBrush");

        // grid and header
        for (var i = 0; i < _days; i++)
        {
            var day = _from.AddDays(i);
            var x = i * DayWidth;
            var weekend = day.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday;
            if (!_weeks && weekend) AddRect(_canvas, x, 0, DayWidth, height, stripe);
            var weekStart = day.DayOfWeek == DayOfWeek.Monday;
            if (!_weeks || weekStart) AddRect(_canvas, x, 0, 1, height, divider);
            if (day.Day == 1)
            {
                var month = new TextBlock { Text = day.ToString(day.Year == DateTime.Today.Year ? "MMMM" : "MMMM yyyy", c), FontWeight = FontWeights.SemiBold, FontSize = 12 };
                Canvas.SetLeft(month, x + 6); Canvas.SetTop(month, 4);
                _headerCanvas.Children.Add(month);
                AddRect(_headerCanvas, x, 0, 1, HeaderHeight, divider);
            }
            if (!_weeks || weekStart)
            {
                var label = new TextBlock
                {
                    Text = _weeks ? day.ToString("d MMM", c) : day.ToString("ddd", c)[..1] + " " + day.Day.ToString(c),
                    FontSize = 11, Foreground = day == DateTime.Today ? Ui.Brush("AccentTextFillColorPrimaryBrush") : secondary,
                    FontWeight = day == DateTime.Today ? FontWeights.SemiBold : FontWeights.Normal,
                };
                Canvas.SetLeft(label, x + 4); Canvas.SetTop(label, 26);
                _headerCanvas.Children.Add(label);
            }
        }
        for (var r = 1; r <= Math.Max(_rows.Count, 6); r++) AddRect(_canvas, 0, r * RowHeight - 1, width, 1, divider);

        // today
        var tx = X(DateTime.Today) + DayWidth / 2;
        AddRect(_canvas, tx - 1, 0, 2, height, Ui.Brush("AccentFillColorDefaultBrush"));
        var todayDot = new Ellipse { Width = 8, Height = 8, Fill = Ui.Brush("AccentFillColorDefaultBrush") };
        Canvas.SetLeft(todayDot, tx - 4); Canvas.SetTop(todayDot, HeaderHeight - 6);
        _headerCanvas.Children.Add(todayDot);

        // rows
        var listHex = _project is { HexColor.Length: 6 } p ? p.HexColor : null;
        for (var i = 0; i < _rows.Count; i++)
        {
            var (info, start, end, fromDue) = _rows[i];
            _titles.RowDefinitions.Add(new RowDefinition { Height = new GridLength(RowHeight) });
            var title = new Grid { Padding = new Thickness(16, 0, 8, 0), BorderThickness = new Thickness(0, 0, 1, 1), BorderBrush = divider };
            var tt = new TextBlock { Text = info.Task.Title, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, Opacity = info.Task.Done ? 0.55 : 1 };
            title.Children.Add(tt);
            var id = info.Id;
            title.Tapped += (_, _) => OpenTask?.Invoke(this, id);
            ToolTipService.SetToolTip(title, info.Task.Title);
            Grid.SetRow(title, i);
            _titles.Children.Add(title);
            AddBar(info, start, end, fromDue, i, listHex);
        }
        for (var i = _rows.Count; i < 6; i++) _titles.RowDefinitions.Add(new RowDefinition { Height = new GridLength(RowHeight) });
        if (_rows.Count == 0)
        {
            var empty = new TextBlock { Text = "No tasks with dates yet. Give a task a start and end date (or a due date) to see it here.", TextWrapping = TextWrapping.Wrap, Opacity = 0.65, Margin = new Thickness(16, 12, 16, 0) };
            Grid.SetRowSpan(empty, 3);
            _titles.Children.Add(empty);
        }

        _unscheduledTitle.Text = $"Unscheduled  {undated.Count}";
        _unscheduled.Children.Clear();
        foreach (var u in undated)
        {
            var b = new Button { HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Left, Padding = new Thickness(10, 8, 10, 8) };
            b.Content = new TextBlock { Text = u.Task.Title, TextTrimming = TextTrimming.CharacterEllipsis };
            AutomationProperties.SetName(b, $"Open {u.Task.Title}");
            var uid = u.Id;
            b.Click += (_, _) => OpenTask?.Invoke(this, uid);
            _unscheduled.Children.Add(b);
        }
    }

    private static void AddRect(Canvas canvas, double x, double y, double w, double h, Brush fill)
    {
        var r = new Rectangle { Width = Math.Max(0, w), Height = Math.Max(0, h), Fill = fill, IsHitTestVisible = false };
        Canvas.SetLeft(r, x); Canvas.SetTop(r, y);
        canvas.Children.Add(r);
    }

    private enum DragMode { None, Move, Start, End }

    private void AddBar(TaskInfo info, DateTime start, DateTime end, bool fromDue, int row, string? listHex)
    {
        var t = info.Task;
        var hex = t.Priority >= 3 ? Ui.PriorityHex(t.Priority) : listHex ?? Ui.ToHex(Icons.Accent);
        var bar = new CursorGrid
        {
            Height = RowHeight - 14, CornerRadius = new CornerRadius(4), Background = Ui.Hex(hex, fromDue ? 0.35 : 0.9),
            BorderBrush = Ui.Hex(hex), BorderThickness = new Thickness(fromDue ? 1 : 0), Opacity = t.Done ? 0.5 : 1,
        };
        var label = new TextBlock
        {
            Text = t.Title, FontSize = 12, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 8, 0), TextTrimming = TextTrimming.CharacterEllipsis,
            Foreground = fromDue ? Ui.Brush("TextFillColorPrimaryBrush") : new SolidColorBrush(Microsoft.UI.Colors.White),
        };
        bar.Children.Add(label);
        var handleL = new Rectangle { Width = 6, HorizontalAlignment = HorizontalAlignment.Left, Fill = new SolidColorBrush(Microsoft.UI.Colors.Transparent) };
        var handleR = new Rectangle { Width = 6, HorizontalAlignment = HorizontalAlignment.Right, Fill = new SolidColorBrush(Microsoft.UI.Colors.Transparent) };
        bar.Children.Add(handleL);
        bar.Children.Add(handleR);
        var span = fromDue ? $"Due {start:d MMM}" : start == end ? start.ToString("ddd d MMM", CultureInfo.CurrentCulture) : $"{start:d MMM} – {end:d MMM}";
        ToolTipService.SetToolTip(bar, $"{t.Title}\n{span}");
        AutomationProperties.SetName(bar, $"{t.Title}, {span}");

        void Place(DateTime s, DateTime e)
        {
            Canvas.SetLeft(bar, X(s) + 2);
            bar.Width = Math.Max(DayWidth - 4, X(e) - X(s) + DayWidth - 4);
        }
        Place(start, end);
        Canvas.SetTop(bar, row * RowHeight + 7);
        _canvas.Children.Add(bar);

        var mode = DragMode.None;
        double downX = 0;
        var moved = false;
        DateTime s0 = start, e0 = end, sNow = start, eNow = end;
        var hand = InputSystemCursor.Create(InputSystemCursorShape.SizeAll);
        var resize = InputSystemCursor.Create(InputSystemCursorShape.SizeWestEast);

        bar.PointerMoved += (_, e) =>
        {
            var x = e.GetCurrentPoint(bar).Position.X;
            if (mode == DragMode.None)
            {
                ProtectedCursorFor(bar, x < 8 || x > bar.Width - 8 ? resize : hand);
                return;
            }
            var dx = e.GetCurrentPoint(_canvas).Position.X - downX;
            var days = (int)Math.Round(dx / DayWidth);
            if (Math.Abs(dx) > 4) moved = true;
            sNow = mode is DragMode.Move or DragMode.Start ? s0.AddDays(days) : s0;
            eNow = mode is DragMode.Move or DragMode.End ? e0.AddDays(days) : e0;
            if (eNow < sNow) { if (mode == DragMode.Start) sNow = eNow; else eNow = sNow; }
            Place(sNow, eNow);
        };
        bar.PointerPressed += (_, e) =>
        {
            var x = e.GetCurrentPoint(bar).Position.X;
            mode = x < 8 ? DragMode.Start : x > bar.Width - 8 ? DragMode.End : DragMode.Move;
            downX = e.GetCurrentPoint(_canvas).Position.X;
            moved = false;
            bar.CapturePointer(e.Pointer);
            e.Handled = true;
        };
        bar.PointerReleased += async (_, e) =>
        {
            var was = mode;
            mode = DragMode.None;
            bar.ReleasePointerCapture(e.Pointer);
            e.Handled = true;
            if (was == DragMode.None) return;
            if (!moved) { OpenTask?.Invoke(this, info.Id); return; }
            if (sNow == s0 && eNow == e0) return;
            await SaveSpanAsync(info, sNow, eNow);
        };
        bar.PointerCaptureLost += (_, _) => { if (mode != DragMode.None) { mode = DragMode.None; Place(s0, e0); } };
    }

    private static void ProtectedCursorFor(UIElement element, InputCursor cursor)
    {
        if (element is CursorGrid g) g.Cursor = cursor;
    }

    private async Task SaveSpanAsync(TaskInfo info, DateTime start, DateTime end)
    {
        if (Svc is not { } svc || App.Window is not { } win) return;
        var offset = DateTimeOffset.Now.Offset;
        var s = new DateTimeOffset(start.Year, start.Month, start.Day, 9, 0, 0, offset);
        var e = new DateTimeOffset(end.Year, end.Month, end.Day, 18, 0, 0, offset);
        var old = (info.Task.StartDate, info.Task.EndDate);
        await win.RunAsync("Couldn't change the dates", async () =>
        {
            await svc.SetSpanAsync(info.Id, s, e);
            win.SetStatus($"\"{info.Task.Title}\": {start:d MMM} – {end:d MMM}");
            win.OfferUndo("Dates changed", () => svc.SetSpanAsync(info.Id, old.StartDate, old.EndDate));
            await ReloadAsync();
            Changed?.Invoke(this, EventArgs.Empty);
        });
    }
}

/// <summary>A Grid whose pointer cursor can be set (UIElement.ProtectedCursor is only settable from a subclass).</summary>
public sealed partial class CursorGrid : Grid
{
    public InputCursor? Cursor { get => ProtectedCursor; set => ProtectedCursor = value; }
}
