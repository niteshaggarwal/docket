using System.Globalization;
using Docket.App.Pages;
using Docket.App.Services;
using Docket.Core;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.ApplicationModel.DataTransfer;
using Windows.System;

namespace Docket.App.Controls;

/// <summary>
/// A list as a Kanban board. Columns have their own colour and icon (Fluent glyph or emoji); To-Do and Done are permanent
/// but can be renamed and restyled. Cards show the column's icon (Done keeps a check), subtasks, due date, priority and people.
/// </summary>
public sealed partial class BoardView : UserControl, IListView
{
    private readonly StackPanel _columns = new() { Orientation = Orientation.Horizontal, Spacing = 12, Padding = new Thickness(0, 0, 24, 12) };
    private VProject? _project;
    private Board? _board;
    private SortMode _sort;
    private bool _prioritySort;
    private (TaskInfo Task, BoardColumn From)? _drag;

    public event EventHandler<long>? OpenTask;
    public event EventHandler? Changed;

    public BoardView()
    {
        Content = new ScrollViewer
        {
            Content = _columns, HorizontalScrollMode = ScrollMode.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollMode = ScrollMode.Disabled, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
        };
    }

    private static DocketService? Svc => App.Service;
    private static MainWindow? Win => App.Window;

    public async Task LoadAsync(VProject project, SortMode sort, bool prioritySort)
    {
        if (Svc is not { } svc) return;
        _project = project; _sort = sort; _prioritySort = prioritySort;
        try { _board = await svc.BoardOfAsync(project.Id); }
        catch (DocketException ex)
        {
            _columns.Children.Clear();
            _columns.Children.Add(Ui.Text(ex.Message, "BodyTextBlockStyle", 0.7, TextWrapping.Wrap));
            return;
        }
        Render();
    }

    private Task ReloadAsync() => _project is null ? Task.CompletedTask : LoadAsync(_project, _sort, _prioritySort);

    private IEnumerable<TaskInfo> Sorted(IEnumerable<TaskInfo> tasks)
    {
        if (_prioritySort) return tasks.OrderByDescending(t => t.Task.Priority).ThenBy(t => t.Task.Due ?? DateTimeOffset.MaxValue);
        if (_sort == SortMode.Custom) return tasks;
        var list = tasks.ToList();
        return DocketService.Sort(list.Select(t => t.Task), _sort).Select(t => list.First(i => i.Id == t.Id));
    }

    private void Render()
    {
        _columns.Children.Clear();
        if (_board is null) return;
        foreach (var col in _board.Columns) _columns.Children.Add(Column(col));
        var add = new Button { VerticalAlignment = VerticalAlignment.Top, Padding = new Thickness(14, 10, 14, 10), Width = 220, HorizontalContentAlignment = HorizontalAlignment.Left };
        var addContent = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        addContent.Children.Add(Ui.Glyph("", 14));
        addContent.Children.Add(new TextBlock { Text = "Add column" });
        add.Content = addContent;
        AutomationProperties.SetName(add, "Add column");
        add.Click += async (_, _) => await AddColumnAsync();
        _columns.Children.Add(add);
    }

    private static string ColumnHex(BoardColumn c) => c.Style.Color ?? (c.IsDone ? "2f8f46" : c.IsDefault ? "2564cf" : "8a8886");

