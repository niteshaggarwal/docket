using System.Globalization;
using System.Text.RegularExpressions;

namespace Docket.Core;

public enum RepeatUnit { Days, Weeks, Months, Years }

/// <summary>
/// How a task repeats. Simple intervals (every N days or weeks, every month) use Vikunja's own
/// repeat, so they also work from Vikunja's web app. Weekdays and every N months/years (other
/// than monthly) aren't supported by Vikunja, so Docket stores them as a "↻ ..." label and moves
/// the dates itself when the task is completed through Docket, the CLI or Claude.
/// </summary>
public sealed partial record RepeatRule(int Interval, RepeatUnit Unit, bool WeekdaysOnly = false)
{
    public const string LabelPrefix = "↻ ";

    public static readonly RepeatRule Daily = new(1, RepeatUnit.Days);
    public static readonly RepeatRule Weekdays = new(1, RepeatUnit.Days, true);
    public static readonly RepeatRule Weekly = new(1, RepeatUnit.Weeks);
    public static readonly RepeatRule Monthly = new(1, RepeatUnit.Months);
    public static readonly RepeatRule Yearly = new(1, RepeatUnit.Years);

    /// <summary>True when Vikunja can do this repeat by itself.</summary>
    public bool IsNative => !WeekdaysOnly && (Unit is RepeatUnit.Days or RepeatUnit.Weeks || (Unit == RepeatUnit.Months && Interval == 1));

    public string Label => WeekdaysOnly ? LabelPrefix + "weekdays"
        : LabelPrefix + (Interval == 1 ? Unit switch { RepeatUnit.Months => "monthly", RepeatUnit.Years => "yearly", RepeatUnit.Weeks => "weekly", _ => "daily" }
                                       : $"every {Interval} {Unit.ToString().ToLowerInvariant()}");

    public string Describe() => WeekdaysOnly ? "Weekdays"
        : Interval == 1 ? Unit switch { RepeatUnit.Days => "Daily", RepeatUnit.Weeks => "Weekly", RepeatUnit.Months => "Monthly", _ => "Yearly" }
        : $"Every {Interval} {Unit.ToString().ToLowerInvariant()}";

    /// <summary>Reads the rule from a task: a Docket "↻" label first, then Vikunja's native repeat.</summary>
    public static RepeatRule? From(VTask t)
    {
        foreach (var l in t.Labels ?? [])
            if (l.Title.StartsWith(LabelPrefix, StringComparison.Ordinal) && TryParse(l.Title[LabelPrefix.Length..], out var r)) return r;
        if (t.RepeatMode == 1) return Monthly;
        if (t.RepeatAfter <= 0) return null;
        if (t.RepeatAfter % 604800 == 0) return new((int)(t.RepeatAfter / 604800), RepeatUnit.Weeks);
        if (t.RepeatAfter % 86400 == 0) return new((int)(t.RepeatAfter / 86400), RepeatUnit.Days);
        return new(Math.Max(1, (int)Math.Round(t.RepeatAfter / 86400.0)), RepeatUnit.Days);
    }

    /// <summary>Parses "daily", "weekdays", "weekly", "monthly", "yearly", "every 3 days|weeks|months|years".</summary>
    public static bool TryParse(string text, out RepeatRule rule)
    {
        var s = text.Trim().ToLowerInvariant();
        rule = s switch
        {
            "daily" or "every day" => Daily, "weekdays" or "every weekday" => Weekdays, "weekly" or "every week" => Weekly,
            "monthly" or "every month" => Monthly, "yearly" or "annually" or "every year" => Yearly, _ => null!,
        };
        if (rule is not null) return true;
        var m = EveryRx().Match(s);
        if (!m.Success) return false;
        var n = int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
        if (n < 1 || n > 999) return false;
        var unit = m.Groups[2].Value[0] switch { 'd' => RepeatUnit.Days, 'w' => RepeatUnit.Weeks, 'm' => RepeatUnit.Months, _ => RepeatUnit.Years };
        rule = new RepeatRule(n, unit);
        return true;
    }

    /// <summary>The next occurrence after <paramref name="from"/>.</summary>
    public DateTimeOffset Next(DateTimeOffset from)
    {
        if (WeekdaysOnly)
        {
            var d = from.AddDays(1);
            while (d.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday) d = d.AddDays(1);
            return d;
        }
        return Unit switch
        {
            RepeatUnit.Days => from.AddDays(Interval), RepeatUnit.Weeks => from.AddDays(7 * Interval),
            RepeatUnit.Months => from.AddMonths(Interval), _ => from.AddYears(Interval),
        };
    }

    /// <summary>Vikunja's native fields for this rule (0/0 when Docket handles it).</summary>
    public (long After, int Mode) NativeFields => !IsNative ? (0, 0)
        : Unit == RepeatUnit.Months ? (0, 1) : (Unit == RepeatUnit.Weeks ? Interval * 604800L : Interval * 86400L, 0);

    [GeneratedRegex(@"^every\s+(\d{1,3})\s+(days?|weeks?|months?|years?)$")]
    private static partial Regex EveryRx();
}
