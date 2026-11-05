using Docket.Core;
using Xunit;

namespace Docket.Core.Tests;

/// <summary>
/// Runs against a throwaway Vikunja (tools/devserver.ps1). Skipped unless DOCKET_DEV_TOKEN is set.
/// DOCKET_DEV_URL defaults to http://localhost:3499; DOCKET_DEV_VIKUNJA points at that server's vikunja.exe.
/// </summary>
public class LiveTests
{
    private static string? Token => Environment.GetEnvironmentVariable("DOCKET_DEV_TOKEN");
    private static DocketService Svc() => new(new VikunjaClient(new Uri(Environment.GetEnvironmentVariable("DOCKET_DEV_URL") ?? "http://localhost:3499"), Token!));

    [Fact]
    public async Task Subtasks_boards_styles_and_sharing_work_on_a_real_server()
    {
        if (string.IsNullOrEmpty(Token)) return;
        var s = Svc();
        var list = await s.CreateProjectAsync($"Live {Guid.NewGuid():N}"[..13], null);

        // nested subtasks with two parents
        var a = await s.AddAsync("Parent A", list.Id.ToString());
        var b = await s.AddAsync("Parent B", list.Id.ToString());
        var c = await s.AddSubtaskAsync(a.Id, "Child tomorrow");
        Assert.NotNull(c.Task.Due);
        var d = await s.AddSubtaskAsync(c.Id, "Grandchild");
        await s.LinkSubtaskAsync(b.Id, c.Id);
        await Assert.ThrowsAsync<DocketException>(() => s.LinkSubtaskAsync(d.Id, a.Id));
        var tree = await s.ProjectTreeAsync(list.Id, SortMode.Custom);
        Assert.Equal(2, tree.Open.Count);
        Assert.All(tree.Open, n => Assert.Equal(c.Id, n.Children.Single().Id));
        Assert.Equal(d.Id, tree.Open[0].Children[0].Children.Single().Id);
        await s.UnlinkSubtaskAsync(b.Id, c.Id);
        tree = await s.ProjectTreeAsync(list.Id, SortMode.Custom);
        Assert.Empty(tree.Open.Single(n => n.Id == b.Id).Children);

        // old checklist steps become subtasks
        await s.AddStepAsync(b.Id, "Step one");
        await s.AddStepAsync(b.Id, "Step two");
        await s.SetStepDoneAsync(b.Id, 1, true);
        Assert.Equal(2, await s.ConvertStepsToSubtasksAsync(b.Id));
        var bt = await s.GetAsync(b.Id);
        Assert.Equal(2, bt.Subtasks.Count);
        Assert.Empty(TaskNotes.Parse(bt.Description).Steps);

        // list style survives Vikunja's save
        await s.SetListStyleAsync(list.Id, st => st with { Icon = "🏠", Background = "color", BackgroundValue = "#223344" });
        Assert.Equal("🏠", (await s.ListStyleAsync(list.Id)).Icon);

        // board: add, style, rename, delete; permanent columns stay
        var col = await s.AddColumnAsync(list.Id, "Waiting", new ColumnStyle("#ff8800", "⏳"));
        var board = await s.BoardOfAsync(list.Id);
        Assert.True(board.Columns.Last().IsDone);
        Assert.Equal("⏳", board.Columns.Single(x => x.Bucket.Id == col.Id).Style.Icon);
        await s.MoveToColumnAsync(list.Id, a.Id, col.Id);
        await s.RenameColumnAsync(list.Id, board.Columns.First(x => x.IsDefault).Bucket.Id, "Backlog");
        await Assert.ThrowsAsync<DocketException>(() => s.DeleteColumnAsync(list.Id, board.Columns.First(x => x.IsDone).Bucket.Id));
        await s.DeleteColumnAsync(list.Id, col.Id);
        board = await s.BoardOfAsync(list.Id);
        Assert.Equal("Backlog", board.Columns.First(x => x.IsDefault).Bucket.Title);
        Assert.Contains(board.Columns.First(x => x.IsDefault).Tasks, t => t.Id == a.Id);

        // people: a local account, shared and assigned
        var exe = Environment.GetEnvironmentVariable("DOCKET_DEV_VIKUNJA");
        if (!string.IsNullOrEmpty(exe))
        {
            var name = "live" + Guid.NewGuid().ToString("N")[..6];
            var acct = await LocalAccounts.CreateAsync(exe, name, null);
            var person = (await s.Client.SearchUsersAsync(name)).FirstOrDefault() ?? new VUser { Username = name };
            await s.ShareAsync(list.Id, name, SharePermission.ReadWrite);
            var people = await s.Client.GetProjectPeopleAsync(list.Id);
            person = people.Single(u => u.Username == name);
            var assigned = await s.AssignAsync(a.Id, person);
            Assert.Contains(assigned.Task.Assignees!, u => u.Username == name);
            var sharing = await s.SharingAsync(list.Id);
            Assert.Contains(sharing.People, p => p.Username == name);
            Assert.NotEmpty(acct.Password);
        }
        var link = await s.Client.CreateLinkShareAsync(list.Id, "Public", SharePermission.Read, null);
        Assert.NotNull(link);
        Assert.Contains("/share/", DocketService.LinkShareUrl("http://x/", link!));
        await s.DeleteListAsync(list.Id);
    }
}