    private FrameworkElement Column(BoardColumn col)
    {
        var hex = ColumnHex(col);
        var grid = new Grid { Width = 300, CornerRadius = new CornerRadius(8), Background = Ui.Brush("CardBackgroundFillColorDefaultBrush"), BorderBrush = Ui.Brush("CardStrokeColorDefaultBrush"), BorderThickness = new Thickness(1) };
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(4) });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.Children.Add(new Border { Background = Ui.Hex(hex) });

        // header
        var head = new Grid { ColumnSpacing = 10, Padding = new Thickness(14, 10, 6, 6) };
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var badge = new Border { Width = 28, Height = 28, CornerRadius = new CornerRadius(6), Background = Ui.Hex(hex, 0.16), Child = Icons.Element(col.Style.Icon, 14, Ui.Hex(hex), Icons.DefaultColumnGlyph(col)) };
        var title = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center };
        title.Children.Add(new TextBlock { Text = col.Bucket.Title, Style = Ui.TextStyle("BodyStrongTextBlockStyle"), TextTrimming = TextTrimming.CharacterEllipsis });
        var count = col.Bucket.Limit > 0 ? $"{col.Tasks.Count}/{col.Bucket.Limit}" : col.Tasks.Count.ToString(CultureInfo.CurrentCulture);
        title.Children.Add(new Border { CornerRadius = new CornerRadius(10), Padding = new Thickness(8, 0, 8, 1), Background = Ui.Brush("SubtleFillColorSecondaryBrush"), Child = new TextBlock { Text = count, FontSize = 12, Foreground = Ui.Brush("TextFillColorSecondaryBrush") } });
        if (col.IsPermanent) ToolTipService.SetToolTip(badge, col.IsDone ? "Done column: cards here are completed" : "To-Do column: new tasks land here");
        var more = Ui.IconButton("", $"Options for {col.Bucket.Title}", (_, _) => { }, 13);
        var menu = new MenuFlyout { Placement = Microsoft.UI.Xaml.Controls.Primitives.FlyoutPlacementMode.BottomEdgeAlignedRight };
        menu.Items.Add(MenuItem("Rename", "", () => RenameColumnAsync(col)));
        menu.Items.Add(MenuItem("Colour…", "", () => ColorColumnAsync(col)));
        menu.Items.Add(MenuItem("Icon…", "", () => IconColumnAsync(col)));
        if (!col.IsPermanent)
        {
            menu.Items.Add(new MenuFlyoutSeparator());
            menu.Items.Add(MenuItem("Delete column", "", () => DeleteColumnAsync(col)));
        }
        more.Flyout = menu;
        Grid.SetColumn(title, 1);
        Grid.SetColumn(more, 2);
        head.Children.Add(badge);
        head.Children.Add(title);
        head.Children.Add(more);
        Grid.SetRow(head, 1);
        grid.Children.Add(head);

        // cards
        var list = new ListView
        {
            SelectionMode = ListViewSelectionMode.None, IsItemClickEnabled = true, CanDragItems = true, AllowDrop = true, CanReorderItems = false,
            Padding = new Thickness(8, 0, 8, 0), MinHeight = 80, Tag = col,
        };
        AutomationProperties.SetName(list, $"{col.Bucket.Title} cards");
        list.ItemContainerStyle = CardContainer();
        foreach (var t in Sorted(col.Tasks)) list.Items.Add(new ListViewItem { Content = Card(t, col), Tag = t, Padding = new Thickness(0), Margin = new Thickness(0, 3, 0, 3), HorizontalContentAlignment = HorizontalAlignment.Stretch });
        list.ItemClick += (_, e) =>
        {
            if ((e.ClickedItem as FrameworkElement)?.Tag is TaskInfo t) OpenTask?.Invoke(this, t.Id);
        };
        list.DragItemsStarting += (_, e) =>
        {
            if (e.Items.FirstOrDefault() is ListViewItem { Tag: TaskInfo t })
            {
                _drag = (t, col);
                e.Data.SetText($"docket-task:{t.Id}");
                e.Data.RequestedOperation = DataPackageOperation.Move;
            }
            else e.Cancel = true;
        };
        list.DragOver += (_, e) =>
        {
            if (_drag is { } d && d.From.Bucket.Id != col.Bucket.Id) { e.AcceptedOperation = DataPackageOperation.Move; e.DragUIOverride.Caption = $"Move to {col.Bucket.Title}"; }
        };
        list.Drop += async (_, _) => await DropAsync(col);
        Grid.SetRow(list, 2);
        grid.Children.Add(list);

        // add a card
        var add = new TextBox { PlaceholderText = "Add a card", Margin = new Thickness(8, 4, 8, 10), BorderThickness = new Thickness(0), Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent) };
        AutomationProperties.SetName(add, $"Add a card to {col.Bucket.Title}");
        add.KeyDown += async (_, e) =>
        {
            if (e.Key != VirtualKey.Enter || add.Text.Trim().Length == 0) return;
            e.Handled = true;
            var text = add.Text.Trim();
            add.Text = "";
            await AddCardAsync(col, text);
        };
        Grid.SetRow(add, 3);
        if (!col.IsDone) grid.Children.Add(add);
        return grid;
    }

    private static Style CardContainer()
    {
        var s = new Style(typeof(ListViewItem)) { BasedOn = Ui.Res<Style>("DefaultListViewItemStyle") };
        s.Setters.Add(new Setter(PaddingProperty, new Thickness(0)));
        s.Setters.Add(new Setter(MinHeightProperty, 0.0));
        s.Setters.Add(new Setter(HorizontalContentAlignmentProperty, HorizontalAlignment.Stretch));
        return s;
    }

    private FrameworkElement Card(TaskInfo info, BoardColumn col)
    {
        var t = info.Task;
        var hex = ColumnHex(col);
        var card = new Grid
        {
            ColumnSpacing = 10, Padding = new Thickness(10, 10, 12, 10), CornerRadius = new CornerRadius(6), Tag = info,
            Background = Ui.Brush("LayerFillColorDefaultBrush"), BorderBrush = Ui.Brush("ControlStrokeColorDefaultBrush"), BorderThickness = new Thickness(1),
        };
        card.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        card.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        FrameworkElement lead;
        if (col.IsDone || t.Done)
        {
            var box = new CheckBox { IsChecked = t.Done, MinWidth = 0, Padding = new Thickness(0), VerticalAlignment = VerticalAlignment.Top };
            AutomationProperties.SetName(box, t.Done ? $"Mark {t.Title} not completed" : $"Mark {t.Title} completed");
            box.Click += async (_, _) => await SetDoneAsync(info, box.IsChecked == true);
            lead = box;
        }
        else
        {
            var b = new Button
            {
                Width = 26, Height = 26, Padding = new Thickness(0), CornerRadius = new CornerRadius(13), VerticalAlignment = VerticalAlignment.Top,
                Background = Ui.Hex(hex, 0.14), BorderBrush = Ui.Hex(hex, 0.6), BorderThickness = new Thickness(1),
                Content = Icons.Element(col.Style.Icon, 12, Ui.Hex(hex), Icons.DefaultColumnGlyph(col)),
            };
            ToolTipService.SetToolTip(b, "Mark as completed");
            AutomationProperties.SetName(b, $"Mark {t.Title} completed");
            b.Click += async (_, _) => await SetDoneAsync(info, true);
            lead = b;
        }
        card.Children.Add(lead);

        var body = new StackPanel { Spacing = 6 };
        body.Children.Add(new TextBlock
        {
            Text = t.Title, TextWrapping = TextWrapping.Wrap, MaxLines = 4, TextTrimming = TextTrimming.CharacterEllipsis,
            Opacity = t.Done ? 0.6 : 1, TextDecorations = t.Done ? Windows.UI.Text.TextDecorations.Strikethrough : Windows.UI.Text.TextDecorations.None,
        });
        var meta = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        if (t.Priority > 0) meta.Children.Add(Ui.PriorityBadge(t.Priority, compact: true));
        if (t.Subtasks.Count > 0) meta.Children.Add(MetaText("", $"{t.Subtasks.Count(s => s.Done)}/{t.Subtasks.Count}", $"{t.Subtasks.Count(s => s.Done)} of {t.Subtasks.Count} subtasks done"));
        if (t.Due is { } due)
        {
            var item = new TaskItem(t);
            var m = MetaText("", item.DueText.Replace("Due ", "", StringComparison.Ordinal), item.DueText);
            if (item.IsOverdue) foreach (var c in ((StackPanel)m).Children) SetFg(c, Ui.Brush("DocketOverdueBrush"));
            meta.Children.Add(m);
        }
        if (t.NextReminder(DateTimeOffset.Now) is not null) meta.Children.Add(MetaIcon("", "Reminder"));
        if (RepeatRule.From(t) is { } rr) meta.Children.Add(MetaIcon("", rr.Describe()));
        if (TaskNotes.Parse(t.Description).Notes.Trim().Length > 0) meta.Children.Add(MetaIcon("", "Has a note"));
        if (t.Attachments is { Count: > 0 }) meta.Children.Add(MetaIcon("", "Files attached"));
        var bottom = new Grid();
        bottom.Children.Add(meta);
        if (t.Assignees is { Count: > 0 } people)
        {
            var stack = Ui.AvatarStack(people, 20);
            stack.HorizontalAlignment = HorizontalAlignment.Right;
            bottom.Children.Add(stack);
        }
        if (meta.Children.Count > 0 || t.Assignees is { Count: > 0 }) body.Children.Add(bottom);
        Grid.SetColumn(body, 1);
        card.Children.Add(body);

        var ctx = new MenuFlyout();
        ctx.Items.Add(MenuItem("Open", "", () => { OpenTask?.Invoke(this, info.Id); return Task.CompletedTask; }));
        var moveTo = new MenuFlyoutSubItem { Text = "Move to column", Icon = new FontIcon { Glyph = "" } };
        foreach (var other in _board?.Columns ?? [])
        {
            if (other.Bucket.Id == col.Bucket.Id) continue;
            var target = other;
            moveTo.Items.Add(MenuItem(other.Bucket.Title, "", () => MoveAsync(info, col, target)));
        }
        ctx.Items.Add(moveTo);
        card.ContextFlyout = ctx;
        AutomationProperties.SetName(card, t.Title);
        return card;
    }

    private static void SetFg(UIElement e, Brush b)
    {
        if (e is TextBlock tb) tb.Foreground = b;
        else if (e is FontIcon f) f.Foreground = b;
    }

    private static FrameworkElement MetaText(string glyph, string text, string tip)
    {
        var p = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, VerticalAlignment = VerticalAlignment.Center };
        p.Children.Add(Ui.Glyph(glyph, 11, Ui.Brush("TextFillColorSecondaryBrush")));
        p.Children.Add(new TextBlock { Text = text, Style = Ui.TextStyle("CaptionTextBlockStyle"), Foreground = Ui.Brush("TextFillColorSecondaryBrush") });
        ToolTipService.SetToolTip(p, tip);
        return p;
    }

    private static FrameworkElement MetaIcon(string glyph, string tip)
    {
        var f = Ui.Glyph(glyph, 11, Ui.Brush("TextFillColorSecondaryBrush"));
        f.VerticalAlignment = VerticalAlignment.Center;
        ToolTipService.SetToolTip(f, tip);
        return f;
    }

    private static MenuFlyoutItem MenuItem(string text, string glyph, Func<Task> action)
    {
        var i = new MenuFlyoutItem { Text = text, Icon = new FontIcon { Glyph = glyph } };
        i.Click += async (_, _) => await action();
        return i;
    }

    // ---------- actions ----------
    private async Task Run(string fail, Func<Task> action, bool reload = true)
    {
        if (Win is null) return;
        await Win.RunAsync(fail, async () => { await action(); if (reload) await ReloadAsync(); Changed?.Invoke(this, EventArgs.Empty); });
    }

    private async Task DropAsync(BoardColumn to)
    {
        if (_drag is not { } d || _project is null || Svc is not { } svc) return;
        _drag = null;
        if (d.From.Bucket.Id == to.Bucket.Id) return;
        await MoveAsync(d.Task, d.From, to);
    }

    private Task MoveAsync(TaskInfo t, BoardColumn from, BoardColumn to) => Run("Couldn't move the card", async () =>
    {
        await Svc!.MoveToColumnAsync(_project!.Id, t.Id, to.Bucket.Id);
        Win?.SetStatus($"Moved to {to.Bucket.Title}");
        if (to.IsDone) Win?.OfferUndo($"Completed \"{t.Task.Title}\"", () => Svc!.MoveToColumnAsync(_project!.Id, t.Id, from.Bucket.Id));
    });

    private Task SetDoneAsync(TaskInfo t, bool done)
    {
        if (_board is null || _project is null) return Task.CompletedTask;
        var doneCol = _board.Columns.FirstOrDefault(c => c.IsDone);
        var todo = _board.Columns.FirstOrDefault(c => c.IsDefault);
        var from = _board.Columns.FirstOrDefault(c => c.Tasks.Any(x => x.Id == t.Id));
        return Run("Couldn't update the card", async () =>
        {
            if (done && doneCol is not null) await Svc!.MoveToColumnAsync(_project.Id, t.Id, doneCol.Bucket.Id);
            else if (!done && todo is not null) await Svc!.MoveToColumnAsync(_project.Id, t.Id, todo.Bucket.Id);
            else await Svc!.SetDoneAsync(t.Id, done);
            if (done && from is not null) Win?.OfferUndo($"Completed \"{t.Task.Title}\"", () => Svc!.MoveToColumnAsync(_project.Id, t.Id, from.Bucket.Id));
        });
    }

    private Task AddCardAsync(BoardColumn col, string text) => Run("Couldn't add the card", async () =>
    {
        var t = await Svc!.AddAsync(text, _project!.Id.ToString(CultureInfo.InvariantCulture));
        if (!col.IsDefault) await Svc.MoveToColumnAsync(_project.Id, t.Id, col.Bucket.Id);
    });

    private async Task AddColumnAsync()
    {
        if (_project is null) return;
        var name = await Dialogs.PromptAsync(XamlRoot, "Add column", "Column name", "Doing", "Add");
        if (string.IsNullOrWhiteSpace(name)) return;
        var used = _board?.Columns.Select(c => c.Style.Color).ToHashSet() ?? [];
        var color = Ui.Palette.Select(p => p.Hex).FirstOrDefault(h => !used.Contains(h) && h != "2564cf" && h != "2f8f46") ?? "7a4fd6";
        await Run("Couldn't add the column", () => Svc!.AddColumnAsync(_project.Id, name, new ColumnStyle(color, "Play")));
    }

    private async Task RenameColumnAsync(BoardColumn col)
    {
        var name = await Dialogs.PromptAsync(XamlRoot, "Rename column", "Column name", col.Bucket.Title, "Rename");
        if (string.IsNullOrWhiteSpace(name)) return;
        await Run("Couldn't rename the column", () => Svc!.RenameColumnAsync(_project!.Id, col.Bucket.Id, name));
    }

    private async Task ColorColumnAsync(BoardColumn col)
    {
        var pick = await Pickers.ColorAsync(XamlRoot, $"Colour for \"{col.Bucket.Title}\"", col.Style.Color);
        if (!pick.Ok) return;
        await Run("Couldn't change the colour", () => Svc!.SetColumnStyleAsync(_project!.Id, col.Bucket.Id, col.Style with { Color = pick.Value }));
    }

    private async Task IconColumnAsync(BoardColumn col)
    {
        var pick = await Pickers.IconAsync(XamlRoot, $"Icon for \"{col.Bucket.Title}\"", col.Style.Icon, ColumnHex(col));
        if (!pick.Ok) return;
        await Run("Couldn't change the icon", () => Svc!.SetColumnStyleAsync(_project!.Id, col.Bucket.Id, col.Style with { Icon = pick.Value }));
    }

    private async Task DeleteColumnAsync(BoardColumn col)
    {
        if (!await Dialogs.ConfirmAsync(XamlRoot, "Delete column?", $"\"{col.Bucket.Title}\" will be removed. Its {col.Tasks.Count} {(col.Tasks.Count == 1 ? "card moves" : "cards move")} to the To-Do column.")) return;
        await Run("Couldn't delete the column", () => Svc!.DeleteColumnAsync(_project!.Id, col.Bucket.Id));
    }
}
