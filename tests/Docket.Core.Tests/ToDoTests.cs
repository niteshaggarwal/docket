using Docket.Core;
using Docket.Shared;
using Xunit;

namespace Docket.Core.Tests;

public class TaskNotesTests
{
    private const string VikunjaChecklist =
        "<ul data-type=\"taskList\"><li data-checked=\"true\" data-type=\"taskItem\"><label><input type=\"checkbox\" checked=\"checked\"><span></span></label><div><p>Step</p></div></li></ul>";

    [Fact]
    public void Parses_vikunjas_own_checklist()
    {
        var n = TaskNotes.Parse("<p>Call first</p>" + VikunjaChecklist);
        Assert.Equal("Call first", n.Notes);
        Assert.Single(n.Steps);
        Assert.Equal(new Step("Step", true), n.Steps[0]);
        Assert.Equal(1, n.DoneCount);
    }

    [Fact]
    public void Round_trip_keeps_notes_and_steps()
    {
        var n = new TaskNotes("Line one\nLine & two", [new Step("Draft <brief>", false), new Step("Send", true)]);
        var back = TaskNotes.Parse(n.ToHtml());
        Assert.Equal("Line one\nLine & two", back.Notes);
        Assert.Equal(n.Steps, back.Steps);
    }

    [Fact]
    public void Empty_and_notes_only()
    {
        Assert.Equal(new TaskNotes("", []), TaskNotes.Parse(null));
        var n = TaskNotes.Parse("<p>Just notes</p>");
        Assert.Equal("Just notes", n.Notes);
        Assert.Empty(n.Steps);
        Assert.Equal("", new TaskNotes("", []).ToHtml());
    }

    [Fact]
    public void Only_the_last_checklist_is_steps()
    {
        var html = "<ul data-type=\"taskList\"><li data-checked=\"false\" data-type=\"taskItem\"><div><p>In notes</p></div></li></ul><p>Middle</p>" + VikunjaChecklist;
        var n = TaskNotes.Parse(html);
        Assert.Single(n.Steps);
        Assert.Contains("In notes", n.Notes, StringComparison.Ordinal);
        Assert.Contains("Middle", n.Notes, StringComparison.Ordinal);
    }
}

public class RepeatRuleTests
{
    private static readonly TimeSpan Ist = TimeSpan.FromHours(5.5);

    [Theory]
    [InlineData("daily", 1, RepeatUnit.Days, false)]
    [InlineData("Weekdays", 1, RepeatUnit.Days, true)]
    [InlineData("weekly", 1, RepeatUnit.Weeks, false)]
    [InlineData("monthly", 1, RepeatUnit.Months, false)]
    [InlineData("yearly", 1, RepeatUnit.Years, false)]
    [InlineData("every 3 weeks", 3, RepeatUnit.Weeks, false)]
    [InlineData("every 2 months", 2, RepeatUnit.Months, false)]
    [InlineData("every 1 day", 1, RepeatUnit.Days, false)]
    public void Parses(string text, int n, RepeatUnit unit, bool weekdays)
    {
        Assert.True(RepeatRule.TryParse(text, out var r));
        Assert.Equal(new RepeatRule(n, unit, weekdays), r);
    }

    [Theory]
    [InlineData("sometimes")]
    [InlineData("every 0 days")]
    [InlineData("every week day")]
    public void Rejects(string text) => Assert.False(RepeatRule.TryParse(text, out _));

    [Fact]
    public void Weekdays_skip_the_weekend()
    {
        var fri = new DateTimeOffset(2026, 11, 6, 18, 0, 0, Ist);
        Assert.Equal(new DateTimeOffset(2026, 11, 9, 18, 0, 0, Ist), RepeatRule.Weekdays.Next(fri));
        var sat = new DateTimeOffset(2026, 11, 7, 18, 0, 0, Ist);
        Assert.Equal(DayOfWeek.Monday, RepeatRule.Weekdays.Next(sat).DayOfWeek);
    }

    [Fact]
    public void Monthly_yearly_and_every_three_weeks()
    {
        var jan31 = new DateTimeOffset(2026, 1, 31, 9, 0, 0, Ist);
        Assert.Equal(new DateTimeOffset(2026, 2, 28, 9, 0, 0, Ist), RepeatRule.Monthly.Next(jan31));
        Assert.Equal(new DateTimeOffset(2027, 1, 31, 9, 0, 0, Ist), RepeatRule.Yearly.Next(jan31));
        Assert.Equal(new DateTimeOffset(2026, 2, 21, 9, 0, 0, Ist), new RepeatRule(3, RepeatUnit.Weeks).Next(jan31));
    }

