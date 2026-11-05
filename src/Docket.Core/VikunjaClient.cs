using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace Docket.Core;

public sealed class VikunjaException(HttpStatusCode status, string message) : Exception(message)
{
    public HttpStatusCode Status { get; } = status;
}

/// <summary>
/// Thin client for the Vikunja v1 REST API, authenticated with a personal API token.
/// </summary>
public sealed partial class VikunjaClient : IDisposable
{
    private readonly HttpClient _http;

    public VikunjaClient(Uri serverUrl, string apiToken, HttpMessageHandler? handler = null)
    {
        var baseUri = new Uri(serverUrl.ToString().TrimEnd('/') + "/api/v1/");
        _http = handler is null ? new HttpClient() : new HttpClient(handler);
        _http.BaseAddress = baseUri;
        _http.Timeout = TimeSpan.FromSeconds(20);
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiToken);
    }

    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    // ---- projects ----
    public async Task<List<VProject>> GetProjectsAsync(CancellationToken ct = default)
    {
        var all = new List<VProject>();
        for (var page = 1; ; page++)
        {
            var batch = await GetAsync<List<VProject>>($"projects?page={page}&per_page=50", ct) ?? [];
            all.AddRange(batch);
            if (batch.Count < 50) break;
        }
        return all;
    }

    public Task<VProject?> CreateProjectAsync(string title, CancellationToken ct = default) =>
        SendAsync<VProject>(HttpMethod.Put, "projects", new { title }, ct);

    // ---- tasks ----
    /// <summary>Tasks across all projects, using Vikunja's filter syntax, e.g. "done = false &amp;&amp; due_date &lt; now/d+1d".</summary>
    public async Task<List<VTask>> GetTasksAsync(string? filter = null, string sortBy = "due_date", string orderBy = "asc", CancellationToken ct = default)
    {
        var all = new List<VTask>();
        for (var page = 1; page <= 20; page++)
        {
            var q = $"tasks?page={page}&per_page=50&sort_by={sortBy}&order_by={orderBy}";
            if (!string.IsNullOrWhiteSpace(filter)) q += "&filter=" + Uri.EscapeDataString(filter);
            var batch = await GetAsync<List<VTask>>(q, ct) ?? [];
            all.AddRange(batch);
            if (batch.Count < 50) break;
        }
        return all;
    }

    public Task<VTask?> GetTaskAsync(long id, CancellationToken ct = default) => GetAsync<VTask>($"tasks/{id}", ct);

    public Task<VTask?> CreateTaskAsync(long projectId, string title, DateTimeOffset? due = null, int priority = 0, CancellationToken ct = default) =>
        SendAsync<VTask>(HttpMethod.Put, $"projects/{projectId}/tasks",
            new { title, due_date = due ?? VDates.Empty, priority }, ct);

    /// <summary>Saves a task. Vikunja replaces all fields, so pass a task fetched from the server.</summary>
    public Task<VTask?> UpdateTaskAsync(VTask task, CancellationToken ct = default) =>
        SendAsync<VTask>(HttpMethod.Post, $"tasks/{task.Id}", task, ct);

    /// <summary>Fetches the latest copy, applies the change and saves it.</summary>
    public async Task<VTask?> ModifyTaskAsync(long id, Action<VTask> change, CancellationToken ct = default)
    {
        var fresh = await GetTaskAsync(id, ct) ?? throw new VikunjaException(HttpStatusCode.NotFound, $"Task {id} not found.");
        change(fresh);
        return await UpdateTaskAsync(fresh, ct);
    }

    public Task<VTask?> SetDoneAsync(long id, bool done, CancellationToken ct = default) =>
        ModifyTaskAsync(id, t => t.Done = done, ct);

    // ---- kanban ----
    public async Task<List<VBucket>> GetBoardAsync(long projectId, long viewId, CancellationToken ct = default) =>
        await GetAsync<List<VBucket>>($"projects/{projectId}/views/{viewId}/tasks", ct) ?? [];

    public Task MoveToBucketAsync(long projectId, long viewId, long bucketId, long taskId, CancellationToken ct = default) =>
        SendAsync<JsonElement>(HttpMethod.Post, $"projects/{projectId}/views/{viewId}/buckets/{bucketId}/tasks", new { task_id = taskId }, ct);

    // ---- comments and labels ----
    public async Task<List<VComment>> GetCommentsAsync(long taskId, CancellationToken ct = default) =>
        await GetAsync<List<VComment>>($"tasks/{taskId}/comments", ct) ?? [];

    public Task<VComment?> AddCommentAsync(long taskId, string text, CancellationToken ct = default) =>
        SendAsync<VComment>(HttpMethod.Put, $"tasks/{taskId}/comments", new { comment = text }, ct);

    public async Task<List<VLabel>> GetLabelsAsync(CancellationToken ct = default) =>
        await GetAsync<List<VLabel>>("labels?per_page=200", ct) ?? [];

    public Task<VLabel?> CreateLabelAsync(string title, CancellationToken ct = default) =>
        SendAsync<VLabel>(HttpMethod.Put, "labels", new { title }, ct);

    public Task AddLabelToTaskAsync(long taskId, long labelId, CancellationToken ct = default) =>
        SendAsync<JsonElement>(HttpMethod.Put, $"tasks/{taskId}/labels", new { label_id = labelId }, ct);

    public Task<VProject?> CreateProjectAsync(string title, long parentId, CancellationToken ct = default) =>
        SendAsync<VProject>(HttpMethod.Put, "projects", new { title, parent_project_id = parentId }, ct);

    /// <summary>Full-text search over task titles and descriptions.</summary>
    public async Task<List<VTask>> SearchTasksAsync(string query, bool includeDone = false, CancellationToken ct = default)
    {
        var q = $"tasks?per_page=50&s={Uri.EscapeDataString(query)}&sort_by=due_date&order_by=asc";
        if (!includeDone) q += "&filter=" + Uri.EscapeDataString("done = false");
        return await GetAsync<List<VTask>>(q, ct) ?? [];
    }

    // ---- To Do features ----
    /// <summary>Replaces a task's labels (no delete permission needed).</summary>
    public Task SetTaskLabelsAsync(long taskId, IEnumerable<long> labelIds, CancellationToken ct = default) =>
        SendAsync<JsonElement>(HttpMethod.Post, $"tasks/{taskId}/labels/bulk", new { labels = labelIds.Distinct().Select(id => new { id }).ToArray() }, ct);

    public Task<VLabel?> UpdateLabelAsync(VLabel label, CancellationToken ct = default) =>
        SendAsync<VLabel>(HttpMethod.Post, $"labels/{label.Id}", label, ct);

    public Task<VLabel?> CreateLabelAsync(string title, string hexColor, CancellationToken ct = default) =>
        SendAsync<VLabel>(HttpMethod.Put, "labels", new { title, hex_color = hexColor }, ct);

    public Task<VProject?> UpdateProjectAsync(VProject project, CancellationToken ct = default) =>
        SendAsync<VProject>(HttpMethod.Post, $"projects/{project.Id}", project, ct);

    public Task DeleteTaskAsync(long id, CancellationToken ct = default) => DeleteAsync($"tasks/{id}", ct);
    public Task DeleteProjectAsync(long id, CancellationToken ct = default) => DeleteAsync($"projects/{id}", ct);

    public Task SetPositionAsync(long taskId, long viewId, double position, CancellationToken ct = default) =>
        SendAsync<JsonElement>(HttpMethod.Post, $"tasks/{taskId}/position", new { task_id = taskId, project_view_id = viewId, position }, ct);

    public async Task<List<VAttachment>> GetAttachmentsAsync(long taskId, CancellationToken ct = default) =>
        await GetAsync<List<VAttachment>>($"tasks/{taskId}/attachments", ct) ?? [];

    public async Task<VAttachment> UploadAttachmentAsync(long taskId, string filePath, CancellationToken ct = default)
    {
        using var form = new MultipartFormDataContent();
        await using var stream = File.OpenRead(filePath);
        form.Add(new StreamContent(stream), "files", Path.GetFileName(filePath));
        using var res = await _http.PutAsync($"tasks/{taskId}/attachments", form, ct);
        await EnsureOk(res, ct);
        using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync(ct));
        if (doc.RootElement.TryGetProperty("errors", out var errs) && errs.ValueKind == JsonValueKind.Array && errs.GetArrayLength() > 0)
            throw new VikunjaException(HttpStatusCode.BadRequest, $"Vikunja: upload failed ({errs[0]})");
        return doc.RootElement.GetProperty("success")[0].Deserialize<VAttachment>(Json)!;
    }

    public async Task<byte[]> DownloadAttachmentAsync(long taskId, long attachmentId, CancellationToken ct = default)
    {
        using var res = await _http.GetAsync($"tasks/{taskId}/attachments/{attachmentId}", ct);
        await EnsureOk(res, ct);
        return await res.Content.ReadAsByteArrayAsync(ct);
    }

    internal async Task DeleteAsync(string path, CancellationToken ct)
    {
        using var res = await _http.DeleteAsync(path, ct);
        if (res.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            throw new VikunjaException(res.StatusCode, "This token can't delete. Re-run C:\\dev\\vikunja\\setup-tokens.ps1 to give the Docket app delete rights.");
        await EnsureOk(res, ct);
    }

    // ---- plumbing ----
    public async Task<string> GetServerVersionAsync(CancellationToken ct = default)
    {
        var info = await GetAsync<JsonElement>("info", ct);
        return info.TryGetProperty("version", out var v) ? v.GetString() ?? "" : "";
    }

    internal async Task<T?> GetAsync<T>(string path, CancellationToken ct)
    {
        using var res = await _http.GetAsync(path, ct);
        await EnsureOk(res, ct);
        return await res.Content.ReadFromJsonAsync<T>(Json, ct);
    }

    internal async Task<T?> SendAsync<T>(HttpMethod method, string path, object body, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(method, path) { Content = JsonContent.Create(body, body.GetType(), options: Json) };
        using var res = await _http.SendAsync(req, ct);
        await EnsureOk(res, ct);
        return await res.Content.ReadFromJsonAsync<T>(Json, ct);
    }

    internal static async Task EnsureOk(HttpResponseMessage res, CancellationToken ct)
    {
        if (res.IsSuccessStatusCode) return;
        var text = await res.Content.ReadAsStringAsync(ct);
        var msg = text;
        try { using var d = JsonDocument.Parse(text); if (d.RootElement.TryGetProperty("message", out var m)) msg = m.GetString() ?? text; } catch (JsonException) { }
        throw new VikunjaException(res.StatusCode, $"Vikunja: {(int)res.StatusCode} {msg}");
    }

    public void Dispose() => _http.Dispose();
}
