using System.Runtime.CompilerServices;
using Docket.App.Services;
using Docket.Core;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;

namespace Docket.App.Controls;

/// <summary>
/// Press and hold a task's tick (or right-click it, or press the menu key on it) to put the task in any board column:
/// To-Do, the columns in between, or Done. The same menu in every view. The click that would follow a hold is swallowed,
/// so holding never ticks the task by accident.
/// </summary>
public static class ColumnMenu
{
    private const int HoldMs = 450;
    private static readonly ConditionalWeakTable<ButtonBase, Holder> Held = new();
    private static bool _opening;

    private sealed class Holder { public bool Fired; }

    /// <param name="tick">The tick button or check box.</param>
    /// <param name="task">The task, read when the menu opens.</param>
    /// <param name="after">Runs after a move, to reload the view. Defaults to reloading the current page.</param>
    public static void Attach(ButtonBase tick, Func<VTask?> task, Func<Task>? after = null)
    {
        var holder = Held.GetOrCreateValue(tick);
        var timer = tick.DispatcherQueue.CreateTimer();
        timer.Interval = TimeSpan.FromMilliseconds(HoldMs);
        timer.IsRepeating = false;
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            holder.Fired = true;
            tick.ReleasePointerCaptures(); // cancels the pending click
            _ = ShowAsync(tick, task(), after);
        };
        tick.AddHandler(UIElement.PointerPressedEvent, new PointerEventHandler((_, e) =>
        {
            var p = e.GetCurrentPoint(tick);
            if (e.Pointer.PointerDeviceType == PointerDeviceType.Mouse && !p.Properties.IsLeftButtonPressed) return;
            holder.Fired = false;
            timer.Start();
        }), true);
        var stop = new PointerEventHandler((_, _) => timer.Stop());
        tick.AddHandler(UIElement.PointerReleasedEvent, stop, true);
        tick.AddHandler(UIElement.PointerCanceledEvent, stop, true);
        tick.AddHandler(UIElement.PointerCaptureLostEvent, stop, true);
        tick.AddHandler(UIElement.PointerExitedEvent, stop, true);
        tick.ContextRequested += (sender, e) =>
        {
            e.Handled = true;
            timer.Stop();
            _ = ShowAsync(tick, task(), after);
        };
        var tip = ToolTipService.GetToolTip(tick) as string;
        ToolTipService.SetToolTip(tick, $"{tip ?? "Mark as completed"}\nHold to move it to another column");
    }

    /// <summary>Call first in the tick's Click handler: true means the click ended a hold, so ignore it.</summary>
    public static bool Swallow(object sender)
    {
        if (sender is not ButtonBase b || !Held.TryGetValue(b, out var h) || !h.Fired) return false;
        h.Fired = false;
        if (b is ToggleButton t) t.IsChecked = !(t.IsChecked == true); // undo the toggle the click made
        return true;
    }

    public static async Task ShowAsync(FrameworkElement anchor, VTask? t, Func<Task>? after = null)
    {
        if (t is null || _opening || App.Service is not { } svc || App.Window is not { } win) return;
        _opening = true;
        try
        {
            Board? board = null;
            await win.RunAsync("Couldn't load the board columns", async () => board = await svc.BoardOfAsync(t.ProjectId));
            if (board is null || anchor.XamlRoot is null) return;
            var here = board.Columns.FirstOrDefault(c => c.Tasks.Any(x => x.Id == t.Id))
                ?? (t.Done ? board.Columns.FirstOrDefault(c => c.IsDone) : board.Columns.FirstOrDefault(c => c.IsDefault));

            var menu = new MenuFlyout { Placement = FlyoutPlacementMode.BottomEdgeAlignedLeft };
            menu.Items.Add(new MenuFlyoutItem { Text = $"Move \"{Short(t.Title)}\" to", IsEnabled = false });
            menu.Items.Add(new MenuFlyoutSeparator());
            foreach (var col in board.Columns)
            {
                var target = col;
                var isHere = here?.Bucket.Id == col.Bucket.Id;
                var item = new MenuFlyoutItem
                {
                    Text = col.Bucket.Title,
                    Icon = Icons.NavIcon(col.Style.Icon, Ui.Hex(Hex(col)), Icons.DefaultColumnGlyph(col)),
                    IsEnabled = !isHere,
                    KeyboardAcceleratorTextOverride = isHere ? "Here now" : col.IsDone ? "Completes it" : "",
                };
                item.Click += async (_, _) => await win.RunAsync("Couldn't move the task", async () =>
                {
                    await svc.MoveToColumnAsync(t.ProjectId, t.Id, target.Bucket.Id);
                    win.SetStatus($"Moved to {target.Bucket.Title}");
                    if (here is { } from)
                        win.OfferUndo($"Moved \"{Short(t.Title)}\" to {target.Bucket.Title}", () => svc.MoveToColumnAsync(t.ProjectId, t.Id, from.Bucket.Id));
                    if (after is not null) await after(); else await win.RefreshAllAsync();
                });
                menu.Items.Add(item);
            }
            menu.ShowAt(anchor);
        }
        finally { _opening = false; }
    }

    internal static string Hex(BoardColumn c) => c.Style.Color ?? (c.IsDone ? "2f8f46" : c.IsDefault ? "2564cf" : "8a8886");

    private static string Short(string s) => s.Length <= 40 ? s : s[..39] + "…";
}
