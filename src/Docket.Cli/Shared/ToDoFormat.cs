using System.Globalization;
using System.Text;
using Docket.Core;

namespace Docket.Shared;

/// <summary>
/// Task text with the To Do details (important, My Day, steps, repeat, next reminder, categories).
/// Shared by the CLI and the Claude connector (linked into both projects).
/// </summary>
internal static class ToDoFormat
{
    private static readonly CultureInfo En = CultureInfo.GetCultureInfo("en-GB");

    public static string When(DateTimeOffset at, DateTimeOffset now)
    {
        var local = at.ToOffset(now.Offset);
        var day = local.Date;
        var label = day == now.Date ? "today" : day == now.Date.AddDays(1) ? "tomorrow"
            : day > now.Date && day < now.Date.AddDays(7) ? local.ToString("ddd", En)
            : local.ToString(day.Year == now.Year ? "d MMM" : "d MMM yyyy", En);
        return $"{label} {local.ToString("HH:mm", En)}";
    }

    /// <summary>"#12 [ ] ★ Send proposal · Work · due today · high · My Day · 2/5 steps · ↻ Weekly · ⏰ tomorrow 09:00 · #client"</summary>
    public static string Line(TaskInfo info, DateTimeOffset now)
    {
        var t = info.Task;
        var parts = new List<string> { $"#{t.Id} {(t.Done ? "[x]" : "[ ]")} {(t.IsFavorite ? "★ " : "")}{t.Title}", info.Project };
        var due = TaskFormat.Due(t, now); if (due.Length > 0) parts.Add("due " + due);
        var pr = TaskFormat.Priority(t.Priority); if (pr.Length > 0) parts.Add(pr);
        if (info.Column is { Length: > 0 } c) parts.Add(c);
        if (DocketService.InMyDay(t)) parts.Add("My Day");
        var notes = TaskNotes.Parse(t.Description);
        if (notes.Steps.Count > 0) parts.Add($"{notes.DoneCount}/{notes.Steps.Count} steps");
        if (RepeatRule.From(t) is { } rule) parts.Add("↻ " + rule.Describe());
        if (t.NextReminder(now) is { } rem) parts.Add("⏰ " + When(rem, now));
        var cats = (t.Labels ?? []).Where(DocketService.IsCategory).Select(l => "#" + l.Title.Replace(' ', '-')).ToList();
        if (cats.Count > 0) parts.Add(string.Join(" ", cats));
        return string.Join(" · ", parts);
    }

    public static string List(IEnumerable<TaskInfo> tasks, DateTimeOffset now, string empty)
    {
        var sb = new StringBuilder();
        foreach (var t in tasks) sb.AppendLine(Line(t, now));
        return sb.Length == 0 ? empty : sb.ToString().TrimEnd();
    }

    /// <summary>Open tasks, then a "Completed (n)" section (capped).</summary>
    public static string Content(ListContent c, DateTimeOffset now, string empty, int maxDone = 10)
    {
        var sb = new StringBuilder(List(c.Open, now, empty));
        if (c.Completed.Count > 0)
        {
            sb.AppendLine().AppendLine().AppendLine($"Completed ({c.Completed.Count})");
            foreach (var t in c.Completed.Take(maxDone)) sb.AppendLine(Line(t, now));
            if (c.Completed.Count > maxDone) sb.AppendLine($"… and {c.Completed.Count - maxDone} more");
        }
        return sb.ToString().TrimEnd();
    }

    public static string Planned(IEnumerable<TaskInfo> tasks, DateTimeOffset now)
    {
        var order = new[] { "Earlier", "Today", "Tomorrow", "This week", "Later", "No date" };
        var groups = tasks.GroupBy(t => DocketService.PlannedGroup(t.Task, now)).OrderBy(g => Array.IndexOf(order, g.Key)).ToList();
        if (groups.Count == 0) return "Nothing planned.";
        var sb = new StringBuilder();
        foreach (var g in groups)
        {
            sb.AppendLine($"{g.Key} ({g.Count()})");
            foreach (var t in g) sb.AppendLine("  " + Line(t, now));
            sb.AppendLine();
        }
        return sb.ToString().TrimEnd();
    }

