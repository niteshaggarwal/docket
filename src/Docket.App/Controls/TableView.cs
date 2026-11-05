using System.Globalization;
using Docket.App.Pages;
using Docket.App.Services;
using Docket.Core;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Docket.App.Controls;

/// <summary>A list as a table: sortable columns, a column picker, one row per task (subtasks included).</summary>
public sealed partial class TableView : UserControl, IListView
{
    private sealed record Col(string Key, string Title, double Width, Func<VTask, IComparable?> SortKey);

    private static readonly Col[] All =
    [
        new("title", "Title", 340, t => t.Title.ToLower(CultureInfo.CurrentCulture)),
        new("done", "Done", 64, t => t.Done),
        new("due", "Due", 130, t => t.Due),
        new("start", "Start", 120, t => VDates.OrNull(t.StartDate)),
        new("end", "End", 120, t => VDates.OrNull(t.EndDate)),
        new("priority", "Priority", 104, t => t.Priority),
        new("assignees", "Assignees", 116, t => t.Assignees?.Count ?? 0),
        new("labels", "Labels", 170, t => string.Join(",", (t.Labels ?? []).Where(DocketService.IsCategory).Select(l => l.Title))),
        new("created", "Created", 130, t => t.Created),
        new("updated", "Updated", 130, t => t.Updated),
        new("list", "List", 140, t => t.ProjectId),
    ];

    private static readonly HashSet<string> Visible = ["title", "done", "due", "priority", "assignees", "labels", "created"];
    private static string _sortKey = "due";
    private static bool _desc;

    private readonly Grid _header = new() { Padding = new Thickness(12, 0, 12, 0), MinHeight = 40 };
    private readonly ListView _rows = new() { SelectionMode = ListViewSelectionMode.Single, IsItemClickEnabled = true };
    private readonly StackPanel _body = new();
    private List<TaskInfo> _tasks = [];

    public event EventHandler<long>? OpenTask;
    public event EventHandler? Changed;

    public TableView()
    {
        AutomationProperties.SetName(_rows, "Tasks table");
        var style = new Style(typeof(ListViewItem)) { BasedOn = Ui.Res<Style>("DefaultListViewItemStyle") };
        style.Setters.Add(new Setter(PaddingProperty, new Thickness(0)));
        style.Setters.Add(new Setter(MinHeightProperty, 0.0));
        style.Setters.Add(new Setter(HorizontalContentAlignmentProperty, HorizontalAlignment.Stretch));
        _rows.ItemContainerStyle = style;
        _rows.ItemClick += (_, e) => { if ((e.ClickedItem as FrameworkElement)?.Tag is TaskInfo t) OpenTask?.Invoke(this, t.Id); };

        var columnsButton = new DropDownButton { Content = ButtonContent("", "Columns"), HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 0, 0, 8) };
        AutomationProperties.SetName(columnsButton, "Choose columns");
        var menu = new MenuFlyout { Placement = Microsoft.UI.Xaml.Controls.Primitives.FlyoutPlacementMode.BottomEdgeAlignedRight };
        foreach (var c in All.Skip(1))
        {
            var item = new ToggleMenuFlyoutItem { Text = c.Title, IsChecked = Visible.Contains(c.Key) };
            var key = c.Key;
            item.Click += (_, _) => { if (item.IsChecked) Visible.Add(key); else Visible.Remove(key); Render(); };
            menu.Items.Add(item);
        }
        columnsButton.Flyout = menu;

        var table = new Border
        {
            CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(1), BorderBrush = Ui.Brush("CardStrokeColorDefaultBrush"),
            Background = Ui.Brush("CardBackgroundFillColorDefaultBrush"), Child = _body, HorizontalAlignment = HorizontalAlignment.Left,
        };
        _body.Children.Add(new Border { Child = _header, BorderThickness = new Thickness(0, 0, 0, 1), BorderBrush = Ui.Brush("DividerStrokeColorDefaultBrush"), Background = Ui.Brush("SubtleFillColorSecondaryBrush"), CornerRadius = new CornerRadius(8, 8, 0, 0) });
        _body.Children.Add(_rows);

