namespace Docket.Core;

/// <summary>Who a list is shared with: people, teams and public links.</summary>
public sealed record Sharing(IReadOnlyList<VProjectUser> People, IReadOnlyList<VTeam> Teams, IReadOnlyList<VLinkShare> Links);

/// <summary>A board column with its look and whether it is one of the two permanent ones.</summary>
public sealed record BoardColumn(VBucket Bucket, ColumnStyle Style, bool IsDefault, bool IsDone, IReadOnlyList<TaskInfo> Tasks)
{
    /// <summary>To-Do (where new tasks land) and Done can be renamed and restyled, but not deleted.</summary>
    public bool IsPermanent => IsDefault || IsDone;
}

public sealed record Board(VProject Project, VView View, ListStyle Style, IReadOnlyList<BoardColumn> Columns);

public sealed partial class DocketService
{
    // ---------- people ----------
    private VUser? _me;

    public async Task<VUser> MeAsync(CancellationToken ct = default) =>
        _me ??= await client.GetCurrentUserAsync(ct) ?? throw new DocketException("Couldn't read your account.");

    /// <summary>Everyone you work with: people on any of your shared lists, plus you.</summary>
    public async Task<List<VUser>> PeopleAsync(CancellationToken ct = default)
    {
        var me = await MeAsync(ct);
        var all = new Dictionary<long, VUser> { [me.Id] = me };
        foreach (var p in await ProjectsAsync(false, ct))
        {
            try { foreach (var u in await client.GetProjectPeopleAsync(p.Id, ct)) all.TryAdd(u.Id, u); }
            catch (VikunjaException) { }
        }
        return all.Values.OrderBy(u => u.Id != me.Id).ThenBy(u => DisplayName(u), StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    public static string DisplayName(VUser u) => string.IsNullOrWhiteSpace(u.Name) ? u.Username : u.Name;

    /// <summary>Initials for an avatar: "Pat Kenny" -> "PK", "admin" -> "AD".</summary>
    public static string Initials(VUser u)
    {
        var parts = DisplayName(u).Split([' ', '.', '_', '-'], StringSplitOptions.RemoveEmptyEntries);
        return parts.Length switch
        {
            0 => "?",
            1 => parts[0][..Math.Min(2, parts[0].Length)].ToUpperInvariant(),
            _ => $"{char.ToUpperInvariant(parts[0][0])}{char.ToUpperInvariant(parts[^1][0])}",
        };
    }

    /// <summary>Assigns a task. If the person can't see the task's list yet, the list is shared with them first (can edit).</summary>
    public async Task<TaskInfo> AssignAsync(long taskId, VUser person, CancellationToken ct = default)
    {
        var t = await GetAsync(taskId, ct);
        var me = await MeAsync(ct);
        if (person.Id != me.Id)
        {
            var people = await client.GetProjectPeopleAsync(t.ProjectId, ct);
            if (people.All(u => u.Id != person.Id))
                await client.ShareWithUserAsync(t.ProjectId, person.Username, SharePermission.ReadWrite, ct);
        }
        if (t.Assignees?.Any(a => a.Id == person.Id) != true) await client.AssignAsync(taskId, person.Id, ct);
        return new TaskInfo(await GetAsync(taskId, ct), await ProjectNameAsync(t.ProjectId, ct));
    }

    public async Task<TaskInfo> UnassignAsync(long taskId, long userId, CancellationToken ct = default)
    {
        await client.UnassignAsync(taskId, userId, ct);
        var t = await GetAsync(taskId, ct);
        return new TaskInfo(t, await ProjectNameAsync(t.ProjectId, ct));
    }

    // ---------- sharing ----------
    public async Task<Sharing> SharingAsync(long projectId, CancellationToken ct = default) => new(
        await client.GetProjectUsersAsync(projectId, ct),
        await client.GetProjectTeamsAsync(projectId, ct),
        await client.GetLinkSharesAsync(projectId, ct));

    /// <summary>Shares a list with an account by username (or the email it was created with, when the server allows lookup).</summary>
    public async Task ShareAsync(long projectId, string usernameOrEmail, SharePermission permission, CancellationToken ct = default)
    {
        var who = usernameOrEmail.Trim();
        if (who.Length == 0) throw new DocketException("Type a username or an email.");
        if (who.Contains('@'))
        {
            var found = await client.SearchUsersAsync(who, ct);
            if (found.Count == 1) who = found[0].Username;
        }
        await client.ShareWithUserAsync(projectId, who, permission, ct);
    }

    /// <summary>The address anyone can open a public link at.</summary>
    public static string LinkShareUrl(string serverUrl, VLinkShare share) => $"{serverUrl.TrimEnd('/')}/share/{share.Hash}/auth";

    // ---------- list look ----------
    public async Task<ListStyle> ListStyleAsync(long projectId, CancellationToken ct = default) =>
        ListStyle.Parse((await ProjectsAsync(false, ct)).FirstOrDefault(p => p.Id == projectId)?.Description);

    public async Task<ListStyle> SetListStyleAsync(long projectId, Func<ListStyle, ListStyle> change, CancellationToken ct = default)
    {
        var p = (await ProjectsAsync(true, ct)).FirstOrDefault(x => x.Id == projectId) ?? throw new DocketException($"List {projectId} not found.");
        var style = change(ListStyle.Parse(p.Description));
        p.Description = style.ApplyTo(p.Description);
        await client.UpdateProjectAsync(p, ct);
        await ProjectsAsync(true, ct);
        return style;
    }

    // ---------- boards ----------
    public async Task<Board> BoardOfAsync(long projectId, CancellationToken ct = default)
    {
        var p = (await ProjectsAsync(true, ct)).FirstOrDefault(x => x.Id == projectId) ?? throw new DocketException($"List {projectId} not found.");
        var view = p.KanbanView ?? throw new DocketException($"\"{p.Title}\" has no board.");
        var style = ListStyle.Parse(p.Description);
        var buckets = await client.GetBoardAsync(p.Id, view.Id, ct);
        var cols = buckets.OrderBy(b => b.Position).Select(b => new BoardColumn(b, style.ColumnFor(b.Id),
            b.Id == view.DefaultBucketId || (view.DefaultBucketId == 0 && b == buckets.OrderBy(x => x.Position).First()),
            b.Id == view.DoneBucketId,
            (b.Tasks ?? []).OrderBy(t => t.Position).Select(t => new TaskInfo(t, p.Title, b.Title)).ToList())).ToList();
        return new Board(p, view, style, cols);
    }

    public async Task<VBucket> AddColumnAsync(long projectId, string title, ColumnStyle? style = null, CancellationToken ct = default)
    {
        var board = await BoardOfAsync(projectId, ct);
        // New columns go just before Done, so Done stays last.
        var done = board.Columns.FirstOrDefault(c => c.IsDone);
        var before = board.Columns.Where(c => !c.IsDone).Select(c => c.Bucket.Position).DefaultIfEmpty(0).Max();
        var pos = done is null ? before + 100 : (before + done.Bucket.Position) / 2;
        var b = await client.CreateBucketAsync(projectId, board.View.Id, title.Trim(), pos, ct) ?? throw new DocketException("Vikunja didn't return the new column.");
        if (style is not null) await SetListStyleAsync(projectId, s => s.WithColumn(b.Id, style), ct);
        return b;
    }

    public async Task RenameColumnAsync(long projectId, long bucketId, string title, CancellationToken ct = default)
    {
        var board = await BoardOfAsync(projectId, ct);
        var col = board.Columns.FirstOrDefault(c => c.Bucket.Id == bucketId) ?? throw new DocketException("Column not found.");
        col.Bucket.Title = title.Trim();
        await client.UpdateBucketAsync(projectId, board.View.Id, col.Bucket, ct);
    }

    public Task SetColumnStyleAsync(long projectId, long bucketId, ColumnStyle style, CancellationToken ct = default) =>
        SetListStyleAsync(projectId, s => s.WithColumn(bucketId, style), ct);

    /// <summary>Deletes a column; its tasks move to To-Do. To-Do and Done can't be deleted.</summary>
    public async Task DeleteColumnAsync(long projectId, long bucketId, CancellationToken ct = default)
    {
        var board = await BoardOfAsync(projectId, ct);
        var col = board.Columns.FirstOrDefault(c => c.Bucket.Id == bucketId) ?? throw new DocketException("Column not found.");
        if (col.IsPermanent) throw new DocketException($"\"{col.Bucket.Title}\" is permanent. You can rename it, but not delete it.");
        var todo = board.Columns.First(c => c.IsDefault);
        foreach (var t in col.Tasks) await client.MoveToBucketAsync(projectId, board.View.Id, todo.Bucket.Id, t.Id, ct);
        await client.DeleteBucketAsync(projectId, board.View.Id, bucketId, ct);
        await SetListStyleAsync(projectId, s => s with { Columns = s.Columns.Where(kv => kv.Key != bucketId.ToString()).ToDictionary() }, ct);
    }

    public async Task MoveToColumnAsync(long projectId, long taskId, long bucketId, CancellationToken ct = default)
    {
        var board = await BoardOfAsync(projectId, ct);
        await client.MoveToBucketAsync(projectId, board.View.Id, bucketId, taskId, ct);
    }
}
