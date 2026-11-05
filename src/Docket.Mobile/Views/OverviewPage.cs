using System.Globalization;
using Docket.Core;
using Docket.Mobile.Services;
using Docket.Mobile.ViewModels;
using Microsoft.Maui.Controls.Shapes;
using Icons = Docket.Mobile.Services.Icons;
using Overview = Docket.Core.Overview;

namespace Docket.Mobile.Views;

/// <summary>The Overview dashboard: counts, the last 14 days, priorities, what's due and how each list is doing.</summary>
public sealed class OverviewPage : ContentPage
{
    private static readonly CultureInfo Ui = CultureInfo.CurrentCulture;
    private readonly Session _session;
    private readonly VerticalStackLayout _stack = new() { Padding = new Thickness(12, 4, 12, 24), Spacing = 10 };
    private readonly RefreshView _refresh;

    public OverviewPage(Session session)
    {
        _session = session;
        Title = "";
        _refresh = new RefreshView { Content = new ScrollView { Content = _stack } };
        _refresh.Command = new Command(async () => await LoadAsync());
        Content = _refresh;
    }

    private bool _appeared;

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (_appeared && SheetState.JustClosed) return;
        _appeared = true;
        await LoadAsync();
    }

    private bool _loading;

    private async Task LoadAsync()
    {
        // Setting IsRefreshing runs the refresh command again; one load at a time.
        if (_session.Service is not { } svc || _loading) return;
        _loading = true;
        _refresh.IsRefreshing = true;
        try { Render(await svc.OverviewAsync()); }
        catch (Exception ex) when (U.IsExpected(ex))
        {
            _stack.Clear();
            _stack.Add(U.Title("Overview"));
            _stack.Add(U.Text(U.Friendly(ex), 15, false, Th.Danger));
        }
        finally { _refresh.IsRefreshing = false; _loading = false; }
    }

    private void Render(Overview o)
    {
        _stack.Clear();
        _stack.Add(U.Title("Overview"));
        _stack.Add(U.Caption(DateTime.Now.ToString("dddd, d MMMM", Ui), 14));

        var stats = new Grid { ColumnDefinitions = [new(GridLength.Star), new(GridLength.Star), new(GridLength.Star)], RowDefinitions = [new(GridLength.Auto), new(GridLength.Auto)], ColumnSpacing = 8, RowSpacing = 8, Margin = new Thickness(0, 6, 0, 0) };
        stats.Add(Stat(o.Open, "Open", IconChars.TaskList, Th.Accent, "view:open"), 0, 0);
        stats.Add(Stat(o.DueToday, "Due today", IconChars.Calendar, Th.Accent, "view:upcoming"), 1, 0);
        stats.Add(Stat(o.Overdue, "Overdue", IconChars.Warning, o.Overdue > 0 ? Th.Danger : Th.Text2, "view:upcoming"), 2, 0);
        stats.Add(Stat(o.Next7Days, "Next 7 days", IconChars.CalendarNext, Th.Accent, "view:upcoming"), 0, 1);
        stats.Add(Stat(o.Important, "Important", IconChars.Star, Th.Accent, "smart:Important"), 1, 1);
        stats.Add(Stat(o.DoneThisWeek, "Done this week", IconChars.Done, Duo.Of("#107C10", "#6CCB5F"), "smart:Completed", o.DoneToday > 0 ? $"{o.DoneToday} today" : null), 2, 1);
        _stack.Add(stats);

        _stack.Add(DoneChart(o.DonePerDay));
        _stack.Add(PriorityCard(o.ByPriority));
        Section("Overdue", o.OverdueTasks, Th.Danger, "view:upcoming");
        Section("Today", o.TodayTasks, Th.Accent, "view:upcoming");
        Section("Upcoming", o.UpcomingTasks, Th.Accent, "view:upcoming");
        Section("Assigned to me", o.AssignedToMe, Th.Accent, null);
        if (o.Lists.Count > 0) _stack.Add(ListsCard(o.Lists));
    }

    private static Border Stat(int value, string label, string glyph, Duo color, string? key, string? extra = null)
    {
        var g = new Grid { RowDefinitions = [new(GridLength.Auto), new(GridLength.Auto), new(GridLength.Auto)] };
        var top = new Grid { ColumnDefinitions = [new(GridLength.Star), new(GridLength.Auto)] };
        top.Add(U.Text(value.ToString(Ui), 26, true, value > 0 ? color : Th.Text), 0);
        var icon = U.Glyph(glyph, 16, color);
        icon.VerticalOptions = LayoutOptions.Start;
        top.Add(icon, 1);
        g.Add(top, 0, 0);
        var l = U.Caption(label, 12);
        l.MaxLines = 1;
        l.LineBreakMode = LineBreakMode.TailTruncation;
        g.Add(l, 0, 1);
        if (extra is not null) g.Add(U.Caption(extra, 11), 0, 2);
        var card = U.Card(g, new Thickness(12, 8, 10, 10));
        if (key is not null) card.Tap(() => Shell.Current.GoToAsync($"list?key={Uri.EscapeDataString(key)}"));
        return card;
    }

    private static Border DoneChart(IReadOnlyList<(DateOnly Day, int Done)> perDay)
    {
        var max = Math.Max(1, perDay.Max(d => d.Done));
        var total = perDay.Sum(d => d.Done);
        var bars = new Grid { ColumnSpacing = 5, HeightRequest = 110 };
        var today = DateOnly.FromDateTime(DateTime.Today);
        for (var i = 0; i < perDay.Count; i++)
        {
            bars.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
            var (day, done) = perDay[i];
            var col = new Grid { RowDefinitions = [new(GridLength.Star), new(16), new(14)] };
            if (done > 0)
            {
                var n = U.Caption(done.ToString(Ui), 10);
                n.HorizontalTextAlignment = TextAlignment.Center;
                n.VerticalOptions = LayoutOptions.End;
                n.Margin = new Thickness(0, 0, 0, 56.0 * done / max + 2);
                col.Add(n, 0, 0);
            }
            var bar = new BoxView { VerticalOptions = LayoutOptions.End, HeightRequest = Math.Max(3, 56.0 * done / max), CornerRadius = new CornerRadius(3, 3, 0, 0), Opacity = day == today ? 1 : done > 0 ? 0.55 : 0.18 };
            bar.Themed(BoxView.ColorProperty, done > 0 ? Th.Accent : Th.Text2);
            col.Add(bar, 0, 0);
            var wd = U.Caption(day.ToString("ddd", Ui)[..1], 10);
            wd.HorizontalTextAlignment = TextAlignment.Center;
            if (day == today) wd.Themed(Label.TextColorProperty, Th.Accent);
            col.Add(wd, 0, 1);
            var dn = U.Caption(day.Day.ToString(Ui), 9);
            dn.HorizontalTextAlignment = TextAlignment.Center;
            col.Add(dn, 0, 2);
            bars.Add(col, i, 0);
        }
        var stack = new VerticalStackLayout { Spacing = 8 };
        var head = new Grid { ColumnDefinitions = [new(GridLength.Star), new(GridLength.Auto)] };
        head.Add(U.Text("Completed, last 14 days", 15, true), 0);
        head.Add(U.Caption($"{total} done", 13), 1);
        stack.Add(head);
        stack.Add(bars);
        return U.Card(stack);
    }

    private static Border PriorityCard(IReadOnlyList<int> byPriority)
    {
        var stack = new VerticalStackLayout { Spacing = 6 };
        stack.Add(U.Text("Open by priority", 15, true));
        var max = Math.Max(1, byPriority.Max());
        for (var p = 5; p >= 0; p--)
        {
            var count = byPriority.ElementAtOrDefault(p);
            var g = new Grid { ColumnDefinitions = [new(76), new(GridLength.Star), new(32)], HeightRequest = 22 };
            g.Add(U.Text(p == 0 ? "None" : DocketService.PriorityName(p), 13, false, p == 0 ? Th.Text2 : Th.Priority(p)), 0);
            var track = new Grid();
            track.Add(new BoxView { CornerRadius = 4, HeightRequest = 8, VerticalOptions = LayoutOptions.Center }.Themed(BoxView.ColorProperty, Th.Subtle));
            var fill = new BoxView { CornerRadius = 4, HeightRequest = 8, VerticalOptions = LayoutOptions.Center, HorizontalOptions = LayoutOptions.Start };
            fill.Themed(BoxView.ColorProperty, p == 0 ? Th.Text2 : Th.Priority(p));
            track.SizeChanged += (_, _) => fill.WidthRequest = count == 0 ? 0 : Math.Max(8, track.Width * count / max);
            track.Add(fill);
            g.Add(track, 1);
            var c = U.Caption(count.ToString(Ui), 13);
            c.HorizontalTextAlignment = TextAlignment.End;
            g.Add(c, 2);
            stack.Add(g);
        }
        return U.Card(stack);
    }

    private void Section(string title, IReadOnlyList<TaskInfo> tasks, Duo color, string? key)
    {
        if (tasks.Count == 0) return;
        var stack = new VerticalStackLayout { Spacing = 0 };
        var head = new Grid { ColumnDefinitions = [new(GridLength.Star), new(GridLength.Auto)], Margin = new Thickness(0, 0, 0, 4) };
        var h = new HorizontalStackLayout { Spacing = 8 };
        h.Add(U.Text(title, 15, true));
        h.Add(U.Badge(tasks.Count.ToString(Ui), color, 11));
        head.Add(h, 0);
        if (key is not null && tasks.Count > 5) head.Add(U.Text("See all", 13, true, Th.Accent).Tap(() => Shell.Current.GoToAsync($"list?key={Uri.EscapeDataString(key)}")), 1);
        stack.Add(head);
        var now = DateTimeOffset.Now;
        foreach (var t in tasks.Take(5))
        {
            var g = new Grid { ColumnDefinitions = [new(36), new(GridLength.Star), new(GridLength.Auto)], MinimumHeightRequest = 44 };
            var id = t.Id;
            g.Add(new FluentCheck { IsChecked = t.Task.Done, Scale = 0.85, Margin = new Thickness(-6, 0, 0, 0), Command = new Command(async () => await U.Guard(async () => { await _session.Service!.SetDoneAsync(id, true); await LoadAsync(); })) }, 0);
            var col = new VerticalStackLayout { VerticalOptions = LayoutOptions.Center, Padding = new Thickness(0, 4) };
            var tl = U.Text(t.Task.Title, 14);
            tl.MaxLines = 2;
            tl.LineBreakMode = LineBreakMode.TailTruncation;
            col.Add(tl);
            var meta = new List<string> { t.Project };
            if (t.Task.Due is { } d) meta.Add((t.Task.IsOverdue(now) ? "Overdue, " : "") + ViewModels.TaskRow.DayLabel(d.ToLocalTime().Date));
            col.Add(U.Caption(string.Join("  ·  ", meta), 12));
            g.Add(col, 1);
            if (t.Task.Priority > 0) g.Add(U.Badge(DocketService.PriorityName(t.Task.Priority), Th.Priority(t.Task.Priority), 11), 2);
            stack.Add(g.Tap(() => ListViewModel.OpenTaskAsync(id)));
        }
        _stack.Add(U.Card(stack));
    }

    private static Border ListsCard(IReadOnlyList<ListProgress> lists)
    {
        var stack = new VerticalStackLayout { Spacing = 10 };
        stack.Add(U.Text("Lists", 15, true));
        foreach (var l in lists)
        {
            var tint = Th.Hex(l.Project.HexColor, Th.Accent.Now);
            var g = new Grid { ColumnDefinitions = [new(32), new(GridLength.Star), new(GridLength.Auto)], RowDefinitions = [new(GridLength.Auto), new(10)], ColumnSpacing = 4 };
            var icon = U.ListIcon(l.Style.Icon, 18, new Duo(tint, tint));
            g.Add(icon, 0, 0);
            Grid.SetRowSpan(icon, 2);
            g.Add(U.Text(l.Project.Title, 14), 1, 0);
            var counts = new HorizontalStackLayout { Spacing = 8 };
            counts.Add(U.Caption($"{l.Open} open", 12));
            if (l.Overdue > 0) counts.Add(U.Text($"{l.Overdue} overdue", 12, false, Th.Danger));
            if (l.DoneThisWeek > 0) counts.Add(U.Text($"{l.DoneThisWeek} done", 12, false, Duo.Of("#107C10", "#6CCB5F")));
            g.Add(counts, 2, 0);
            var total = l.Open + l.DoneThisWeek;
            var bar = new ProgressBar { Progress = total == 0 ? 0 : (double)l.DoneThisWeek / total, ProgressColor = tint, VerticalOptions = LayoutOptions.Center };
            g.Add(bar, 1, 1);
            Grid.SetColumnSpan(bar, 2);
            var id = l.Project.Id;
            stack.Add(g.Tap(() => Shell.Current.GoToAsync($"list?key={Uri.EscapeDataString($"list:{id}")}")));
        }
        stack.Add(U.Caption("Bars show this week's completed tasks against what's still open.", 11));
        return U.Card(stack);
    }
}
