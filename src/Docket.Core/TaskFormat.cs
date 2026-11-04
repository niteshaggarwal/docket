using System.Globalization;
using System.Text;

namespace Docket.Core;

/// <summary>Plain-text rendering shared by the CLI and the Claude connector.</summary>
public static class TaskFormat
{
    private static readonly CultureInfo En = CultureInfo.GetCultureInfo("en-GB");

    public static string Priority(int p) => p switch { >= 5 => "do now", 4 => "urgent", 3 => "high", 2 => "medium", 1 => "low", _ => "" };

    public static string Due(VTask t, DateTimeOffset now)
    {
        if (t.Due is not { } due) return "";
        var local = due.ToOffset(now.Offset);
        var day = local.Date;
        var today = now.Date;
        var label = day == today ? "today" : day == today.AddDays(1) ? "tomorrow" : day == today.AddDays(-1) ? "yesterday"
            : day > today && day < today.AddDays(7) ? local.ToString("dddd", En)
            : local.ToString(day.Year == today.Year ? "d MMM" : "d MMM yyyy", En);
        if (local.TimeOfDay != new TimeSpan(18, 0, 0) && local.TimeOfDay != TimeSpan.Zero) label += " " + local.ToString("HH:mm", En);
        return t.IsOverdue(now) ? $"overdue, {label}" : label;
    }

    /// <summary>One line: "#12 [ ] Send proposal · Work · due today · high · Doing"</summary>
    public static string Line(TaskInfo info, DateTimeOffset now)
    {
        var t = info.Task;
        var parts = new List<string> { $"#{t.Id} {(t.Done ? "[x]" : "[ ]")} {t.Title}", info.Project };
        var due = Due(t, now); if (due.Length > 0) parts.Add("due " + due);
        var pr = Priority(t.Priority); if (pr.Length > 0) parts.Add(pr);
        if (info.Column is { Length: > 0 } c) parts.Add(c);
        if (t.Labels is { Count: > 0 } ls) parts.Add(string.Join(" ", ls.Select(l => "#" + l.Title.Replace(' ', '-'))));
        return string.Join(" · ", parts);
    }

    public static string List(IReadOnlyList<TaskInfo> tasks, DateTimeOffset now, string empty)
    {
        if (tasks.Count == 0) return empty;
        var sb = new StringBuilder();
        foreach (var t in tasks) sb.AppendLine(Line(t, now));
        return sb.ToString().TrimEnd();
    }

    public static string Detail(TaskDetail d, DateTimeOffset now)
    {
        var t = d.Info.Task;
        var sb = new StringBuilder();
        sb.AppendLine($"#{t.Id} {t.Title}");
        sb.AppendLine($"Status: {(t.Done ? "done" : "open")}{(VDates.OrNull(t.DoneAt) is { } da && t.Done ? $" ({da.ToOffset(now.Offset).ToString("d MMM HH:mm", En)})" : "")}");
        sb.AppendLine($"Project: {d.Info.Project}");
        if (d.Info.Column is { } c) sb.AppendLine($"Column: {c}{(d.Columns.Count > 0 ? $" (columns: {string.Join(", ", d.Columns)})" : "")}");
        var due = Due(t, now); sb.AppendLine($"Due: {(due.Length > 0 ? due : "none")}");
        sb.AppendLine($"Priority: {(t.Priority > 0 ? $"{Priority(t.Priority)} ({t.Priority})" : "none")}");
        if (t.Labels is { Count: > 0 } ls) sb.AppendLine($"Labels: {string.Join(", ", ls.Select(l => l.Title))}");
        sb.AppendLine($"Created: {t.Created.ToOffset(now.Offset).ToString("d MMM yyyy HH:mm", En)}");
        if (!string.IsNullOrWhiteSpace(t.Description)) { sb.AppendLine(); sb.AppendLine(StripHtml(t.Description)); }
        if (d.Comments.Count > 0)
        {
            sb.AppendLine(); sb.AppendLine("Comments:");
            foreach (var cm in d.Comments)
                sb.AppendLine($"- {cm.Created.ToOffset(now.Offset).ToString("d MMM HH:mm", En)} {cm.Author?.Username}: {StripHtml(cm.Comment)}");
        }
        return sb.ToString().TrimEnd();
    }

    public static string Board(string project, IReadOnlyList<BoardColumnInfo> columns, DateTimeOffset now)
    {
        var sb = new StringBuilder($"{project}\n");
        foreach (var c in columns)
        {
            sb.AppendLine().AppendLine($"{c.Title} ({c.Tasks.Count})");
            foreach (var t in c.Tasks.Take(c.IsDone ? 10 : 100))
            {
                var due = Due(t.Task, now);
                sb.AppendLine($"  #{t.Id} {t.Task.Title}{(due.Length > 0 ? $" · due {due}" : "")}");
            }
            if (c.IsDone && c.Tasks.Count > 10) sb.AppendLine($"  … and {c.Tasks.Count - 10} more");
        }
        return sb.ToString().TrimEnd();
    }

    /// <summary>Vikunja stores descriptions and comments as HTML; show them as text.</summary>
    public static string StripHtml(string html)
    {
        var s = System.Text.RegularExpressions.Regex.Replace(html, @"<(br|/p|/li|/h\d)\s*/?>", "\n", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        s = System.Text.RegularExpressions.Regex.Replace(s, "<li>", "- ", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        s = System.Text.RegularExpressions.Regex.Replace(s, "<[^>]+>", "");
        return System.Net.WebUtility.HtmlDecode(s).Trim();
    }
}
