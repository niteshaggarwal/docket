using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace Docket.Core;

public sealed record Step(string Title, bool Done);

/// <summary>
/// A task's description split into plain-text notes and steps. Steps are stored as a Vikunja
/// checklist (the editor's taskList), so Vikunja's own web app shows them as checkboxes too.
/// </summary>
public sealed partial record TaskNotes(string Notes, IReadOnlyList<Step> Steps)
{
    public static TaskNotes Parse(string? html)
    {
        if (string.IsNullOrWhiteSpace(html)) return new("", []);
        var steps = new List<Step>();
        var lists = TaskListRx().Matches(html);
        var rest = html;
        if (lists.Count > 0)
        {
            var last = lists[^1];
            foreach (Match item in ItemRx().Matches(last.Value))
            {
                var text = TaskFormat.StripHtml(item.Groups["body"].Value).Replace('\n', ' ').Trim();
                if (text.Length > 0) steps.Add(new Step(text, item.Groups["checked"].Value == "true"));
            }
            rest = html.Remove(last.Index, last.Length);
        }
        return new(TaskFormat.StripHtml(rest), steps);
    }

    public string ToHtml()
    {
        var sb = new StringBuilder();
        foreach (var line in Notes.Replace("\r\n", "\n").Split('\n'))
            sb.Append("<p>").Append(WebUtility.HtmlEncode(line)).Append("</p>");
        if (Notes.Trim().Length == 0) sb.Clear();
        if (Steps.Count > 0)
        {
            sb.Append("<ul data-type=\"taskList\">");
            foreach (var s in Steps)
            {
                var c = s.Done ? "true" : "false";
                sb.Append($"<li data-checked=\"{c}\" data-type=\"taskItem\"><label><input type=\"checkbox\"{(s.Done ? " checked=\"checked\"" : "")}><span></span></label><div><p>")
                  .Append(WebUtility.HtmlEncode(s.Title)).Append("</p></div></li>");
            }
            sb.Append("</ul>");
        }
        return sb.ToString();
    }

    public int DoneCount => Steps.Count(s => s.Done);

    [GeneratedRegex(@"<ul[^>]*data-type=""taskList""[^>]*>.*?</ul>", RegexOptions.Singleline | RegexOptions.IgnoreCase)]
    private static partial Regex TaskListRx();

    [GeneratedRegex(@"<li[^>]*data-checked=""(?<checked>true|false)""[^>]*>(?<body>.*?)</li>", RegexOptions.Singleline | RegexOptions.IgnoreCase)]
    private static partial Regex ItemRx();
}
