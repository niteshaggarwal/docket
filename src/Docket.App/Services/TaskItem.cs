using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using Docket.Core;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace Docket.App.Services;

/// <summary>A category chip on a task row.</summary>
public sealed record Chip(string Text, Brush Fill, Brush Dot);

/// <summary>A task as shown in lists and board columns.</summary>
public sealed class TaskItem : INotifyPropertyChanged
{
    public TaskItem(VTask task, string? projectTitle = null, bool showList = false)
    {
        Task = task;
        ProjectTitle = projectTitle ?? "";
        ShowList = showList;
        Notes = TaskNotes.Parse(task.Description);
    }

    public VTask Task { get; private set; }
    public TaskNotes Notes { get; private set; }
    public long Id => Task.Id;
    public string Title => Task.Title;
    public string ProjectTitle { get; }
    public bool ShowList { get; }

    public bool IsDone
    {
        get => Task.Done;
        set { if (Task.Done != value) { Task.Done = value; OnChanged(); OnChanged(nameof(TitleOpacity)); } }
    }

    public bool IsImportant => Task.IsFavorite;
    public string StarGlyph => Task.IsFavorite ? "" : "";
    public double TitleOpacity => Task.Done ? 0.6 : 1;
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
    public string PriorityText => Task.Priority switch { >= 5 => "Do now", 4 => "Urgent", 3 => "High", 2 => "Medium", 1 => "Low", _ => "" };
    public string StepsText => Notes.Steps.Count > 0 ? $"{Notes.DoneCount} of {Notes.Steps.Count}" : "";
    public bool HasSteps => Notes.Steps.Count > 0;
    public bool HasNotes => Notes.Notes.Trim().Length > 0;
    public bool HasReminder => Task.NextReminder(DateTimeOffset.Now) is not null;
    public bool HasRepeat => RepeatRule.From(Task) is not null;
    public bool HasFiles => Task.Attachments is { Count: > 0 };
    public Brush DueBrush => IsOverdue ? (Brush)Application.Current.Resources["DocketOverdueBrush"] : (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"];

    public IReadOnlyList<Chip> Chips => (Task.Labels ?? []).Where(DocketService.IsCategory).Select(l => new Chip(l.Title, ColorBrush(l.HexColor, 0.18), ColorBrush(l.HexColor, 1))).ToList();
    public bool HasChips => Chips.Count > 0;

    /// <summary>The second line under the title, as in a To Do row: My Day · list · steps · due · icons.</summary>
    public bool HasMeta => InMyDay || ShowList || HasSteps || HasDue || HasReminder || HasRepeat || HasNotes || HasFiles || HasChips;

    public string Meta => ""; // kept for the board page binding

    public void Replace(VTask fresh)
    {
        Task = fresh;
        Notes = TaskNotes.Parse(fresh.Description);
        OnChanged(string.Empty);
    }

    public static Brush ColorBrush(string hex, double alpha)
    {
        var h = (hex ?? "").TrimStart('#');
        if (h.Length != 6) h = "8a8886";
        var c = Windows.UI.Color.FromArgb((byte)(alpha * 255),
            byte.Parse(h[..2], NumberStyles.HexNumber, CultureInfo.InvariantCulture),
            byte.Parse(h[2..4], NumberStyles.HexNumber, CultureInfo.InvariantCulture),
            byte.Parse(h[4..], NumberStyles.HexNumber, CultureInfo.InvariantCulture));
        return new SolidColorBrush(c);
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnChanged([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

/// <summary>A heading row inside a list (Planned's date groups, search sections).</summary>
public sealed record GroupHeader(string Title, int Count)
{
    public string CountText => Count.ToString(CultureInfo.CurrentCulture);
}
