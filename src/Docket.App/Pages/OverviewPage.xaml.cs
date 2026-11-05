using System.Globalization;
using Docket.App.Controls;
using Docket.App.Services;
using Docket.Core;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using Microsoft.UI.Xaml.Shapes;

namespace Docket.App.Pages;

/// <summary>A dashboard: headline numbers, a 14-day completion chart, priorities, what's due, and progress per list.</summary>
public sealed partial class OverviewPage : Page, IRefreshable
{
    public OverviewPage() => InitializeComponent();

    private static DocketService? Svc => App.Service;
    private static MainWindow? Win => App.Window;
    private static readonly CultureInfo C = CultureInfo.CurrentCulture;

    protected override async void OnNavigatedTo(NavigationEventArgs e) => await RefreshAsync();

    private async void Refresh_Click(object sender, RoutedEventArgs e) => await RefreshAsync();

    public async Task RefreshAsync()
    {
        if (Svc is not { } svc || Win is null) return;
        Spinner.IsActive = Sections.Children.Count == 0;
        Spinner.Visibility = Spinner.IsActive ? Visibility.Visible : Visibility.Collapsed;
        await Win.RunAsync("Couldn't load the overview", async () =>
        {
            var o = await svc.OverviewAsync();
            var me = await svc.MeAsync();
            var hour = DateTime.Now.Hour;
            var part = hour < 12 ? "Good morning" : hour < 17 ? "Good afternoon" : "Good evening";
            var name = DocketService.DisplayName(me);
            Greeting.Text = $"{part}, {char.ToUpper(name[0], C)}{name[1..]}";
            DateText.Text = DateTimeOffset.Now.ToString("dddd, d MMMM", C) + (o.DueToday + o.Overdue > 0
                ? $"  ·  {o.DueToday} due today{(o.Overdue > 0 ? $", {o.Overdue} overdue" : "")}" : "  ·  Nothing due today");
            Build(o);
            await Win.RefreshCountsAsync();
        });
        Spinner.IsActive = false;
        Spinner.Visibility = Visibility.Collapsed;
    }

