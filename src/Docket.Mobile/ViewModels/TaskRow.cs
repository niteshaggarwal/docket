using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using Docket.Core;
using Docket.Mobile.Services;

namespace Docket.Mobile.ViewModels;

public sealed record ChipVm(string Text, Color Color);

public sealed record AvatarVm(string Initials, Color Color, string Name);

/// <summary>
/// One task row: check, title, the line under it (My Day · list · subtasks · due · icons · priority · people · categories)
/// and the star. In nested lists it also carries its depth and an expand chevron.
/// </summary>
public sealed partial class TaskRow : ObservableObject
{
    public TaskRow(VTask task, string project, bool showList, TaskNode? node = null, bool expanded = false)
    {
        Task = task;
        Project = project;
        ShowList = showList;
        Node = node;
        Expanded = expanded;
        Notes = TaskNotes.Parse(task.Description);
    }

    public VTask Task { get; private set; }
    public TaskNotes Notes { get; private set; }
    public TaskNode? Node { get; }
    public string Project { get; }
    public bool ShowList { get; }
    public bool Expanded { get; }

    /// <summary>Set by the list when any row in it has subtasks, so titles line up.</summary>
    public bool ReserveChevron { get; set; }

    public long Id => Task.Id;
    public string Key => Node?.Key ?? Id.ToString(CultureInfo.InvariantCulture);
    public int Depth => Node?.Depth ?? 0;
    public Thickness RowMargin => new(12 + Math.Min(Depth, 6) * 22, 3, 12, 3);
    public bool HasChildren => Node?.HasChildren == true;
    public double ChevronWidth => ReserveChevron ? 26 : 0;
    public double ChevronRotation => Expanded ? 90 : 0;
    public bool IsSubtask => Depth > 0;

    public string Title => Task.Title;
    public bool IsDone => Task.Done;
    public bool IsImportant => Task.IsFavorite;
    public string StarGlyph => Task.IsFavorite ? IconChars.StarFilled : IconChars.Star;
    public string StarFont => Task.IsFavorite ? "FluentIconsFilled" : "FluentIcons";
    public bool InMyDay => DocketService.InMyDay(Task);
    public double TitleOpacity => Task.Done ? 0.55 : 1;
    public TextDecorations TitleDecorations => Task.Done ? TextDecorations.Strikethrough : TextDecorations.None;

    public bool HasDue => Task.Due is not null;
    public bool IsOverdue => Task.IsOverdue(DateTimeOffset.Now);
    public string DueText
    {
        get
        {
            if (Task.Due is not { } due) return "";
            var label = DayLabel(due.ToLocalTime().Date);
            return IsOverdue ? $"Overdue, {label}" : label;
        }
    }

    public static string DayLabel(DateTime d)
    {
        var today = DateTime.Today;
        return d == today ? "Today" : d == today.AddDays(1) ? "Tomorrow" : d == today.AddDays(-1) ? "Yesterday"
            : d > today && d < today.AddDays(7) ? d.ToString("ddd", CultureInfo.CurrentCulture)
            : d.ToString(d.Year == today.Year ? "ddd, d MMM" : "d MMM yyyy", CultureInfo.CurrentCulture);
    }

    public string ShortDue => Task.Due is { } d ? d.ToLocalTime().ToString(d.Year == DateTime.Today.Year ? "d MMM" : "d MMM yy", CultureInfo.CurrentCulture) : "";

    public string StepsText => Notes.Steps.Count > 0 ? $"{Notes.DoneCount} of {Notes.Steps.Count}" : "";
    public bool HasSteps => Notes.Steps.Count > 0;

    private int SubCount => Node is { } n ? n.Children.Count : Task.Subtasks.Count;
    private int SubDone => Node is { } n ? n.DoneChildren : Task.Subtasks.Count(s => s.Done);
    public bool HasSubtasks => SubCount > 0;
    public string SubtaskText => $"{SubDone} of {SubCount}";

    public int Priority => Math.Clamp(Task.Priority, 0, 5);
    public bool HasPriority => Priority > 0 && !Task.Done;
    public string PriorityText => DocketService.PriorityName(Priority);
    public string PriorityShort => Priority switch { >= 5 => "!!!", 4 => "!!", 3 => "!", _ => "" } + (Priority >= 3 ? " " : "") + PriorityText;
    public Color PriorityColor => Th.Priority(Priority).Now;
    public Color PriorityTint => PriorityColor.WithAlpha(Th.Dark ? 0.2f : 0.12f);

    public IReadOnlyList<AvatarVm> Avatars => (Task.Assignees ?? []).Take(3)
        .Select(u => new AvatarVm(DocketService.Initials(u), Th.AvatarColor(u.Username), DocketService.DisplayName(u))).ToList();
    public bool HasAssignees => Task.Assignees is { Count: > 0 };
    public string MoreAssignees => Task.Assignees is { Count: > 3 } a ? $"+{a.Count - 3}" : "";

    public bool HasNotes => Notes.Notes.Trim().Length > 0;
    public bool HasReminder => Task.NextReminder(DateTimeOffset.Now) is not null;
    public bool HasRepeat => RepeatRule.From(Task) is not null;
    public bool HasFiles => Task.Attachments is { Count: > 0 };
    public IReadOnlyList<ChipVm> Chips => (Task.Labels ?? []).Where(DocketService.IsCategory).Select(l => new ChipVm(l.Title, ColorOf(l.HexColor))).ToList();
    public bool HasChips => Chips.Count > 0;
    public bool HasMeta => InMyDay || ShowList || HasSteps || HasSubtasks || HasDue || HasReminder || HasRepeat || HasNotes || HasFiles || HasChips || HasPriority || HasAssignees;

    public void Replace(VTask fresh)
    {
        // Update answers can leave out relations and people; keep what the list fetched.
        fresh.RelatedTasks ??= Task.RelatedTasks;
        fresh.Assignees ??= Task.Assignees;
        fresh.Attachments ??= Task.Attachments;
        fresh.Labels ??= Task.Labels;
        Task = fresh;
        Notes = TaskNotes.Parse(fresh.Description);
        OnPropertyChanged(string.Empty);
    }

    public static Color ColorOf(string hex) => (hex ?? "").TrimStart('#') is { Length: 6 } h ? Color.FromArgb("#" + h) : Color.FromArgb("#8A8886");
}

/// <summary>A heading inside a grouped list (Planned, Upcoming).</summary>
public sealed record GroupHeaderRow(string Title, int Count)
{
    public bool OnImage { get; init; }
}

/// <summary>A row on the home screen: a smart list, a view, or one of the user's lists.</summary>
public sealed partial class ListEntry : ObservableObject
{
    public required string Key { get; init; }
    public required string Title { get; init; }
    public required string Glyph { get; init; }
    public Color? Tint { get; init; }
    public int Indent { get; init; }
    public bool IsGroup { get; init; }
    /// <summary>True when the icon is an emoji, drawn in colour at its own size.</summary>
    public bool IsEmoji { get; init; }
    public string IconFont => IsEmoji ? "" : "FluentIcons";
    public double IconSize => IsEmoji ? 18 : 20;
    [ObservableProperty] public partial int Count { get; set; }
    public bool HasCount => Count > 0;
    partial void OnCountChanged(int value) => OnPropertyChanged(nameof(HasCount));
    public Thickness Margin => new(Indent * 20, 0, 0, 0);
}
