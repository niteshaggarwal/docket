using System.Net;
using System.Text;
using Docket.Core;
using Xunit;

namespace Docket.Core.Tests;

public class ParseDueTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 4, 10, 0, 0, TimeSpan.FromHours(5.5));

    [Theory]
    [InlineData("none")]
    [InlineData("clear")]
    [InlineData("")]
    public void Clearing_words_return_null(string s) => Assert.Null(DocketService.ParseDue(s, Now));

    [Fact]
    public void Iso_date_means_six_pm_local() =>
        Assert.Equal(new DateTimeOffset(2026, 11, 20, 18, 0, 0, Now.Offset), DocketService.ParseDue("2026-11-20", Now));

    [Fact]
    public void Iso_date_time_is_kept() =>
        Assert.Equal(new DateTimeOffset(2026, 11, 20, 15, 30, 0, Now.Offset), DocketService.ParseDue("2026-11-20T15:30:00+05:30", Now));

    [Theory]
    [InlineData("tomorrow", 5)]
    [InlineData("fri", 6)]
    [InlineData("in 3 days", 7)]
    [InlineData("20/11", 20)]
    public void Quick_add_words_work(string s, int day) => Assert.Equal(day, DocketService.ParseDue(s, Now)!.Value.Day);

    [Fact]
    public void Nonsense_is_rejected() => Assert.Throws<DocketException>(() => DocketService.ParseDue("next blue moon", Now));

    [Theory]
    [InlineData("today", TaskView.Today)]
    [InlineData("WEEK", TaskView.Upcoming)]
    [InlineData("all", TaskView.Open)]
    [InlineData("completed", TaskView.Done)]
    public void Views_parse(string s, TaskView v) => Assert.Equal(v, DocketService.ParseView(s));
}

public class FormatTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 4, 10, 0, 0, TimeSpan.FromHours(5.5));

    [Fact]
    public void Line_shows_id_status_project_due_priority_column()
    {
        var t = new VTask { Id = 12, Title = "Send proposal", DueDate = new DateTimeOffset(2026, 11, 4, 18, 0, 0, Now.Offset), Priority = 3 };
        Assert.Equal("#12 [ ] Send proposal · Work · due today · high · Doing", TaskFormat.Line(new TaskInfo(t, "Work", "Doing"), Now));
    }

    [Fact]
    public void Overdue_and_times_are_shown()
    {
        var t = new VTask { Id = 3, Title = "Call", DueDate = new DateTimeOffset(2026, 11, 3, 9, 30, 0, Now.Offset) };
        Assert.Equal("overdue, yesterday 09:30", TaskFormat.Due(t, Now));
    }

    [Fact]
    public void Html_is_turned_into_text() =>
        Assert.Equal("Hello\n- one\n- two & more", TaskFormat.StripHtml("<p>Hello</p><ul><li>one</li><li>two &amp; more</li></ul>"));
}

public class ResolveProjectTests
{
    private sealed class FakeVikunja(string projectsJson) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(projectsJson, Encoding.UTF8, "application/json") });
    }

    private static DocketService Svc() => new(new VikunjaClient(new Uri("http://x"), "t", new FakeVikunja(
        """[{"id":1,"title":"Inbox"},{"id":2,"title":"Quartifex work"},{"id":3,"title":"Home"},{"id":4,"title":"Home office"}]""")));

    [Theory]
    [InlineData("inbox", 1)]
    [InlineData("quartifex", 2)]
    [InlineData("#3", 3)]
    [InlineData("home", 3)]
    public async Task Finds_by_id_exact_or_unique_partial(string key, long id) => Assert.Equal(id, (await Svc().ResolveProjectAsync(key)).Id);

    [Fact]
    public async Task Ambiguous_partial_lists_the_options()
    {
        var ex = await Assert.ThrowsAsync<DocketException>(() => Svc().ResolveProjectAsync("hom"));
        Assert.Contains("Home office", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Unknown_lists_all_projects()
    {
        var ex = await Assert.ThrowsAsync<DocketException>(() => Svc().ResolveProjectAsync("garden"));
        Assert.Contains("Inbox", ex.Message, StringComparison.Ordinal);
    }
}
