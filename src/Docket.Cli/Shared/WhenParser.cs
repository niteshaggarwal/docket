using System.Globalization;
using System.Text.RegularExpressions;
using Docket.Core;

namespace Docket.Shared;

/// <summary>
/// Reads a reminder time: ISO date-time ("2026-11-20T09:00", "2026-11-20 09:00"), "in 2 hours",
/// "in 30 min", "today 17:30", "tomorrow 9:00", "fri 9am", "20/11 14:00", a bare time ("17:30",
/// later today or else tomorrow) or a bare day ("tomorrow", 9:00 that day).
/// Shared by the CLI and the Claude connector (linked into both projects).
/// </summary>
internal static partial class WhenParser
{
    private static readonly TimeSpan DefaultTime = new(9, 0, 0);

    public static DateTimeOffset Parse(string text, DateTimeOffset now)
    {
        var s = text.Trim();
        if (s.Length == 0) throw new DocketException("Empty reminder time.");

        if (IsoRx().IsMatch(s) &&
            DateTimeOffset.TryParse(s.Replace(' ', 'T'), CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var iso))
            return iso.ToOffset(now.Offset);

        var rel = RelativeRx().Match(s);
        if (rel.Success)
        {
            var n = int.Parse(rel.Groups["n"].Value, CultureInfo.InvariantCulture);
            var unit = rel.Groups["u"].Value.ToLowerInvariant();
            return unit[0] switch
            {
                'm' => now.AddMinutes(n),
                'h' => now.AddHours(n),
                'd' => now.AddDays(n),
                _ => now.AddDays(7 * n),
            };
        }

        TimeSpan? time = null;
        var tm = TimeRx().Match(s);
        if (tm.Success)
        {
            var h = int.Parse(tm.Groups["h"].Value, CultureInfo.InvariantCulture);
            var m = tm.Groups["m"].Success ? int.Parse(tm.Groups["m"].Value, CultureInfo.InvariantCulture) : 0;
            var ampm = tm.Groups["ap"].Value.ToLowerInvariant();
            if (ampm == "pm" && h < 12) h += 12;
            if (ampm == "am" && h == 12) h = 0;
            if (h > 23 || m > 59) throw new DocketException($"\"{text}\" isn't a valid time.");
            time = new TimeSpan(h, m, 0);
            s = s[..tm.Index].Trim();
        }

        if (s.Length == 0)
        {
            var today = At(now, time!.Value);
            return today > now ? today : today.AddDays(1);
        }

        DateTimeOffset? day;
        try { day = DocketService.ParseDue(s, now); }
        catch (DocketException) { throw new DocketException($"Couldn't read the reminder time \"{text}\". Try \"tomorrow 9:00\", \"in 2 hours\", \"fri 5pm\" or 2026-11-20T09:00."); }
        if (day is null) throw new DocketException($"Couldn't read the reminder time \"{text}\".");
        return At(day.Value, time ?? DefaultTime);
    }

    private static DateTimeOffset At(DateTimeOffset day, TimeSpan time) =>
        new DateTimeOffset(day.Year, day.Month, day.Day, 0, 0, 0, day.Offset).Add(time);

    [GeneratedRegex(@"^\d{4}-\d{2}-\d{2}[T ]\d{1,2}:\d{2}")]
    private static partial Regex IsoRx();

    [GeneratedRegex(@"^in\s+(?<n>\d{1,4})\s*(?<u>minutes?|mins?|m|hours?|hrs?|h|days?|d|weeks?|w)$", RegexOptions.IgnoreCase)]
    private static partial Regex RelativeRx();

    [GeneratedRegex(@"(?:^|\s)(?:at\s+)?(?<h>\d{1,2})(?:(?::(?<m>\d{2}))(?:\s*(?<ap>am|pm))?|\s*(?<ap>am|pm))$", RegexOptions.IgnoreCase)]
    private static partial Regex TimeRx();
}
