using Docket.Core;
using Docket.Mobile.Services;
using Docket.Mobile.Views;
using Icons = Docket.Mobile.Services.Icons;

namespace Docket.Mobile.ViewModels;

/// <summary>Table, Timeline and Board: the same tasks as the list, drawn differently.</summary>
public sealed partial class ListViewModel
{
    /// <summary>Table sort: "title", "due", "priority" or "list"; descending when <see cref="TableDescending"/>.</summary>
    public string TableSort { get; private set; } = "due";
    public bool TableDescending { get; private set; }

    public IReadOnlyList<TaskInfo> TableRows
    {
        get
        {
            var rows = FlatOpen;
            IOrderedEnumerable<TaskInfo> ordered = TableSort switch
            {
                "title" => rows.OrderBy(t => t.Task.Title, StringComparer.CurrentCultureIgnoreCase),
                "priority" => rows.OrderBy(t => t.Task.Priority).ThenBy(t => t.Task.Due ?? DateTimeOffset.MaxValue),
                "list" => rows.OrderBy(t => t.Project, StringComparer.CurrentCultureIgnoreCase),
                _ => rows.OrderBy(t => t.Task.Due ?? DateTimeOffset.MaxValue),
            };
            var list = ordered.ToList();
            if (TableDescending ^ (TableSort == "priority")) list.Reverse();
            return list;
        }
    }

    public void SortTable(string column)
    {
        if (TableSort == column) TableDescending = !TableDescending;
        else { TableSort = column; TableDescending = false; }
        DataChanged?.Invoke(this, EventArgs.Empty);
    }

    public Task ToggleDoneByIdAsync(long id, bool done) => RunAsync(async svc =>
    {
        HapticFeedback.Default.Perform(HapticFeedbackType.Click);
        await svc.SetDoneAsync(id, done);
    });

    // ---------- board ----------
    /// <summary>A board column's cards in the list's chosen sort.</summary>
    public IReadOnlyList<TaskInfo> CardsOf(BoardColumn col)
    {
        if (Prefs.SortByPriority(_key)) return col.Tasks.OrderByDescending(t => t.Task.Priority).ThenBy(t => t.Task.Due ?? DateTimeOffset.MaxValue).ToList();
        var sort = Prefs.SortFor(_key);
        if (sort == SortMode.Custom) return col.Tasks;
        return DocketService.Sort(col.Tasks.Select(t => t.Task), sort).Select(t => col.Tasks.First(x => x.Id == t.Id)).ToList();
    }

    public async Task CardMenuAsync(TaskInfo card, BoardColumn from)
    {
        if (Board is not { } board) return;
        var choices = new List<Choice> { new("Open", IconChars.ArrowRight), new(card.Task.Done ? "Mark as not done" : "Mark as done", IconChars.Done) };
        var targets = board.Columns.Where(c => c.Bucket.Id != from.Bucket.Id).ToList();
        foreach (var c in targets) choices.Add(new Choice($"Move to {c.Bucket.Title}", Icons.GlyphOf(c.Style.Icon) ?? Icons.DefaultColumnGlyph(c), null, false, false, Icons.IsEmoji(c.Style.Icon) ? c.Style.Icon : null));
        choices.Add(new("Delete task", IconChars.Delete, Danger: true));
        var pick = await ChoiceSheet.PickAsync(card.Task.Title, choices);
        if (pick < 0) return;
        if (pick == 0) { await OpenTaskAsync(card.Id); return; }
        if (pick == 1) { await RunAsync(svc => svc.SetDoneAsync(card.Id, !card.Task.Done)); return; }
        if (pick == choices.Count - 1) { await DeleteTaskAsync(card.Id, card.Task.Title); return; }
        var target = targets[pick - 2];
        await RunAsync(svc => svc.MoveToColumnAsync(board.Project.Id, card.Id, target.Bucket.Id));
    }

    public Task AddCardAsync(BoardColumn col, string text) => string.IsNullOrWhiteSpace(text) || Board is not { } board ? Task.CompletedTask : RunAsync(async svc =>
    {
        var t = await svc.AddAsync(text.Trim(), board.Project.Id.ToString(System.Globalization.CultureInfo.InvariantCulture));
        if (!col.IsDefault) await svc.MoveToColumnAsync(board.Project.Id, t.Id, col.Bucket.Id);
    });

    public async Task AddColumnAsync()
    {
        if (Board is not { } board) return;
        var name = await Shell.Current.DisplayPromptAsync("New column", "Column name", "Add", "Cancel", "In review");
        if (string.IsNullOrWhiteSpace(name)) return;
        await RunAsync(svc => svc.AddColumnAsync(board.Project.Id, name));
    }

    public async Task ColumnMenuAsync(BoardColumn col)
    {
        if (Board is not { } board) return;
        var choices = new List<Choice> { new("Rename", IconChars.Rename), new("Colour", IconChars.Palette), new("Icon", IconChars.Emoji) };
        if (!col.IsPermanent) choices.Add(new("Delete column", IconChars.Delete, "Its cards move to " + (board.Columns.FirstOrDefault(c => c.IsDefault)?.Bucket.Title ?? "To-Do"), Danger: true));
        var note = col.IsPermanent ? $"“{col.Bucket.Title}” is permanent: {(col.IsDone ? "cards here are done" : "new tasks land here")}. You can rename and restyle it, but not delete it." : null;
        var pick = await ChoiceSheet.PickAsync(col.Bucket.Title, choices, note);
        var id = board.Project.Id;
        switch (pick)
        {
            case 0:
                var name = await Shell.Current.DisplayPromptAsync("Rename column", "Column name", "Rename", "Cancel", initialValue: col.Bucket.Title);
                if (!string.IsNullOrWhiteSpace(name)) await RunAsync(svc => svc.RenameColumnAsync(id, col.Bucket.Id, name));
                break;
            case 1:
                var color = await Sheet<string>.ShowAsync(new ColorSheet("Column colour", col.Style.Color));
                if (color is not null) await RunAsync(svc => svc.SetColumnStyleAsync(id, col.Bucket.Id, col.Style with { Color = color.Length == 0 ? null : color }));
                break;
            case 2:
                var icon = await Sheet<string>.ShowAsync(new IconSheet("Column icon", col.Style.Icon));
                if (icon is not null) await RunAsync(svc => svc.SetColumnStyleAsync(id, col.Bucket.Id, col.Style with { Icon = icon.Length == 0 ? null : icon }));
                break;
            case 3:
                if (await Shell.Current.DisplayAlertAsync("Delete column?", $"“{col.Bucket.Title}” will be deleted. Its {col.Tasks.Count} cards move to To-Do.", "Delete", "Cancel"))
                    await RunAsync(svc => svc.DeleteColumnAsync(id, col.Bucket.Id));
                break;
        }
    }
}
