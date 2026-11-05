using System.Globalization;
using CommunityToolkit.Maui.Behaviors;
using Docket.Core;
using Docket.Mobile.Services;
using Docket.Mobile.ViewModels;
using Microsoft.Maui.Controls.Shapes;
using Icons = Docket.Mobile.Services.Icons;

namespace Docket.Mobile.Views;

/// <summary>The Table, Timeline (Gantt) and Board views of a list, drawn in code from the list's tasks.</summary>
public static class ListViews
{
    private static readonly CultureInfo Ui = CultureInfo.CurrentCulture;

    // ---------- table ----------
    public static View Table(ListViewModel vm)
    {
        var rows = vm.TableRows;
        var showList = vm.Project is null;
        var stack = new VerticalStackLayout { Spacing = 0 };

        var header = new Grid { ColumnDefinitions = [new(40), new(GridLength.Star), new(68), new(70)], HeightRequest = 40, Padding = new Thickness(0, 0, 8, 0) };
        header.Add(HeaderCell(vm, "Task", "title", TextAlignment.Start), 1);
        header.Add(HeaderCell(vm, "Due", "due", TextAlignment.End), 2);
        header.Add(HeaderCell(vm, "Priority", "priority", TextAlignment.End), 3);
        stack.Add(header);
        stack.Add(U.Divider());

        var now = DateTimeOffset.Now;
        foreach (var t in rows)
        {
            var task = t.Task;
            var g = new Grid { ColumnDefinitions = [new(40), new(GridLength.Star), new(68), new(70)], MinimumHeightRequest = 42, Padding = new Thickness(0, 0, 8, 0) };
            var check = new FluentCheck { IsChecked = task.Done, Scale = 0.85, Command = new Command(async () => await vm.ToggleDoneByIdAsync(task.Id, !task.Done)) };
            g.Add(check, 0);
            var titleCol = new VerticalStackLayout { VerticalOptions = LayoutOptions.Center, Padding = new Thickness(0, 6) };
            var title = U.Text(task.Title, 14);
            title.LineBreakMode = LineBreakMode.TailTruncation;
            title.MaxLines = 2;
            titleCol.Add(title);
            var sub = new List<string>();
            if (showList) sub.Add(t.Project);
            if (task.Parents.Count > 0) sub.Add("↳ " + task.Parents[0].Title);
            if (task.Subtasks.Count > 0) sub.Add($"{task.Subtasks.Count(s => s.Done)}/{task.Subtasks.Count} subtasks");
            if (sub.Count > 0) { var c = U.Caption(string.Join("  ·  ", sub), 11); c.LineBreakMode = LineBreakMode.TailTruncation; c.MaxLines = 1; titleCol.Add(c); }
            g.Add(titleCol, 1);
            var due = U.Caption(task.Due is { } d ? d.ToLocalTime().ToString(d.Year == now.Year ? "d MMM" : "d MMM yy", Ui) : "–", 13);
            due.HorizontalTextAlignment = TextAlignment.End;
            due.VerticalTextAlignment = TextAlignment.Center;
            if (task.IsOverdue(now)) due.Themed(Label.TextColorProperty, Th.Danger);
            g.Add(due, 2);
            if (task.Priority > 0)
            {
                var b = U.Badge(DocketService.PriorityName(task.Priority), Th.Priority(task.Priority), 11);
                b.HorizontalOptions = LayoutOptions.End;
                g.Add(b, 3);
            }
            else { var dash = U.Caption("–", 13); dash.HorizontalTextAlignment = TextAlignment.End; dash.VerticalTextAlignment = TextAlignment.Center; g.Add(dash, 3); }
            g.Tap(() => ListViewModel.OpenTaskAsync(task.Id));
            stack.Add(g);
            stack.Add(U.Divider(40));
        }
        if (rows.Count == 0) stack.Add(new Label { Text = "No open tasks.", Margin = new Thickness(40, 16), Opacity = 0.7 });
        var card = U.Card(stack, new Thickness(0, 0, 0, 4));
        card.Margin = new Thickness(12, 4, 12, 12);
        card.VerticalOptions = LayoutOptions.Start;
        return new ScrollView { Content = card };
    }