    [Fact]
    public void Native_fields_and_labels()
    {
        Assert.Equal((86400L, 0), RepeatRule.Daily.NativeFields);
        Assert.Equal((3 * 604800L, 0), new RepeatRule(3, RepeatUnit.Weeks).NativeFields);
        Assert.Equal((0L, 1), RepeatRule.Monthly.NativeFields);
        Assert.Equal((0L, 0), RepeatRule.Weekdays.NativeFields);
        Assert.Equal((0L, 0), RepeatRule.Yearly.NativeFields);
        Assert.False(RepeatRule.Weekdays.IsNative);
        Assert.Equal("↻ weekdays", RepeatRule.Weekdays.Label);
        Assert.Equal("↻ every 2 months", new RepeatRule(2, RepeatUnit.Months).Label);
    }

    [Fact]
    public void Reads_rule_from_task()
    {
        Assert.Null(RepeatRule.From(new VTask()));
        Assert.Equal(RepeatRule.Daily, RepeatRule.From(new VTask { RepeatAfter = 86400 }));
        Assert.Equal(new RepeatRule(2, RepeatUnit.Weeks), RepeatRule.From(new VTask { RepeatAfter = 2 * 604800 }));
        Assert.Equal(RepeatRule.Monthly, RepeatRule.From(new VTask { RepeatMode = 1 }));
        Assert.Equal(RepeatRule.Weekdays, RepeatRule.From(new VTask { Labels = [new VLabel { Title = "↻ weekdays" }] }));
        Assert.Equal(RepeatRule.Yearly, RepeatRule.From(new VTask { Labels = [new VLabel { Title = "Client" }, new VLabel { Title = "↻ yearly" }] }));
    }
}

public class PlannedGroupTests
{
    // Wednesday 4 Nov 2026, 10:00 IST
    private static readonly DateTimeOffset Now = new(2026, 11, 4, 10, 0, 0, TimeSpan.FromHours(5.5));
    private static VTask Due(int y, int m, int d) => new() { DueDate = new DateTimeOffset(y, m, d, 18, 0, 0, Now.Offset) };

    [Fact]
    public void Groups_like_planned_view()
    {
        Assert.Equal("Earlier", DocketService.PlannedGroup(Due(2026, 11, 3), Now));
        Assert.Equal("Today", DocketService.PlannedGroup(Due(2026, 11, 4), Now));
        Assert.Equal("Tomorrow", DocketService.PlannedGroup(Due(2026, 11, 5), Now));
        Assert.Equal("This week", DocketService.PlannedGroup(Due(2026, 11, 8), Now)); // Sunday
        Assert.Equal("Later", DocketService.PlannedGroup(Due(2026, 11, 9), Now));     // next Monday
        Assert.Equal("No date", DocketService.PlannedGroup(new VTask(), Now));
    }

    [Fact]
    public void Categories_exclude_my_day_and_repeat_markers()
    {
        Assert.True(DocketService.IsCategory(new VLabel { Title = "Client" }));
        Assert.False(DocketService.IsCategory(new VLabel { Title = "My Day" }));
        Assert.False(DocketService.IsCategory(new VLabel { Title = "↻ weekdays" }));
    }
}

public class WhenParserTests
{
    // Wednesday 4 Nov 2026, 10:00 IST
    private static readonly DateTimeOffset Now = new(2026, 11, 4, 10, 0, 0, TimeSpan.FromHours(5.5));
    private static DateTimeOffset At(int d, int h, int m = 0) => new(2026, 11, d, h, m, 0, Now.Offset);

    [Theory]
    [InlineData("tomorrow 9:00", 5, 9, 0)]
    [InlineData("today 17:30", 4, 17, 30)]
    [InlineData("fri 5pm", 6, 17, 0)]
    [InlineData("friday at 9am", 6, 9, 0)]
    [InlineData("20/11 14:00", 20, 14, 0)]
    [InlineData("tomorrow", 5, 9, 0)]
    [InlineData("17:30", 4, 17, 30)]
    [InlineData("9:30", 5, 9, 30)]   // already past today, so tomorrow
    [InlineData("2026-11-20T09:15", 20, 9, 15)]
    [InlineData("2026-11-20 09:15", 20, 9, 15)]
    public void Reads_times(string text, int day, int h, int m) => Assert.Equal(At(day, h, m), WhenParser.Parse(text, Now));

    [Fact]
    public void Relative_times()
    {
        Assert.Equal(Now.AddHours(2), WhenParser.Parse("in 2 hours", Now));
        Assert.Equal(Now.AddMinutes(30), WhenParser.Parse("in 30 min", Now));
        Assert.Equal(Now.AddDays(3), WhenParser.Parse("in 3 days", Now));
    }

    [Theory]
    [InlineData("whenever")]
    [InlineData("25:00")]
    [InlineData("")]
    public void Rejects_nonsense(string text) => Assert.Throws<DocketException>(() => WhenParser.Parse(text, Now));
}
