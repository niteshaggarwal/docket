using Docket.Core;
using Xunit;

namespace Docket.Core.Tests;

public class ListStyleTests
{
    [Fact]
    public void Style_rides_in_the_description_without_touching_the_text()
    {
        var style = new ListStyle { Icon = "🏠", Background = "gradient", BackgroundValue = "#112233,#445566,135" }
            .WithColumn(7, new ColumnStyle("#ff8800", "🔥"));
        var desc = style.ApplyTo("<p>Shopping for the flat</p>");
        Assert.StartsWith("<p>Shopping for the flat</p><!--docket:", desc);
        var back = ListStyle.Parse(desc);
        Assert.Equal("🏠", back.Icon);
        Assert.Equal("gradient", back.Background);
        Assert.Equal("#ff8800", back.ColumnFor(7).Color);
        Assert.Equal("🔥", back.ColumnFor(7).Icon);
        Assert.Null(back.ColumnFor(8).Color);
        Assert.Equal("<p>Shopping for the flat</p>", ListStyle.VisibleText(desc));
    }

    [Fact]
    public void Applying_twice_replaces_and_empty_style_removes()
    {
        var once = new ListStyle { Icon = "A" }.ApplyTo("x");
        var twice = new ListStyle { Icon = "B" }.ApplyTo(once);
        Assert.Equal("B", ListStyle.Parse(twice).Icon);
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(twice, "docket:"));
        Assert.Equal("x", new ListStyle().ApplyTo(twice));
        Assert.Equal(new ListStyle().Icon, ListStyle.Parse("<!--docket:{broken-->").Icon);
    }

    [Fact]
    public void Double_dash_cannot_close_the_comment_early()
    {
        var desc = new ListStyle { Icon = "a-->b" }.ApplyTo("");
        Assert.Equal("a-->b", ListStyle.Parse(desc).Icon);
    }
}

public class SlashCommandTests
{
    [Theory]
    [InlineData("/s milk", SlashKind.Search, "milk")]
    [InlineData("/x  pan copies ", SlashKind.Done, "pan copies")]
    [InlineData("/m", SlashKind.MyDay, "")]
    [InlineData("/i call", SlashKind.Important, "call")]
    [InlineData("/ai plan the trip", SlashKind.Ai, "plan the trip")]
    [InlineData("/?", SlashKind.Help, "")]
    [InlineData("Buy milk /s", SlashKind.None, "Buy milk /s")]
    [InlineData("/unknown thing", SlashKind.None, "/unknown thing")]
    public void Parses(string input, SlashKind kind, string arg)
    {
        var c = SlashCommand.Parse(input);
        Assert.Equal(kind, c.Kind);
        Assert.Equal(arg, c.Argument);
    }
}

public class TreeTests
{
    private static TaskInfo T(long id, long[]? subs = null, long[]? parents = null) => new(new VTask
    {
        Id = id,
        Title = $"t{id}",
        RelatedTasks = new()
        {
            ["subtask"] = (subs ?? []).Select(s => new VTask { Id = s, Title = $"t{s}" }).ToList(),
            ["parenttask"] = (parents ?? []).Select(s => new VTask { Id = s, Title = $"t{s}" }).ToList(),
        },
    }, "List");

    [Fact]
    public void Nests_any_depth_and_shows_a_child_under_each_parent()
    {
        // A and B both parent C; C parents D.
        var a = T(1, [3]); var b = T(2, [3]); var c = T(3, [4], [1, 2]); var d = T(4, null, [3]);
        var known = new[] { a, b, c, d }.ToDictionary(x => x.Id);
        var tree = DocketService.BuildTree([a, b, c, d], known);
        Assert.Equal([1L, 2L], tree.Select(n => n.Id));
        Assert.Equal(3, tree[0].Children[0].Id);
        Assert.Equal(4, tree[0].Children[0].Children[0].Id);
        Assert.Equal(3, tree[1].Children[0].Id);
        Assert.Equal(2, tree[0].Children[0].Children[0].Depth);

        var rows = DocketService.Flatten(tree, new HashSet<string> { "1", "1/3" });
        Assert.Equal([1L, 3L, 4L, 2L], rows.Select(r => r.Node.Id));
        Assert.True(rows[0].Expanded);
        Assert.False(rows[3].Expanded);
    }