    private static View HeaderCell(ListViewModel vm, string text, string column, TextAlignment align)
    {
        var active = vm.TableSort == column;
        var row = new HorizontalStackLayout { Spacing = 4, VerticalOptions = LayoutOptions.Center, HorizontalOptions = align == TextAlignment.End ? LayoutOptions.End : LayoutOptions.Start };
        row.Add(U.Text(text, 12, true, active ? Th.Accent : Th.Text2));
        if (active) row.Add(U.Glyph(vm.TableDescending ^ (column == "priority") ? IconChars.ChevronDown : "", 10, Th.Accent));
        var cell = new ContentView { Content = row, Padding = new Thickness(0, 0, 0, 0) };
        SemanticProperties.SetDescription(cell, $"Sort by {text}");
        return cell.Tap(() => vm.SortTable(column));
    }

    // ---------- timeline ----------
    public static View Gantt(ListViewModel vm)
    {
        const double dayW = 32, rowH = 44, headH = 46, leftW = 128;
        var today = DateTime.Today;
        var spans = new List<(TaskInfo Info, DateTime Start, DateTime End, bool Point)>();
        var undated = new List<TaskInfo>();
        foreach (var t in vm.FlatOpen)
        {
            var s = VDates.OrNull(t.Task.StartDate)?.ToLocalTime().Date;
            var e = VDates.OrNull(t.Task.EndDate)?.ToLocalTime().Date ?? t.Task.Due?.ToLocalTime().Date;
            if (s is null && e is null) { undated.Add(t); continue; }
            var start = s ?? e!.Value;
            var end = e ?? s!.Value;
            if (end < start) (start, end) = (end, start);
            spans.Add((t, start, end, s is null));
        }
        spans = spans.OrderBy(x => x.Start).ThenBy(x => x.End).ToList();
        var first = spans.Select(x => x.Start).Append(today.AddDays(-2)).Min();
        var last = spans.Select(x => x.End).Append(today.AddDays(13)).Max();
        if ((last - first).TotalDays > 120) last = first.AddDays(120);
        var days = (int)(last - first).TotalDays + 1;
        var tint = vm.Tint ?? Th.Accent.Now;

        var titles = new VerticalStackLayout { WidthRequest = leftW, Spacing = 0 };
        titles.Add(new ContentView { HeightRequest = headH });
        var canvas = new AbsoluteLayout { WidthRequest = days * dayW, HeightRequest = headH + spans.Count * rowH };
        void Place(View v, double x, double y, double w, double h) { AbsoluteLayout.SetLayoutBounds(v, new Rect(x, y, w, h)); canvas.Add(v); }

        for (var i = 0; i < days; i++)
        {
            var day = first.AddDays(i);
            if (day.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
                Place(new BoxView().Themed(BoxView.ColorProperty, Th.Subtle), i * dayW, 0, dayW, canvas.HeightRequest);
            if (i == 0 || day.Day == 1)
                Place(U.Text(day.ToString("MMM", Ui), 11, true, Th.Text2), i * dayW + 4, 0, 60, 16);
            var isToday = day == today;
            var num = U.Text(day.Day.ToString(Ui), 13, isToday, isToday ? Th.Accent : Th.Text);
            num.HorizontalTextAlignment = TextAlignment.Center;
            var wd = U.Caption(day.ToString("ddd", Ui)[..1], 10);
            wd.HorizontalTextAlignment = TextAlignment.Center;
            Place(wd, i * dayW, 16, dayW, 14);
            Place(num, i * dayW, 28, dayW, 18);
        }
        var todayX = (today - first).TotalDays * dayW;
        Place(new BoxView { Opacity = 0.7 }.Themed(BoxView.ColorProperty, Th.Accent), todayX + dayW / 2 - 1, headH - 2, 2, canvas.HeightRequest - headH + 2);

        for (var r = 0; r < spans.Count; r++)
        {
            var (info, start, end, point) = spans[r];
            var y = headH + r * rowH;
            var label = U.Text(info.Task.Title, 13);
            label.LineBreakMode = LineBreakMode.TailTruncation;
            label.MaxLines = 2;
            label.VerticalTextAlignment = TextAlignment.Center;
            var cell = new ContentView { HeightRequest = rowH, Padding = new Thickness(12, 0, 6, 0), Content = label };
            titles.Add(cell.Tap(() => ListViewModel.OpenTaskAsync(info.Id)));
            Place(new BoxView { Opacity = 0.6 }.Themed(BoxView.ColorProperty, Th.Stroke), 0, y + rowH - 1, canvas.WidthRequest, 1);
            var x = (start - first).TotalDays * dayW;
            var overdue = info.Task.IsOverdue(DateTimeOffset.Now);
            var color = overdue ? Th.Danger.Now : Th.Hex(info.Task.HexColor, tint);
            View bar;
            if (point)
            {
                bar = new Border
                {
                    BackgroundColor = color, StrokeThickness = 0, Rotation = 45,
                    StrokeShape = new RoundRectangle { CornerRadius = 3 },
                };
                Place(bar.Tap(() => ListViewModel.OpenTaskAsync(info.Id)), x + dayW / 2 - 8, y + rowH / 2 - 8, 16, 16);
            }
            else
            {
                var len = (end - start).TotalDays + 1;
                var pct = info.Task.PercentDone;
                var inner = new Grid();
                if (pct > 0) inner.Add(new BoxView { Color = Colors.Black.WithAlpha(0.18f), HorizontalOptions = LayoutOptions.Start, WidthRequest = (len * dayW - 4) * Math.Clamp(pct, 0, 1) });
                bar = new Border { BackgroundColor = color, StrokeThickness = 0, StrokeShape = new RoundRectangle { CornerRadius = 6 }, Content = inner };
                Place(bar.Tap(() => ListViewModel.OpenTaskAsync(info.Id)), x + 2, y + 10, len * dayW - 4, rowH - 20);
            }
        }

        var hscroll = new ScrollView { Orientation = ScrollOrientation.Horizontal, Content = canvas, HorizontalScrollBarVisibility = ScrollBarVisibility.Never };
        var grid = new Grid { ColumnDefinitions = [new(leftW), new(GridLength.Star)] };
        grid.Add(titles, 0);
        grid.Add(hscroll, 1);
        grid.Add(new BoxView { WidthRequest = 1, HorizontalOptions = LayoutOptions.End }.Themed(BoxView.ColorProperty, Th.Stroke), 0);
        hscroll.Loaded += async (_, _) => { await Task.Delay(50); await hscroll.ScrollToAsync(Math.Max(0, todayX - dayW * 2), 0, false); };

        var stack = new VerticalStackLayout { Spacing = 12, Padding = new Thickness(12, 4, 12, 16) };
        var chart = U.Card(spans.Count > 0 ? grid : (View)new Label { Text = "Nothing on the timeline yet.", Opacity = 0.7, Margin = new Thickness(4, 12) }, new Thickness(0, 0, 0, 0));
        stack.Add(chart);
        if (undated.Count > 0)
        {
            var list = new VerticalStackLayout { Spacing = 0 };
            list.Add(U.Text($"No dates  ·  {undated.Count}", 14, true));
            list.Add(U.Caption("Open a task and set its start and end dates (or a due date) to place it on the timeline.", 12));
            foreach (var t in undated.Take(40))
            {
                var l = U.Text(t.Task.Title, 14);
                l.Margin = new Thickness(0, 8, 0, 0);
                list.Add(l.Tap(() => ListViewModel.OpenTaskAsync(t.Id)));
            }
            stack.Add(U.Card(list));
        }
        return new ScrollView { Content = stack };
    }

    // ---------- board ----------
    public static View Board(ListViewModel vm)
    {
        if (vm.Board is not { } board) return new Label { Text = "This list has no board.", Margin = 24, Opacity = 0.7 };
        var width = DeviceDisplay.Current.MainDisplayInfo.Width / DeviceDisplay.Current.MainDisplayInfo.Density;
        var colW = Math.Min(340, width - 60);
        var items = board.Columns.Cast<object>().Append("add").ToList();
        var cv = new CollectionView
        {
            ItemsSource = items,
            ItemsLayout = new LinearItemsLayout(ItemsLayoutOrientation.Horizontal) { ItemSpacing = 10, SnapPointsType = SnapPointsType.Mandatory, SnapPointsAlignment = SnapPointsAlignment.Start },
            HorizontalScrollBarVisibility = ScrollBarVisibility.Never,
            Header = new ContentView { WidthRequest = 2 },
            Footer = new ContentView { WidthRequest = 12 },
            ItemTemplate = new DataTemplate(() =>
            {
                var host = new ContentView { WidthRequest = colW, Padding = new Thickness(0, 4, 0, 12) };
                host.BindingContextChanged += (_, _) => host.Content = host.BindingContext switch
                {
                    BoardColumn c => Column(vm, c, colW),
                    "add" => AddColumnCard(vm),
                    _ => null,
                };
                return host;
            }),
        };
        cv.Margin = new Thickness(10, 0, 0, 0);
        return cv;
    }

    private static View AddColumnCard(ListViewModel vm)
    {
        var row = new HorizontalStackLayout { Spacing = 8, HorizontalOptions = LayoutOptions.Center, VerticalOptions = LayoutOptions.Center };
        row.Add(U.Glyph(IconChars.Add, 18, Th.Accent));
        row.Add(U.Text("Add column", 15, true, Th.Accent));
        var b = new Border
        {
            Content = row, HeightRequest = 56, VerticalOptions = LayoutOptions.Start, StrokeThickness = 1.5, StrokeDashArray = [4, 3],
            StrokeShape = new RoundRectangle { CornerRadius = 8 },
        }.Themed(Border.StrokeProperty, Th.Accent).Themed(VisualElement.BackgroundColorProperty, Th.AccentTint);
        return b.Tap(vm.AddColumnAsync);
    }

    private static View Column(ListViewModel vm, BoardColumn col, double width)
    {
        var defaultColor = col.IsDone ? Color.FromArgb("#498205") : col.IsDefault ? Color.FromArgb("#69797E") : Th.Accent.Now;
        var color = Th.Hex(col.Style.Color, defaultColor);
        var cards = vm.CardsOf(col);

        var head = new Grid { ColumnDefinitions = [new(30), new(GridLength.Star), new(GridLength.Auto), new(40)], HeightRequest = 48, Padding = new Thickness(10, 0, 0, 0) };
        var icon = Icons.IsEmoji(col.Style.Icon)
            ? new Label { Text = col.Style.Icon, FontSize = 17, VerticalTextAlignment = TextAlignment.Center }
            : new Label { Text = Icons.GlyphOf(col.Style.Icon) ?? Icons.DefaultColumnGlyph(col), FontFamily = U.Icons, FontSize = 19, TextColor = color, VerticalTextAlignment = TextAlignment.Center };
        head.Add(icon, 0);
        var title = U.Text(col.Bucket.Title, 15, true);
        title.VerticalTextAlignment = TextAlignment.Center;
        title.LineBreakMode = LineBreakMode.TailTruncation;
        head.Add(title, 1);
        var count = new Border
        {
            StrokeThickness = 0, Padding = new Thickness(8, 1), VerticalOptions = LayoutOptions.Center, StrokeShape = new RoundRectangle { CornerRadius = 10 },
            BackgroundColor = color.WithAlpha(0.16f),
            Content = new Label { Text = cards.Count.ToString(Ui), FontFamily = "SelawikSemibold", FontSize = 12, TextColor = Th.Dark ? color.AddLuminosity(0.2f) : color },
        };
        head.Add(count, 2);
        head.Add(U.IconButton(IconChars.More, () => vm.ColumnMenuAsync(col), $"{col.Bucket.Title} options", size: 18), 3);

        var list = new VerticalStackLayout { Spacing = 8, Padding = new Thickness(8, 2, 8, 8) };
        foreach (var c in cards) list.Add(Card(vm, c, col));
        if (cards.Count == 0) list.Add(new Label { Text = col.IsDone ? "Finished cards land here." : "No cards yet.", FontSize = 13, Opacity = 0.6, Margin = new Thickness(4, 6), HorizontalTextAlignment = TextAlignment.Center }.Themed(Label.TextColorProperty, Th.Text2));

        var add = U.Entry("Add a card");
        add.ReturnType = ReturnType.Done;
        add.Completed += async (_, _) => { var text = add.Text; add.Text = ""; await vm.AddCardAsync(col, text ?? ""); };
        var addRow = new Grid { ColumnDefinitions = [new(32), new(GridLength.Star)], Padding = new Thickness(6, 0, 8, 4) };
        addRow.Add(U.Glyph(IconChars.Add, 16, Th.Accent), 0);
        addRow.Add(add, 1);

        var grid = new Grid { RowDefinitions = [new(4), new(GridLength.Auto), new(GridLength.Star), new(GridLength.Auto)] };
        grid.Add(new BoxView { Color = color }, 0, 0);
        grid.Add(head, 0, 1);
        grid.Add(new ScrollView { Content = list }, 0, 2);
        grid.Add(addRow, 0, 3);
        var border = new Border { Content = grid, StrokeThickness = 1, Padding = 0, StrokeShape = new RoundRectangle { CornerRadius = 10 }, WidthRequest = width };
        border.SetAppThemeColor(VisualElement.BackgroundColorProperty, Color.FromArgb("#EBEBEB").WithAlpha(0.96f), Color.FromArgb("#262626").WithAlpha(0.96f));
        return border.Themed(Border.StrokeProperty, Th.Stroke);
    }

    private static View Card(ListViewModel vm, TaskInfo info, BoardColumn col)
    {
        var t = info.Task;
        var row = new ViewModels.TaskRow(t, info.Project, false);
        var g = new Grid { ColumnDefinitions = [new(36), new(GridLength.Star), new(32)] };
        g.Add(new FluentCheck { IsChecked = t.Done, Scale = 0.85, Margin = new Thickness(-6, -8, 0, 0), VerticalOptions = LayoutOptions.Start, Command = new Command(async () => await vm.ToggleDoneByIdAsync(t.Id, !t.Done)) }, 0);
        var body = new VerticalStackLayout { Spacing = 4 };
        var title = U.Text(t.Title, 14);
        if (t.Done) { title.TextDecorations = TextDecorations.Strikethrough; title.Opacity = 0.6; }
        body.Add(title);
        var meta = new FlexLayout { Wrap = Microsoft.Maui.Layouts.FlexWrap.Wrap, AlignItems = Microsoft.Maui.Layouts.FlexAlignItems.Center };
        void Meta(string glyph, string? text, Duo? c = null)
        {
            var h = new HorizontalStackLayout { Spacing = 3, Margin = new Thickness(0, 0, 8, 2) };
            h.Add(U.Glyph(glyph, 11, c ?? Th.Text2));
            if (text is not null) h.Add(U.Text(text, 11, false, c ?? Th.Text2));
            meta.Add(h);
        }
        if (row.HasPriority) { var b = U.Badge(row.PriorityText, Th.Priority(row.Priority), 11); b.Margin = new Thickness(0, 0, 8, 2); meta.Add(b); }
        if (row.HasDue) Meta(IconChars.Calendar, row.ShortDue, row.IsOverdue ? Th.Danger : null);
        if (row.HasSubtasks) Meta(IconChars.Tree, row.SubtaskText);
        if (row.HasNotes) Meta(IconChars.Note, null);
        if (row.HasFiles) Meta(IconChars.Attach, null);
        if (row.HasReminder) Meta(IconChars.Bell, null);
        if (row.HasRepeat) Meta(IconChars.Repeat, null);
        foreach (var u in (t.Assignees ?? []).Take(3)) { var a = U.Avatar(u, 20); a.Margin = new Thickness(0, 0, 2, 2); meta.Add(a); }
        if (meta.Count > 0) body.Add(meta);
        g.Add(body, 1);
        g.Add(new Label { Text = t.IsFavorite ? IconChars.StarFilled : IconChars.Star, FontFamily = t.IsFavorite ? U.IconsFilled : U.Icons, FontSize = 16, HorizontalTextAlignment = TextAlignment.End, IsVisible = t.IsFavorite }.Themed(Label.TextColorProperty, Th.Accent), 2);
        var card = U.Card(g, new Thickness(10, 10, 8, 10));
        card.Behaviors.Add(new TouchBehavior
        {
            Command = new Command(async () => await ListViewModel.OpenTaskAsync(t.Id)),
            LongPressCommand = new Command(async () => { HapticFeedback.Default.Perform(HapticFeedbackType.LongPress); await vm.CardMenuAsync(info, col); }),
            LongPressDuration = 450,
            PressedOpacity = 0.75,
        });
        SemanticProperties.SetHint(card, "Tap to open. Touch and hold to move it to another column.");
        return card;
    }
}
