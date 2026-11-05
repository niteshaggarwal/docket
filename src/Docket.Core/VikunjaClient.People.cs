using System.Net;
using System.Text.Json;

namespace Docket.Core;

public sealed partial class VikunjaClient
{
    // ---- relations (subtasks) ----
    public Task AddRelationAsync(long taskId, long otherTaskId, string kind = "subtask", CancellationToken ct = default) =>
        SendAsync<JsonElement>(HttpMethod.Put, $"tasks/{taskId}/relations", new { task_id = taskId, other_task_id = otherTaskId, relation_kind = kind }, ct);

    public Task RemoveRelationAsync(long taskId, long otherTaskId, string kind = "subtask", CancellationToken ct = default) =>
        DeleteAsync($"tasks/{taskId}/relations/{kind}/{otherTaskId}", ct);

    // ---- people ----
    public async Task<List<VUser>> SearchUsersAsync(string query, CancellationToken ct = default) =>
        await GetAsync<List<VUser>>($"users?s={Uri.EscapeDataString(query)}", ct) ?? [];

    public Task<VUser?> GetCurrentUserAsync(CancellationToken ct = default) => GetAsync<VUser>("user", ct);

    /// <summary>Everyone who can see a list (owner, people and team members).</summary>
    public async Task<List<VUser>> GetProjectPeopleAsync(long projectId, CancellationToken ct = default) =>
        await GetAsync<List<VUser>>($"projects/{projectId}/projectusers", ct) ?? [];

    public Task AssignAsync(long taskId, long userId, CancellationToken ct = default) =>
        SendAsync<JsonElement>(HttpMethod.Put, $"tasks/{taskId}/assignees", new { user_id = userId }, ct);

    public Task UnassignAsync(long taskId, long userId, CancellationToken ct = default) =>
        DeleteAsync($"tasks/{taskId}/assignees/{userId}", ct);

    // ---- sharing ----
    public async Task<List<VProjectUser>> GetProjectUsersAsync(long projectId, CancellationToken ct = default) =>
        await GetAsync<List<VProjectUser>>($"projects/{projectId}/users", ct) ?? [];

    public Task ShareWithUserAsync(long projectId, string username, SharePermission permission, CancellationToken ct = default) =>
        SendAsync<JsonElement>(HttpMethod.Put, $"projects/{projectId}/users", new { username, permission = (int)permission }, ct);

    public Task SetUserPermissionAsync(long projectId, long userId, SharePermission permission, CancellationToken ct = default) =>
        SendAsync<JsonElement>(HttpMethod.Post, $"projects/{projectId}/users/{userId}", new { permission = (int)permission }, ct);

    public Task UnshareUserAsync(long projectId, long userId, CancellationToken ct = default) =>
        DeleteAsync($"projects/{projectId}/users/{userId}", ct);

    public async Task<List<VTeam>> GetTeamsAsync(CancellationToken ct = default) =>
        await GetAsync<List<VTeam>>("teams", ct) ?? [];

    public Task<VTeam?> CreateTeamAsync(string name, CancellationToken ct = default) =>
        SendAsync<VTeam>(HttpMethod.Put, "teams", new { name }, ct);

    public Task AddTeamMemberAsync(long teamId, string username, CancellationToken ct = default) =>
        SendAsync<JsonElement>(HttpMethod.Put, $"teams/{teamId}/members", new { username }, ct);

    public async Task<List<VTeam>> GetProjectTeamsAsync(long projectId, CancellationToken ct = default) =>
        await GetAsync<List<VTeam>>($"projects/{projectId}/teams", ct) ?? [];

    public Task ShareWithTeamAsync(long projectId, long teamId, SharePermission permission, CancellationToken ct = default) =>
        SendAsync<JsonElement>(HttpMethod.Put, $"projects/{projectId}/teams", new { team_id = teamId, permission = (int)permission }, ct);

    public Task SetTeamPermissionAsync(long projectId, long teamId, SharePermission permission, CancellationToken ct = default) =>
        SendAsync<JsonElement>(HttpMethod.Post, $"projects/{projectId}/teams/{teamId}", new { permission = (int)permission }, ct);

    public Task UnshareTeamAsync(long projectId, long teamId, CancellationToken ct = default) =>
        DeleteAsync($"projects/{projectId}/teams/{teamId}", ct);

    public async Task<List<VLinkShare>> GetLinkSharesAsync(long projectId, CancellationToken ct = default) =>
        await GetAsync<List<VLinkShare>>($"projects/{projectId}/shares", ct) ?? [];

    public Task<VLinkShare?> CreateLinkShareAsync(long projectId, string name, SharePermission permission, string? password, CancellationToken ct = default) =>
        SendAsync<VLinkShare>(HttpMethod.Put, $"projects/{projectId}/shares", new
        {
            name,
            permission = (int)permission,
            sharing_type = string.IsNullOrEmpty(password) ? 1 : 2,
            password = password ?? "",
        }, ct);

    public Task DeleteLinkShareAsync(long projectId, long shareId, CancellationToken ct = default) =>
        DeleteAsync($"projects/{projectId}/shares/{shareId}", ct);

    // ---- boards ----
    public async Task<List<VBucket>> GetBucketsAsync(long projectId, long viewId, CancellationToken ct = default) =>
        await GetAsync<List<VBucket>>($"projects/{projectId}/views/{viewId}/buckets", ct) ?? [];

    public Task<VBucket?> CreateBucketAsync(long projectId, long viewId, string title, double position, CancellationToken ct = default) =>
        SendAsync<VBucket>(HttpMethod.Put, $"projects/{projectId}/views/{viewId}/buckets", new { title, position, project_view_id = viewId }, ct);

    public Task<VBucket?> UpdateBucketAsync(long projectId, long viewId, VBucket bucket, CancellationToken ct = default) =>
        SendAsync<VBucket>(HttpMethod.Post, $"projects/{projectId}/views/{viewId}/buckets/{bucket.Id}",
            new { id = bucket.Id, title = bucket.Title, position = bucket.Position, limit = bucket.Limit, project_view_id = viewId }, ct);

    public Task DeleteBucketAsync(long projectId, long viewId, long bucketId, CancellationToken ct = default) =>
        DeleteAsync($"projects/{projectId}/views/{viewId}/buckets/{bucketId}", ct);

    // ---- list background ----
    public async Task UploadBackgroundAsync(long projectId, string filePath, CancellationToken ct = default)
    {
        using var form = new MultipartFormDataContent();
        await using var stream = File.OpenRead(filePath);
        form.Add(new StreamContent(stream), "background", Path.GetFileName(filePath));
        using var res = await _http.PutAsync($"projects/{projectId}/backgrounds/upload", form, ct);
        await EnsureOk(res, ct);
    }

    /// <summary>The list's background image, or null when it has none.</summary>
    public async Task<byte[]?> GetBackgroundAsync(long projectId, CancellationToken ct = default)
    {
        using var res = await _http.GetAsync($"projects/{projectId}/background", ct);
        if (res.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Forbidden) return null;
        await EnsureOk(res, ct);
        return await res.Content.ReadAsByteArrayAsync(ct);
    }

    public Task RemoveBackgroundAsync(long projectId, CancellationToken ct = default) => DeleteAsync($"projects/{projectId}/background", ct);
}