    private void Build(Overview o)
    {
        Sections.Children.Clear();

        // ---- stat cards ----
        var stats = new Grid { ColumnSpacing = 12 };
        (string Glyph, string Hex, int Value, string Label, ListKey Key)[] cards =
        [
            ("", "2564cf", o.Open, "Open", ListKey.Of(ExtraView.Open)),
            ("", "0e8a7e", o.DueToday, "Due today", ListKey.Of(SmartList.Planned)),
            ("", "d13438", o.Overdue, "Overdue", ListKey.Of(ExtraView.Upcoming)),
            ("", "7a4fd6", o.Next7Days, "Next 7 days", ListKey.Of(ExtraView.Upcoming)),
            ("", "2f8f46", o.DoneThisWeek, "Done this week", ListKey.Of(SmartList.Completed)),
        ];
        for (var i = 0; i < cards.Length; i++)
        {
            stats.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var card = StatCard(cards[i].Glyph, cards[i].Hex, cards[i].Value, cards[i].Label, cards[i].Key);
            Grid.SetColumn(card, i);
            stats.Children.Add(card);
        }
        Sections.Children.Add(stats);

        // ---- chart + priorities ----
        var row2 = new Grid { ColumnSpacing = 12 };
        row2.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(3, GridUnitType.Star) });
        row2.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2, GridUnitType.Star) });
        var chart = ChartCard(o);
        var prio = PriorityCard(o);
        Grid.SetColumn(prio, 1);
        row2.Children.Add(chart);
        row2.Children.Add(prio);
        Sections.Children.Add(row2);

        // ---- task lists ----
        var lists = new Grid { ColumnSpacing = 12, RowSpacing = 12 };
        lists.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        lists.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        lists.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        lists.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var cells = new[]
        {
            TaskCard("Today", "", "0e8a7e", o.TodayTasks, "Nothing due today.", ListKey.Of(SmartList.MyDay)),
            TaskCard("Overdue", "", "d13438", o.OverdueTasks, "Nothing overdue. Nice.", ListKey.Of(ExtraView.Upcoming)),
            TaskCard("Upcoming", "", "7a4fd6", o.UpcomingTasks, "Nothing due in the next week.", ListKey.Of(ExtraView.Upcoming)),
            TaskCard("Assigned to me", "", "2564cf", o.AssignedToMe, "Nothing is assigned to you.", ListKey.Of(ExtraView.Open)),
        };
        for (var i = 0; i < cells.Length; i++)
        {
            Grid.SetColumn(cells[i], i % 2);
            Grid.SetRow(cells[i], i / 2);
            lists.Children.Add(cells[i]);
        }
        Sections.Children.Add(lists);

        // ---- list progress ----
        if (o.Lists.Count > 0)
        {
            Sections.Children.Add(SectionTitle("Lists"));
            var wrap = new VariableSizedWrapGrid { Orientation = Orientation.Horizontal, ItemWidth = 292, ItemHeight = 132 };
            foreach (var l in o.Lists) wrap.Children.Add(ListCard(l));
            Sections.Children.Add(wrap);
        }
    }

    private static TextBlock SectionTitle(string text) => new() { Text = text, Style = Ui.TextStyle("SubtitleTextBlockStyle"), Margin = new Thickness(0, 8, 0, 0) };

    private static Border IconBadge(string glyph, string hex, double size = 36) => new()
    {
        Width = size, Height = size, CornerRadius = new CornerRadius(size / 2), Background = Ui.Hex(hex, Ui.IsDark ? 0.3 : 0.14),
        Child = Ui.Glyph(glyph, size * 0.44, Ui.Hex(Ui.Readable(hex))), HorizontalAlignment = HorizontalAlignment.Left,
    };

    private static Button CardButton(UIElement content, string name, Action click)
    {
        var b = new Button
        {
            Content = content, HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch, VerticalContentAlignment = VerticalAlignment.Stretch,
            Padding = new Thickness(16), CornerRadius = new CornerRadius(8),
            Background = Ui.Brush("CardBackgroundFillColorDefaultBrush"), BorderBrush = Ui.Brush("CardStrokeColorDefaultBrush"),
        };
        AutomationProperties.SetName(b, name);
        b.Click += (_, _) => click();
        return b;
    }

    private static Button StatCard(string glyph, string hex, int value, string label, ListKey key)
    {
        var p = new StackPanel { Spacing = 10 };
        p.Children.Add(IconBadge(glyph, hex));
        var n = new TextBlock { Text = value.ToString(C), FontSize = 30, FontWeight = FontWeights.SemiBold, LineHeight = 34 };
        if (label == "Overdue" && value > 0) n.Foreground = Ui.Hex(hex);
        p.Children.Add(n);
        p.Children.Add(new TextBlock { Text = label, Foreground = Ui.Brush("TextFillColorSecondaryBrush") });
        var b = CardButton(p, $"{label}: {value}", () => Win?.Show(key));
        ToolTipService.SetToolTip(b, $"Show {label.ToLower(C)}");
        return b;
    }

    private static Border ChartCard(Overview o)
    {
        var max = Math.Max(1, o.DonePerDay.Max(d => d.Done));
        var total = o.DonePerDay.Sum(d => d.Done);
        var head = new Grid();
        head.Children.Add(new TextBlock { Text = "Completed, last 14 days", Style = Ui.TextStyle("BodyStrongTextBlockStyle") });
        head.Children.Add(new TextBlock { Text = $"{total} tasks", HorizontalAlignment = HorizontalAlignment.Right, Foreground = Ui.Brush("TextFillColorSecondaryBrush") });
        const double plot = 132;
        var bars = new Grid { ColumnSpacing = 6, Height = plot + 40, Margin = new Thickness(0, 12, 0, 0) };
        var today = DateOnly.FromDateTime(DateTime.Today);
        for (var i = 0; i < o.DonePerDay.Count; i++)
        {
            var (day, done) = o.DonePerDay[i];
            bars.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var col = new Grid();
            col.RowDefinitions.Add(new RowDefinition { Height = new GridLength(plot + 18) });
            col.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var stack = new StackPanel { VerticalAlignment = VerticalAlignment.Bottom, Spacing = 4 };
            if (done > 0) stack.Children.Add(new TextBlock { Text = done.ToString(C), FontSize = 11, HorizontalAlignment = HorizontalAlignment.Center, Foreground = Ui.Brush("TextFillColorSecondaryBrush") });
            stack.Children.Add(new Rectangle
            {
                Height = done == 0 ? 3 : Math.Max(6, plot * done / max), RadiusX = 3, RadiusY = 3, MaxWidth = 34,
                Fill = done == 0 ? Ui.Brush("ControlStrongFillColorDisabledBrush") : day == today ? Ui.Brush("AccentFillColorDefaultBrush") : Ui.Brush("AccentFillColorSecondaryBrush"),
            });
            col.Children.Add(stack);
            var date = day.ToDateTime(TimeOnly.MinValue);
            var label = new TextBlock
            {
                Text = day == today ? "Today" : date.ToString(date.Day == 1 || i == 0 ? "d MMM" : "ddd", C), FontSize = 11, HorizontalAlignment = HorizontalAlignment.Center,
                Foreground = day == today ? Ui.Brush("AccentTextFillColorPrimaryBrush") : Ui.Brush("TextFillColorSecondaryBrush"), Margin = new Thickness(0, 6, 0, 0),
            };
            Grid.SetRow(label, 1);
            col.Children.Add(label);
            ToolTipService.SetToolTip(col, $"{date.ToString("dddd d MMMM", C)}: {done} completed");
            AutomationProperties.SetName(col, $"{date.ToString("d MMMM", C)}: {done} completed");
            Grid.SetColumn(col, i);
            bars.Children.Add(col);
        }
        var p = new StackPanel();
        p.Children.Add(head);
        p.Children.Add(bars);
        return Ui.Card(p, 20);
    }

    private static Border PriorityCard(Overview o)
    {
        var p = new StackPanel { Spacing = 12 };
        var head = new Grid();
        head.Children.Add(new TextBlock { Text = "Open by priority", Style = Ui.TextStyle("BodyStrongTextBlockStyle") });
        p.Children.Add(head);
        var max = Math.Max(1, o.ByPriority.Max());
        for (var level = 5; level >= 0; level--)
        {
            var n = o.ByPriority.ElementAtOrDefault(level);
            var row = new Grid { ColumnSpacing = 12 };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(70) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(28) });
            var name = new TextBlock { Text = DocketService.PriorityName(level), FontSize = 13, VerticalAlignment = VerticalAlignment.Center };
            var track = new Grid { Height = 10, CornerRadius = new CornerRadius(5), Background = Ui.Brush("SubtleFillColorSecondaryBrush"), VerticalAlignment = VerticalAlignment.Center };
            track.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(n, 0.0001), GridUnitType.Star) });
            track.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(max - n, 0.0001), GridUnitType.Star) });
            if (n > 0) track.Children.Add(new Border { CornerRadius = new CornerRadius(5), Background = Ui.Hex(Ui.PriorityHex(level), level == 0 ? 0.5 : 1) });
            var count = new TextBlock { Text = n.ToString(C), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center, Foreground = Ui.Brush("TextFillColorSecondaryBrush") };
            Grid.SetColumn(track, 1);
            Grid.SetColumn(count, 2);
            row.Children.Add(name);
            row.Children.Add(track);
            row.Children.Add(count);
            AutomationProperties.SetName(row, $"{DocketService.PriorityName(level)}: {n}");
            p.Children.Add(row);
        }
        return Ui.Card(p, 20);
    }

    private Border TaskCard(string title, string glyph, string hex, IReadOnlyList<TaskInfo> tasks, string empty, ListKey key)
    {
        var p = new StackPanel { Spacing = 2 };
        var head = new Grid { ColumnSpacing = 10, Margin = new Thickness(0, 0, 0, 8) };
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var badge = IconBadge(glyph, hex, 28);
        var t = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center };
        t.Children.Add(new TextBlock { Text = title, Style = Ui.TextStyle("BodyStrongTextBlockStyle") });
        t.Children.Add(new TextBlock { Text = tasks.Count.ToString(C), Foreground = Ui.Brush("TextFillColorSecondaryBrush") });
        var all = new HyperlinkButton { Content = "See all", Padding = new Thickness(6, 2, 6, 2) };
        all.Click += (_, _) => Win?.Show(key);
        Grid.SetColumn(t, 1);
        Grid.SetColumn(all, 2);
        head.Children.Add(badge);
        head.Children.Add(t);
        head.Children.Add(all);
        p.Children.Add(head);
        if (tasks.Count == 0) p.Children.Add(new TextBlock { Text = empty, Foreground = Ui.Brush("TextFillColorSecondaryBrush"), Margin = new Thickness(38, 4, 0, 8) });
        foreach (var info in tasks.Take(6)) p.Children.Add(TaskLine(info));
        if (tasks.Count > 6) p.Children.Add(new TextBlock { Text = $"and {tasks.Count - 6} more", Style = Ui.TextStyle("CaptionTextBlockStyle"), Foreground = Ui.Brush("TextFillColorSecondaryBrush"), Margin = new Thickness(38, 4, 0, 0) });
        var card = Ui.Card(p, 16);
        card.VerticalAlignment = VerticalAlignment.Stretch;
        return card;
    }

    private FrameworkElement TaskLine(TaskInfo info)
    {
        var item = new TaskItem(info.Task, info.Project, true);
        var g = new Grid { ColumnSpacing = 10, Padding = new Thickness(4, 6, 8, 6) };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var box = new CheckBox { MinWidth = 0, Padding = new Thickness(0), VerticalAlignment = VerticalAlignment.Center };
        AutomationProperties.SetName(box, $"Complete {info.Task.Title}");
        box.Click += async (_, _) =>
        {
            if (Svc is not { } svc || Win is null) return;
            await Win.RunAsync("Couldn't complete the task", async () =>
            {
                await svc.SetDoneAsync(info.Id, true);
                Win.OfferUndo($"Completed \"{info.Task.Title}\"", () => svc.SetDoneAsync(info.Id, false));
                await RefreshAsync();
            });
        };
        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(new TextBlock { Text = info.Task.Title, TextTrimming = TextTrimming.CharacterEllipsis });
        var meta = new List<string> { info.Project };
        if (item.HasDue) meta.Add(item.DueText);
        var sub = new TextBlock { Text = string.Join("  ·  ", meta), Style = Ui.TextStyle("CaptionTextBlockStyle"), Foreground = item.IsOverdue ? Ui.Brush("DocketOverdueBrush") : Ui.Brush("TextFillColorSecondaryBrush"), TextTrimming = TextTrimming.CharacterEllipsis };
        text.Children.Add(sub);
        var right = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, VerticalAlignment = VerticalAlignment.Center };
        if (info.Task.Priority > 0) right.Children.Add(Ui.PriorityBadge(info.Task.Priority, compact: true));
        if (info.Task.Assignees is { Count: > 0 } a) right.Children.Add(Ui.AvatarStack(a, 20));
        Grid.SetColumn(text, 1);
        Grid.SetColumn(right, 2);
        g.Children.Add(box);
        g.Children.Add(text);
        g.Children.Add(right);
        var button = new Button
        {
            Content = g, Padding = new Thickness(0), HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent), BorderThickness = new Thickness(0), CornerRadius = new CornerRadius(4),
        };
        AutomationProperties.SetName(button, $"Open {info.Task.Title}");
        button.Click += async (_, _) => await OpenDetailAsync(info.Id);
        return button;
    }

    private static FrameworkElement ListCard(ListProgress l)
    {
        var hex = Ui.IsHex(l.Project.HexColor) ? l.Project.HexColor : Ui.ToHex(Icons.Accent);
        var p = new Grid { RowSpacing = 8 };
        p.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        p.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        p.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var head = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        var badge = new Border { Width = 32, Height = 32, CornerRadius = new CornerRadius(8), Background = Ui.Hex(hex, 0.15), Child = Icons.Element(l.Style.Icon, 16, Ui.Hex(hex)) };
        head.Children.Add(badge);
        var names = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        names.Children.Add(new TextBlock { Text = l.Project.Title, Style = Ui.TextStyle("BodyStrongTextBlockStyle"), TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 200 });
        names.Children.Add(new TextBlock
        {
            Text = $"{l.Open} open" + (l.Overdue > 0 ? $"  ·  {l.Overdue} overdue" : ""), Style = Ui.TextStyle("CaptionTextBlockStyle"),
            Foreground = l.Overdue > 0 ? Ui.Brush("DocketOverdueBrush") : Ui.Brush("TextFillColorSecondaryBrush"),
        });
        head.Children.Add(names);
        p.Children.Add(head);
        var total = l.Open + l.DoneThisWeek;
        var bar = new ProgressBar { Value = total == 0 ? 0 : 100.0 * l.DoneThisWeek / total, Foreground = Ui.Hex(hex), Height = 6, CornerRadius = new CornerRadius(3) };
        Grid.SetRow(bar, 1);
        bar.VerticalAlignment = VerticalAlignment.Bottom;
        p.Children.Add(bar);
        var foot = new TextBlock { Text = $"{l.DoneThisWeek} done this week", Style = Ui.TextStyle("CaptionTextBlockStyle"), Foreground = Ui.Brush("TextFillColorSecondaryBrush") };
        Grid.SetRow(foot, 2);
        p.Children.Add(foot);
        var b = CardButton(p, $"{l.Project.Title}: {l.Open} open, {l.DoneThisWeek} done this week", () => Win?.SelectList(l.Project.Id));
        b.Margin = new Thickness(0, 0, 12, 12);
        return b;
    }

    // ---------- details ----------
    private async Task OpenDetailAsync(long id)
    {
        DetailColumn.Width = new GridLength(400);
        Detail.Visibility = Visibility.Visible;
        await Detail.LoadAsync(id);
    }

    private void CloseDetail()
    {
        Detail.Visibility = Visibility.Collapsed;
        DetailColumn.Width = new GridLength(0);
        Detail.Clear();
    }

    private void Detail_Closed(object? sender, EventArgs e) => CloseDetail();

    private async void Detail_Changed(object? sender, TaskChange change)
    {
        if (change == TaskChange.Deleted) CloseDetail();
        await RefreshAsync();
    }

    private async void Detail_OpenTask(object? sender, long id) => await OpenDetailAsync(id);
}