    [Fact]
    public void A_cycle_does_not_loop_forever()
    {
        var a = T(1, [2], [2]); var b = T(2, [1], [1]);
        var known = new[] { a, b }.ToDictionary(x => x.Id);
        var tree = DocketService.BuildTree([a], known);
        Assert.Single(tree);
        Assert.Equal(2, tree[0].Children[0].Id);
        Assert.Empty(tree[0].Children[0].Children);
    }
}

public class AiPlanTests
{
    [Fact]
    public void Reads_nested_plans_even_with_chatter_around_the_json()
    {
        var plan = AiPlanner.ParsePlan("Sure! {\"tasks\":[{\"title\":\"Trip\",\"my_day\":true,\"subtasks\":[{\"title\":\"Book train\",\"subtasks\":[{\"title\":\"Compare fares\"}]}]},{\"title\":\" \"}]} hope it helps");
        Assert.Single(plan);
        Assert.True(plan[0].MyDay);
        Assert.Equal("Compare fares", plan[0].Subtasks![0].Subtasks![0].Title);
    }

    [Fact]
    public void Garbage_gives_a_clear_error() =>
        Assert.Throws<DocketException>(() => AiPlanner.ParsePlan("no json here"));

    [Fact]
    public void Usernames_and_passwords_for_new_people()
    {
        Assert.Equal("pat.kenny", LocalAccounts.UsernameFrom("Pat Kenny"));
        Assert.Equal("pat.kenny", LocalAccounts.UsernameFrom("pat.kenny@example.com"));
        Assert.Equal(19, LocalAccounts.NewPassword().Length);
        Assert.Equal("Pat Kenny", DocketService.DisplayName(new VUser { Username = "pk", Name = "Pat Kenny" }));
        Assert.Equal("PK", DocketService.Initials(new VUser { Username = "pk", Name = "Pat Kenny" }));
        Assert.Equal("AD", DocketService.Initials(new VUser { Username = "admin" }));
    }
}

public class DocketCloudAiTests
{
    private sealed class Fake(System.Net.HttpStatusCode code, string body) : HttpMessageHandler
    {
        public HttpRequestMessage? Seen;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Seen = request;
            return Task.FromResult(new HttpResponseMessage(code) { Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json") });
        }
    }

    private static readonly AiConfig Cloud = new("docket", "", "tok123", CloudUrl: "https://cloud.example.com/");

    [Fact]
    public async Task Plans_through_docket_cloud_with_the_users_token()
    {
        var fake = new Fake(System.Net.HttpStatusCode.OK, "{\"tasks\":[{\"title\":\"Book train\"}],\"credits\":{\"left\":299}}");
        var plan = await AiPlanner.PlanAsync(Cloud, "trip", DateTimeOffset.Now, ["Home"], new HttpClient(fake));
        Assert.Equal("Book train", plan.Single().Title);
        Assert.Equal("https://cloud.example.com/api/pro/ai/plan", fake.Seen!.RequestUri!.ToString());
        Assert.Equal("tok123", fake.Seen.Headers.Authorization!.Parameter);
        Assert.True(Cloud.IsReady);
        Assert.False((Cloud with { CloudUrl = "" }).IsReady);
    }

    [Fact]
    public async Task Out_of_credits_shows_the_servers_message()
    {
        var fake = new Fake(System.Net.HttpStatusCode.PaymentRequired, "{\"detail\":\"You've used this month's AI credits.\"}");
        var ex = await Assert.ThrowsAsync<DocketException>(() => AiPlanner.PlanAsync(Cloud, "x", DateTimeOffset.Now, [], new HttpClient(fake)));
        Assert.Equal("You've used this month's AI credits.", ex.Message);
    }
}
