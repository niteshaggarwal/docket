using System.Text.RegularExpressions;

namespace Docket.Core;

/// <summary>Result of parsing a quick-add line such as "Send invoice to RNG tomorrow !3".</summary>
public sealed record QuickAddResult(string Title, DateTimeOffset? Due, int Priority);

/// <summary>
/// Small, predictable quick-add parser: "today", "tomorrow", weekday names, "in N days",
/// a date like 12/11 (day/month), and "!1".."!5" for priority. Everything else is the title.
/// </summary>
public static partial class QuickAdd
{
    public static QuickAddResult Parse(string input, DateTimeOffset now)
    {
        var text = " " + input.Trim() + " ";
        DateTimeOffset? due = null;
        var priority = 0;

        var p = PriorityRx().Match(text);
        if (p.Success) { priority = int.Parse(p.Groups[1].Value); text = text.Remove(p.Index, p.Length).Insert(p.Index, " "); }

        var today = new DateTimeOffset(now.Year, now.Month, now.Day, 0, 0, 0, now.Offset);
        DateTimeOffset EndOf(DateTimeOffset day) => day.AddHours(18); // default due time: 6 pm

        Match m;
        if ((m = Word("today").Match(text)).Success) due = EndOf(today);
        else if ((m = Word("tomorrow").Match(text)).Success) due = EndOf(today.AddDays(1));
        else if ((m = InDaysRx().Match(text)).Success) due = EndOf(today.AddDays(int.Parse(m.Groups[1].Value)));
        else if ((m = WeekdayRx().Match(text)).Success)
        {
            var target = Enum.Parse<DayOfWeek>(Expand(m.Groups[1].Value), ignoreCase: true);
            var delta = ((int)target - (int)today.DayOfWeek + 7) % 7;
            due = EndOf(today.AddDays(delta == 0 ? 7 : delta));
        }
        else if ((m = DateRx().Match(text)).Success)
        {
            var d = int.Parse(m.Groups[1].Value); var mo = int.Parse(m.Groups[2].Value);
            if (mo is >= 1 and <= 12 && d >= 1 && d <= DateTime.DaysInMonth(today.Year, mo))
            {
                var candidate = new DateTimeOffset(today.Year, mo, d, 0, 0, 0, now.Offset);
                if (candidate < today) candidate = candidate.AddYears(1);
                due = EndOf(candidate);
            }
            else m = Match.Empty;
        }

        if (m.Success) text = text.Remove(m.Index, m.Length).Insert(m.Index, " ");
        var title = SpacesRx().Replace(text, " ").Trim();
        return new QuickAddResult(title.Length == 0 ? input.Trim() : title, due, priority);
    }

    private static string Expand(string day) => day.ToLowerInvariant() switch
    {
        "mon" => "Monday", "tue" or "tues" => "Tuesday", "wed" => "Wednesday", "thu" or "thur" or "thurs" => "Thursday",
        "fri" => "Friday", "sat" => "Saturday", "sun" => "Sunday", var full => full,
    };

    private static Regex Word(string w) => new($@"(?<=\s)(?:{w})(?=\s)", RegexOptions.IgnoreCase);

    [GeneratedRegex(@"(?<=\s)!([1-5])(?=\s)")] private static partial Regex PriorityRx();
    [GeneratedRegex(@"(?<=\s)in\s+(\d{1,3})\s+days?(?=\s)", RegexOptions.IgnoreCase)] private static partial Regex InDaysRx();
    [GeneratedRegex(@"(?<=\s)(?:on\s+)?(monday|tuesday|wednesday|thursday|friday|saturday|sunday|mon|tues?|wed|thu(?:rs?)?|fri|sat|sun)(?=\s)", RegexOptions.IgnoreCase)] private static partial Regex WeekdayRx();
    [GeneratedRegex(@"(?<=\s)(\d{1,2})/(\d{1,2})(?=\s)")] private static partial Regex DateRx();
    [GeneratedRegex(@"\s+")] private static partial Regex SpacesRx();
}