        var outer = new Grid();
        outer.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        outer.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        var scroll = new ScrollViewer
        {
            Content = table, HorizontalScrollMode = ScrollMode.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollMode = ScrollMode.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Padding = new Thickness(0, 0, 0, 12),
        };
        Grid.SetRow(scroll, 1);
        outer.Children.Add(columnsButton);
        outer.Children.Add(scroll);
        Content = outer;
    }

    private static StackPanel ButtonContent(string glyph, string text)
    {
        var p = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        p.Children.Add(Ui.Glyph(glyph, 14));
        p.Children.Add(new TextBlock { Text = text });
        return p;
    }

    private static DocketService? Svc => App.Service;

    public async Task LoadAsync(VProject project, SortMode sort, bool prioritySort)
    {
        if (Svc is not { } svc) return;
        var content = await svc.ProjectListAsync(project.Id, SortMode.Custom);
        _tasks = [.. content.Open, .. content.Completed];
        if (prioritySort) { _sortKey = "priority"; _desc = true; }
        Render();
    }

    private IEnumerable<Col> Columns => All.Where(c => Visible.Contains(c.Key));

    private void Render()
    {
        var cols = Columns.ToList();
        _header.ColumnDefinitions.Clear();
        _header.Children.Clear();
        for (var i = 0; i < cols.Count; i++)
        {
            var c = cols[i];
            _header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(c.Width) });
            var sorted = _sortKey == c.Key;
            var content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
            content.Children.Add(new TextBlock { Text = c.Title, FontWeight = FontWeights.SemiBold, FontSize = 12, Foreground = Ui.Brush(sorted ? "TextFillColorPrimaryBrush" : "TextFillColorSecondaryBrush") });
            if (sorted) content.Children.Add(Ui.Glyph(_desc ? "" : "", 9));
            var b = new Button
            {
                Content = content, Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent), BorderThickness = new Thickness(0),
                Padding = new Thickness(4, 6, 4, 6), HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Left,
            };
            AutomationProperties.SetName(b, $"Sort by {c.Title}");
            ToolTipService.SetToolTip(b, $"Sort by {c.Title}");
            var key = c.Key;
            b.Click += (_, _) => { if (_sortKey == key) _desc = !_desc; else { _sortKey = key; _desc = key is "priority" or "updated" or "created"; } Render(); };
            Grid.SetColumn(b, i);
            _header.Children.Add(b);
        }

        var sortCol = All.First(c => c.Key == _sortKey);
        var ordered = _desc ? _tasks.OrderByDescending(t => sortCol.SortKey(t.Task) ?? MinKey(sortCol)) : _tasks.OrderBy(t => sortCol.SortKey(t.Task) ?? MaxKey(sortCol));
        _rows.Items.Clear();
        foreach (var t in ordered) _rows.Items.Add(Row(t, cols));
        if (_tasks.Count == 0) _rows.Items.Add(new TextBlock { Text = "No tasks in this list yet.", Margin = new Thickness(16), Opacity = 0.65 });
    }

    private static IComparable MaxKey(Col c) => c.Key is "due" or "start" or "end" ? DateTimeOffset.MaxValue : "";
    private static IComparable MinKey(Col c) => c.Key is "due" or "start" or "end" ? DateTimeOffset.MinValue : "";

    private FrameworkElement Row(TaskInfo info, List<Col> cols)
    {
        var t = info.Task;
        var g = new Grid { Padding = new Thickness(12, 0, 12, 0), MinHeight = 44, Tag = info, BorderThickness = new Thickness(0, 0, 0, 1), BorderBrush = Ui.Brush("DividerStrokeColorDefaultBrush") };
        AutomationProperties.SetName(g, t.Title);
        for (var i = 0; i < cols.Count; i++)
        {
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(cols[i].Width) });
            var cell = Cell(info, cols[i].Key);
            cell.Margin = new Thickness(4, 0, 8, 0);
            cell.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(cell, i);
            g.Children.Add(cell);
        }
        return g;
    }

    private FrameworkElement Cell(TaskInfo info, string key)
    {
        var t = info.Task;
        TextBlock Txt(string s, double opacity = 1) => new() { Text = s, TextTrimming = TextTrimming.CharacterEllipsis, Opacity = opacity, Style = Ui.TextStyle("BodyTextBlockStyle"), TextWrapping = TextWrapping.NoWrap };
        string D(DateTimeOffset? d) => d is { } x ? x.ToLocalTime().ToString(x.Year == DateTimeOffset.Now.Year ? "ddd d MMM" : "d MMM yyyy", CultureInfo.CurrentCulture) : "";
        switch (key)
        {
            case "title":
            {
                var p = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
                if (t.IsFavorite) p.Children.Add(Ui.Glyph("", 12, Ui.Brush("AccentTextFillColorPrimaryBrush")));
                if (t.Parents.Count > 0) p.Children.Add(Ui.Glyph("", 11, Ui.Brush("TextFillColorSecondaryBrush")));
                var title = Txt(t.Title, t.Done ? 0.6 : 1);
                if (t.Done) title.TextDecorations = Windows.UI.Text.TextDecorations.Strikethrough;
                p.Children.Add(title);
                if (t.Subtasks.Count > 0) p.Children.Add(Txt($"{t.Subtasks.Count(s => s.Done)}/{t.Subtasks.Count}", 0.55));
                return p;
            }
            case "done":
            {
                var box = new CheckBox { IsChecked = t.Done, MinWidth = 0, Padding = new Thickness(0) };
                AutomationProperties.SetName(box, $"{t.Title} done");
                box.Click += async (_, _) =>
                {
                    if (Svc is not { } svc || App.Window is not { } win) return;
                    await win.RunAsync("Couldn't update the task", async () => { var r = await svc.SetDoneAsync(t.Id, box.IsChecked == true); info.Task.Done = r.Task.Done; Changed?.Invoke(this, EventArgs.Empty); });
                };
                return box;
            }
            case "due":
            {
                var tb = Txt(D(t.Due));
                if (t.IsOverdue(DateTimeOffset.Now)) tb.Foreground = Ui.Brush("DocketOverdueBrush");
                return tb;
            }
            case "start": return Txt(D(VDates.OrNull(t.StartDate)));
            case "end": return Txt(D(VDates.OrNull(t.EndDate)));
            case "priority": return t.Priority > 0 ? Ui.PriorityBadge(t.Priority, compact: true) : Txt("—", 0.4);
            case "assignees": return t.Assignees is { Count: > 0 } a ? Ui.AvatarStack(a, 22, 4) : Txt("—", 0.4);
            case "labels":
            {
                var p = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
                foreach (var l in (t.Labels ?? []).Where(DocketService.IsCategory).Take(3))
                    p.Children.Add(new Border { CornerRadius = new CornerRadius(4), Padding = new Thickness(6, 0, 6, 1), Background = Ui.Hex(l.HexColor, 0.18), Child = new TextBlock { Text = l.Title, FontSize = 12 } });
                if (p.Children.Count == 0) p.Children.Add(Txt("—", 0.4));
                return p;
            }
            case "created": return Txt(D(VDates.OrNull(t.Created)), 0.8);
            case "updated": return Txt(D(VDates.OrNull(t.Updated)), 0.8);
            default: return Txt(info.Project, 0.8);
        }
    }
}