    public static string Suggestions(IEnumerable<Suggestion> list, DateTimeOffset now)
    {
        var sb = new StringBuilder();
        foreach (var g in list.GroupBy(s => s.Group))
        {
            sb.AppendLine(g.Key);
            foreach (var s in g) sb.AppendLine("  " + Line(s.Task, now));
            sb.AppendLine();
        }
        return sb.Length == 0 ? "No suggestions right now." : sb.ToString().TrimEnd();
    }

    public static string Detail(TaskDetail d, DateTimeOffset now)
    {
        var t = d.Info.Task;
        var notes = TaskNotes.Parse(t.Description);
        var sb = new StringBuilder();
        sb.AppendLine($"#{t.Id} {(t.IsFavorite ? "★ " : "")}{t.Title}");
        sb.AppendLine($"Status: {(t.Done ? "done" : "open")}{(t.Done && VDates.OrNull(t.DoneAt) is { } da ? $" ({da.ToOffset(now.Offset).ToString("d MMM HH:mm", En)})" : "")}");
        sb.AppendLine($"List: {d.Info.Project}");
        if (d.Info.Column is { } c) sb.AppendLine($"Column: {c}{(d.Columns.Count > 0 ? $" (columns: {string.Join(", ", d.Columns)})" : "")}");
        sb.AppendLine($"Important: {(t.IsFavorite ? "yes" : "no")}");
        sb.AppendLine($"My Day: {(DocketService.InMyDay(t) ? "yes" : "no")}");
        var due = TaskFormat.Due(t, now); sb.AppendLine($"Due: {(due.Length > 0 ? due : "none")}");
        sb.AppendLine($"Repeat: {RepeatRule.From(t)?.Describe() ?? "none"}");
        var rems = (t.Reminders ?? []).Select(r => VDates.OrNull(r.Reminder)).OfType<DateTimeOffset>().OrderBy(x => x).ToList();
        sb.AppendLine($"Reminders: {(rems.Count > 0 ? string.Join(", ", rems.Select(r => When(r, now) + (r < now ? " (past)" : ""))) : "none")}");
        sb.AppendLine($"Priority: {(t.Priority > 0 ? $"{TaskFormat.Priority(t.Priority)} ({t.Priority})" : "none")}");
        var cats = (t.Labels ?? []).Where(DocketService.IsCategory).Select(l => l.Title).ToList();
        sb.AppendLine($"Categories: {(cats.Count > 0 ? string.Join(", ", cats) : "none")}");
        sb.AppendLine($"Created: {t.Created.ToOffset(now.Offset).ToString("d MMM yyyy HH:mm", En)}");
        if (notes.Steps.Count > 0)
        {
            sb.AppendLine().AppendLine($"Steps ({notes.DoneCount}/{notes.Steps.Count}):");
            for (var i = 0; i < notes.Steps.Count; i++) sb.AppendLine($"  {i + 1}. [{(notes.Steps[i].Done ? "x" : " ")}] {notes.Steps[i].Title}");
        }
        if (notes.Notes.Length > 0) sb.AppendLine().AppendLine("Notes:").AppendLine(notes.Notes);
        if (t.Attachments is { Count: > 0 } files)
            sb.AppendLine().AppendLine("Files: " + string.Join(", ", files.Select(f => $"{f.File.Name} ({Size(f.File.Size)})")));
        if (d.Comments.Count > 0)
        {
            sb.AppendLine().AppendLine("Comments:");
            foreach (var cm in d.Comments)
                sb.AppendLine($"- {cm.Created.ToOffset(now.Offset).ToString("d MMM HH:mm", En)} {cm.Author?.Username}: {TaskFormat.StripHtml(cm.Comment)}");
        }
        return sb.ToString().TrimEnd();
    }

    private static string Size(long b) => b < 1024 ? $"{b} B" : b < 1048576 ? $"{b / 1024.0:0.#} KB" : $"{b / 1048576.0:0.#} MB";
}
