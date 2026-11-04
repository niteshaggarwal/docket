using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using Docket.Core;

namespace Docket.Mobile.ViewModels;

public sealed record ChipVm(string Text, Color Color);

/// <summary>One task row: title plus the line under it (My Day · list · steps · due · icons · categories).</summary>
public sealed partial class TaskRow : ObservableObject
{
    public TaskRow(VTask task, string project, bool showList)
    {
        Task = task;
        Project = project;
        ShowList = showList;
        Notes = TaskNotes.Parse(task.Description);
    }

    public VTask Task { get; private set; }
    public TaskNotes Notes { get; private set; }
    public string Project { get; }
    public bool ShowList { get; }

    public long Id => Task.Id;
    public string Title => Task.Title;
    public bool IsDone => Task.Done;
    public bool IsImportant => Task.IsFavorite;
    public string StarGlyph => Task.IsFavorite ? "" : "";
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
            var d = due.ToLocalTime().Date;
            var today = DateTime.Today;
            var label = d == today ? "Today" : d == today.AddDays(1) ? "Tomorrow" : d == today.AddDays(-1) ? "Yesterday"
                : d > today && d < today.AddDays(7) ? d.ToString("ddd", CultureInfo.CurrentCulture)
                : d.ToString(d.Year == today.Year ? "ddd, d MMM" : "d MMM yyyy", CultureInfo.CurrentCulture);
            return IsOverdue ? $"Overdue, {label}" : label;
        }
    }

    public string StepsText => Notes.Steps.Count > 0 ? $"{Notes.DoneCount} of {Notes.Steps.Count}" : "";
    public bool HasSteps => Notes.Steps.Count > 0;
    public bool HasNotes => Notes.Notes.Trim().Length > 0;
    public bool HasReminder => Task.NextReminder(DateTimeOffset.Now) is not null;
    public bool HasRepeat => RepeatRule.From(Task) is not null;
    public bool HasFiles => Task.Attachments is { Count: > 0 };
    public IReadOnlyList<ChipVm> Chips => (Task.Labels ?? []).Where(DocketService.IsCategory).Select(l => new ChipVm(l.Title, ColorOf(l.HexColor))).ToList();
    public bool HasChips => Chips.Count > 0;
    public bool HasMeta => InMyDay || ShowList || HasSteps || HasDue || HasReminder || HasRepeat || HasNotes || HasFiles || HasChips;

    public void Replace(VTask fresh)
    {
        Task = fresh;
        Notes = TaskNotes.Parse(fresh.Description);
        OnPropertyChanged(string.Empty);
    }

    public static Color ColorOf(string hex) => (hex ?? "").TrimStart('#') is { Length: 6 } h ? Color.FromArgb("#" + h) : Color.FromArgb("#8A8886");
}

/// <summary>A date heading inside Planned.</summary>
public sealed record GroupHeaderRow(string Title, int Count);

/// <summary>A row on the home screen: a smart list or one of the user's lists.</summary>
public sealed partial class ListEntry : ObservableObject
{
    public required string Key { get; init; }
    public required string Title { get; init; }
    public required string Glyph { get; init; }
    public Color? Tint { get; init; }
    public int Indent { get; init; }
    public bool IsGroup { get; init; }
    [ObservableProperty] public partial int Count { get; set; }
    public bool HasCount => Count > 0;
    partial void OnCountChanged(int value) => OnPropertyChanged(nameof(HasCount));
    public Thickness Margin => new(Indent * 20, 0, 0, 0);
}
