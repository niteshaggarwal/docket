using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using Docket.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace Docket.App.Services;

/// <summary>A category chip on a task row.</summary>
public sealed record Chip(string Text, Brush Fill, Brush Dot);

/// <summary>A task as shown in lists, trees and board columns.</summary>
public sealed class TaskItem : INotifyPropertyChanged
{
    public TaskItem(VTask task, string? projectTitle = null, bool showList = false, TaskNode? node = null, bool expanded = false)
    {
        Task = task;
        ProjectTitle = projectTitle ?? "";
        ShowList = showList;
        Notes = TaskNotes.Parse(task.Description);
        Node = node;
        IsExpanded = expanded;
    }

    public VTask Task { get; private set; }
    public TaskNotes Notes { get; private set; }
    public TaskNode? Node { get; }
    public long Id => Task.Id;
    public string Title => Task.Title;
    public string ProjectTitle { get; }
    public bool ShowList { get; }

    // ---------- tree ----------
    public int Depth => Node?.Depth ?? 0;
    public string Key => Node?.Key ?? Id.ToString(CultureInfo.InvariantCulture);
    public bool HasChildren => Node?.HasChildren == true;
    public bool IsExpanded { get; set; }
    public Thickness Indent => new(Depth * 24, 0, 0, 0);
    /// <summary>False when nothing in the list has subtasks, so rows don't reserve room for a chevron.</summary>
    public bool TreeColumn { get; init; } = true;
    public double ChevronWidth => TreeColumn ? 28 : 0;
    public double ChevronOpacity => HasChildren ? 1 : 0;
    public bool ChevronEnabled => HasChildren;
    public string ChevronGlyph => IsExpanded ? "" : "";
    public string ChevronName => IsExpanded ? $"Collapse {Title}" : $"Expand {Title}";

    public int SubtaskCount => Node?.Children.Count ?? Task.Subtasks.Count;
    public int SubtaskDone => Node?.DoneChildren ?? Task.Subtasks.Count(s => s.Done);
    public bool HasSubtasks => SubtaskCount > 0;
    public string SubtaskText => $"{SubtaskDone} of {SubtaskCount}";
    public string SubtaskTip => $"{SubtaskDone} of {SubtaskCount} subtasks done";

    // ---------- status ----------
    public bool IsDone
    {
        get => Task.Done;
        set { if (Task.Done != value) { Task.Done = value; OnChanged(); OnChanged(nameof(TitleOpacity)); } }
    }

    public bool IsImportant => Task.IsFavorite;
    public string StarGlyph => Task.IsFavorite ? "" : "";
    public string StarName => Task.IsFavorite ? "Remove importance" : "Mark as important";
    public double TitleOpacity => Task.Done ? 0.6 : 1;
    public Windows.UI.Text.TextDecorations TitleDecorations => Task.Done ? Windows.UI.Text.TextDecorations.Strikethrough : Windows.UI.Text.TextDecorations.None;
    public bool InMyDay => DocketService.InMyDay(Task);

    public string DueText
    {
        get
        {
            if (Task.Due is not { } due) return "";
            var local = due.ToLocalTime();
            var today = DateTimeOffset.Now.Date;
            var day = local.Date;
            var label = day == today ? "Today" : day == today.AddDays(1) ? "Tomorrow" : day == today.AddDays(-1) ? "Yesterday"
                : day > today && day < today.AddDays(7) ? local.ToString("ddd", CultureInfo.CurrentCulture)
                : local.ToString(day.Year == today.Year ? "ddd, d MMM" : "d MMM yyyy", CultureInfo.CurrentCulture);
            return IsOverdue ? $"Overdue, {label}" : $"Due {label}";
        }
    }

    public bool IsOverdue => Task.IsOverdue(DateTimeOffset.Now);
    public bool HasDue => Task.Due is not null;
    public bool HasPriority => Task.Priority > 0;
    public string PriorityText => DocketService.PriorityName(Task.Priority);
    public Brush PriorityFore => Ui.Hex(Ui.Readable(Ui.PriorityHex(Task.Priority)));
    public Brush PriorityFill => Ui.Hex(Ui.PriorityHex(Task.Priority), Ui.IsDark ? 0.24 : 0.14);
    public string PriorityTip => $"Priority: {PriorityText}";

    public string StepsText => Notes.Steps.Count > 0 ? $"{Notes.DoneCount} of {Notes.Steps.Count} steps" : "";
    public bool HasSteps => Notes.Steps.Count > 0;
    public bool HasNotes => Notes.Notes.Trim().Length > 0;
    public bool HasReminder => Task.NextReminder(DateTimeOffset.Now) is not null;
    public string ReminderTip => Task.NextReminder(DateTimeOffset.Now) is { } r ? $"Reminder {Ui.FriendlyDate(r)}" : "Reminder";
    public bool HasRepeat => RepeatRule.From(Task) is not null;
    public string RepeatTip => RepeatRule.From(Task)?.Describe() ?? "Repeats";
    public bool HasFiles => Task.Attachments is { Count: > 0 };
    public string FilesTip => Task.Attachments is { Count: > 0 } a ? (a.Count == 1 ? "1 file attached" : $"{a.Count} files attached") : "Files";
    public Brush DueBrush => IsOverdue ? Ui.Brush("DocketOverdueBrush") : Ui.Brush("TextFillColorSecondaryBrush");

    public IReadOnlyList<VUser> Assignees => Task.Assignees ?? [];
    public bool HasAssignees => Task.Assignees is { Count: > 0 };

    public IReadOnlyList<Chip> Chips => (Task.Labels ?? []).Where(DocketService.IsCategory).Select(l => new Chip(l.Title, Ui.Hex(l.HexColor, Ui.IsDark ? 0.28 : 0.16), Ui.Hex(Ui.Readable(l.HexColor)))).ToList();
    public bool HasChips => Chips.Count > 0;

    /// <summary>The second line under the title, as in a To Do row.</summary>
    public bool HasMeta => InMyDay || ShowList || HasSteps || HasSubtasks || HasDue || HasReminder || HasRepeat || HasNotes || HasFiles || HasChips || HasPriority || HasAssignees;

    public void Replace(VTask fresh)
    {
        fresh.RelatedTasks ??= Task.RelatedTasks;
        fresh.Attachments ??= Task.Attachments;
        Task = fresh;
        Notes = TaskNotes.Parse(fresh.Description);
        OnChanged(string.Empty);
    }

    public static Brush ColorBrush(string hex, double alpha) => Ui.Hex(hex, alpha);

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnChanged([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

/// <summary>A heading row inside a list (Planned's date groups, search sections).</summary>
public sealed record GroupHeader(string Title, int Count)
{
    public string CountText => Count.ToString(CultureInfo.CurrentCulture);
}
