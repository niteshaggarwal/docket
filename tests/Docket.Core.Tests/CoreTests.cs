using System.Text.Json;
using Docket.Core;
using Xunit;

namespace Docket.Core.Tests;

public class QuickAddTests
{
    // Wednesday 4 Nov 2026, 10:00 IST
    private static readonly DateTimeOffset Now = new(2026, 11, 4, 10, 0, 0, TimeSpan.FromHours(5.5));

    [Fact]
    public void Plain_title_has_no_date_or_priority()
    {
        var r = QuickAdd.Parse("Call Pat about the portfolio", Now);
        Assert.Equal("Call Pat about the portfolio", r.Title);
        Assert.Null(r.Due);
        Assert.Equal(0, r.Priority);
    }

    [Fact]
    public void Tomorrow_and_priority_are_extracted()
    {
        var r = QuickAdd.Parse("Send invoice tomorrow !3", Now);
        Assert.Equal("Send invoice", r.Title);
        Assert.Equal(new DateTimeOffset(2026, 11, 5, 18, 0, 0, Now.Offset), r.Due);
        Assert.Equal(3, r.Priority);
    }

    [Theory]
    [InlineData("Review deck friday", 6)]
    [InlineData("Review deck on fri", 6)]
    [InlineData("Review deck wednesday", 11)] // same weekday means next week
    public void Weekday_means_the_next_one(string input, int day)
    {
        var r = QuickAdd.Parse(input, Now);
        Assert.Equal("Review deck", r.Title);
        Assert.Equal(day, r.Due!.Value.Day);
    }

    [Fact]
    public void Day_month_date_in_the_past_rolls_to_next_year()
    {
        var r = QuickAdd.Parse("Renew domain 1/2", Now);
        Assert.Equal(new DateTimeOffset(2027, 2, 1, 18, 0, 0, Now.Offset), r.Due);
    }

    [Fact]
    public void Words_inside_other_words_are_left_alone()
    {
        var r = QuickAdd.Parse("Fix todays layout", Now);
        Assert.Equal("Fix todays layout", r.Title);
        Assert.Null(r.Due);
    }

    [Fact]
    public void In_n_days()
    {
        var r = QuickAdd.Parse("Follow up in 3 days", Now);
        Assert.Equal("Follow up", r.Title);
        Assert.Equal(7, r.Due!.Value.Day);
    }
}

public class ModelTests
{
    private const string TaskJson = """
        {"id":1,"title":"Probe task","description":"","project_id":2,"done":false,"done_at":"0001-01-01T00:00:00Z",
         "due_date":"2026-11-04T05:32:37+05:30","reminders":null,"repeat_after":0,"repeat_mode":0,"priority":3,
         "start_date":"0001-01-01T00:00:00Z","end_date":"0001-01-01T00:00:00Z","labels":null,"bucket_id":7,"position":65536}
        """;

    [Fact]
    public void Empty_dates_read_as_null_and_unknown_fields_round_trip()
    {
        var t = JsonSerializer.Deserialize<VTask>(TaskJson, VikunjaClient.Json)!;
        Assert.Equal(3, t.Priority);
        Assert.NotNull(t.Due);
        Assert.Null(VDates.OrNull(t.DoneAt));

        var back = JsonSerializer.Serialize(t, VikunjaClient.Json);
        Assert.Contains("\"repeat_after\":0", back, StringComparison.Ordinal);
        Assert.Contains("\"reminders\":null", back, StringComparison.Ordinal);
    }

    [Fact]
    public void Kanban_view_is_found_on_project()
    {
        var p = JsonSerializer.Deserialize<VProject>("""{"id":2,"title":"P","views":[{"id":9,"view_kind":"list"},{"id":12,"view_kind":"kanban","done_bucket_id":9}]}""", VikunjaClient.Json)!;
        Assert.Equal(12, p.KanbanView!.Id);
    }
}
