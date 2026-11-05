using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Docket.Core;

/// <summary>Which AI plans tasks from a sentence. Provider: "ollama" (local), "anthropic", "openai", "gemini" or "off".</summary>
public sealed record AiConfig(string Provider, string Model, string? ApiKey = null, string OllamaUrl = "http://localhost:11434")
{
    public static string DefaultModel(string provider) => provider switch
    {
        "ollama" => "qwen2.5:7b",
        "anthropic" => "claude-haiku-5-5",
        "openai" => "gpt-4.1-mini",
        "gemini" => "gemini-2.5-flash",
        _ => "",
    };

    public bool IsReady => Provider switch
    {
        "ollama" => !string.IsNullOrWhiteSpace(Model),
        "anthropic" or "openai" or "gemini" => !string.IsNullOrWhiteSpace(ApiKey) && !string.IsNullOrWhiteSpace(Model),
        _ => false,
    };

    public string Label => Provider switch
    {
        "ollama" => $"Local AI ({Model})",
        "anthropic" => "Claude",
        "openai" => "ChatGPT",
        "gemini" => "Gemini",
        _ => "AI off",
    };
}

/// <summary>A task the AI proposes. Nothing is added until the person confirms.</summary>
public sealed class PlannedTask
{
    [JsonPropertyName("title")] public string Title { get; set; } = "";
    [JsonPropertyName("due")] public string? Due { get; set; }
    [JsonPropertyName("priority")] public int Priority { get; set; }
    [JsonPropertyName("important")] public bool Important { get; set; }
    [JsonPropertyName("my_day")] public bool MyDay { get; set; }
    [JsonPropertyName("list")] public string? List { get; set; }
    [JsonPropertyName("notes")] public string? Notes { get; set; }
    [JsonPropertyName("subtasks")] public List<PlannedTask>? Subtasks { get; set; }
}

public static class AiPlanner
{
    private static readonly HttpClient Shared = new() { Timeout = TimeSpan.FromMinutes(3) };

    public static string SystemPrompt(DateTimeOffset now, IEnumerable<string> lists) => $$"""
        You turn what a person writes into to-do tasks for their task app. Reply with JSON only, no prose:
        {"tasks":[{"title":"...","due":"YYYY-MM-DDTHH:mm or null","priority":0,"important":false,"my_day":false,"list":"one of the lists or null","notes":null,"subtasks":[ same shape, nested as deep as needed ]}]}
        Rules: short imperative titles; keep the person's own words and names; only set "due" when they gave or clearly implied a time
        (today is {{now:dddd yyyy-MM-dd HH:mm}}, zone {{now:zzz}}); priority 0-5 (5 = do now) only when urgency is stated;
        "my_day" true for things meant for today; break a big job into subtasks when it has clear parts; never invent facts.
        Lists the person has: {{string.Join(", ", lists)}}.
        """;

    /// <summary>Asks the configured AI to plan tasks from a sentence or a pasted list.</summary>
    public static async Task<List<PlannedTask>> PlanAsync(AiConfig config, string request, DateTimeOffset now, IEnumerable<string> lists,
        HttpClient? http = null, CancellationToken ct = default)
    {
        if (!config.IsReady) throw new DocketException("Turn on an AI in Settings first: local (Ollama) or an API key.");
        http ??= Shared;
        var system = SystemPrompt(now, lists);
        if (config.Provider == "ollama" && await OllamaModelsAsync(config.OllamaUrl, ct) is { Count: > 0 } installed && !installed.Contains(config.Model))
            config = config with { Model = PickLocalModel(installed) ?? config.Model };
        var text = config.Provider switch
        {
            "ollama" => await OllamaAsync(http, config, system, request, ct),
            "anthropic" => await AnthropicAsync(http, config, system, request, ct),
            "openai" => await OpenAiAsync(http, config, system, request, ct),
            "gemini" => await GeminiAsync(http, config, system, request, ct),
            _ => throw new DocketException("No AI is selected."),
        };
        return ParsePlan(text);
    }

    public static List<PlannedTask> ParsePlan(string text)
    {
        var start = text.IndexOf('{');
        var end = text.LastIndexOf('}');
        if (start < 0 || end <= start) throw new DocketException("The AI didn't return tasks. Try rewording it.");
        try
        {
            using var doc = JsonDocument.Parse(text[start..(end + 1)]);
            var arr = doc.RootElement.TryGetProperty("tasks", out var t) ? t : doc.RootElement;
            var list = arr.ValueKind == JsonValueKind.Array ? arr.Deserialize<List<PlannedTask>>(VikunjaClient.Json) ?? [] : [];
            return list.Where(p => !string.IsNullOrWhiteSpace(p.Title)).ToList();
        }
        catch (JsonException) { throw new DocketException("The AI's answer wasn't valid. Try again."); }
    }

    private static async Task<string> PostAsync(HttpClient http, HttpRequestMessage req, CancellationToken ct)
    {
        using var res = await http.SendAsync(req, ct);
        var body = await res.Content.ReadAsStringAsync(ct);
        if (!res.IsSuccessStatusCode) throw new DocketException($"AI error {(int)res.StatusCode}: {Trim(body)}");
        return body;
    }

    private static string Trim(string s) => s.Length > 300 ? s[..300] + "…" : s;

    private static async Task<string> OllamaAsync(HttpClient http, AiConfig c, string system, string user, CancellationToken ct)
    {
        var req = new HttpRequestMessage(HttpMethod.Post, $"{c.OllamaUrl.TrimEnd('/')}/api/chat")
        {
            Content = JsonContent.Create(new { model = c.Model, stream = false, format = "json", messages = new[] { new { role = "system", content = system }, new { role = "user", content = user } } }),
        };
        try
        {
            using var doc = JsonDocument.Parse(await PostAsync(http, req, ct));
            return doc.RootElement.GetProperty("message").GetProperty("content").GetString() ?? "";
        }
        catch (HttpRequestException) { throw new DocketException("Local AI isn't running. Open Settings > AI to install or start Ollama."); }
    }

    private static async Task<string> AnthropicAsync(HttpClient http, AiConfig c, string system, string user, CancellationToken ct)
    {
        var req = new HttpRequestMessage(HttpMethod.Post, "https://api.anthropic.com/v1/messages")
        {
            Content = JsonContent.Create(new { model = c.Model, max_tokens = 2000, system, messages = new[] { new { role = "user", content = user } } }),
        };
        req.Headers.Add("x-api-key", c.ApiKey);
        req.Headers.Add("anthropic-version", "2023-06-01");
        using var doc = JsonDocument.Parse(await PostAsync(http, req, ct));
        var sb = new StringBuilder();
        foreach (var part in doc.RootElement.GetProperty("content").EnumerateArray())
            if (part.TryGetProperty("text", out var t)) sb.Append(t.GetString());
        return sb.ToString();
    }

    private static async Task<string> OpenAiAsync(HttpClient http, AiConfig c, string system, string user, CancellationToken ct)
    {
        var req = new HttpRequestMessage(HttpMethod.Post, "https://api.openai.com/v1/chat/completions")
        {
            Content = JsonContent.Create(new { model = c.Model, response_format = new { type = "json_object" }, messages = new[] { new { role = "system", content = system }, new { role = "user", content = user } } }),
        };
        req.Headers.Authorization = new("Bearer", c.ApiKey);
        using var doc = JsonDocument.Parse(await PostAsync(http, req, ct));
        return doc.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString() ?? "";
    }

    private static async Task<string> GeminiAsync(HttpClient http, AiConfig c, string system, string user, CancellationToken ct)
    {
        var req = new HttpRequestMessage(HttpMethod.Post, $"https://generativelanguage.googleapis.com/v1beta/models/{Uri.EscapeDataString(c.Model)}:generateContent")
        {
            Content = JsonContent.Create(new
            {
                systemInstruction = new { parts = new[] { new { text = system } } },
                contents = new[] { new { role = "user", parts = new[] { new { text = user } } } },
                generationConfig = new { responseMimeType = "application/json" },
            }),
        };
        req.Headers.Add("x-goog-api-key", c.ApiKey);
        using var doc = JsonDocument.Parse(await PostAsync(http, req, ct));
        return doc.RootElement.GetProperty("candidates")[0].GetProperty("content").GetProperty("parts")[0].GetProperty("text").GetString() ?? "";
    }

    // ---------- local AI (Ollama) ----------
    /// <summary>
    /// When the chosen model isn't installed, use one that is: skip embedding-only models, prefer a mid-size
    /// general model (around 7-14B fits a 12 GB GPU), else the first chat model.
    /// </summary>
    public static string? PickLocalModel(IReadOnlyList<string> installed)
    {
        var chat = installed.Where(m => !m.Contains("embed", StringComparison.OrdinalIgnoreCase)).ToList();
        string[] preferred = ["12b", "14b", "8b", "7b", "9b", "4b", "3b"];
        foreach (var size in preferred)
            if (chat.FirstOrDefault(m => m.Contains(size, StringComparison.OrdinalIgnoreCase) && !m.Contains("-vl", StringComparison.OrdinalIgnoreCase)) is { } hit) return hit;
        return chat.FirstOrDefault();
    }

    public static async Task<List<string>?> OllamaModelsAsync(string url, CancellationToken ct = default)
    {
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(3));
            using var doc = JsonDocument.Parse(await Shared.GetStringAsync($"{url.TrimEnd('/')}/api/tags", cts.Token));
            return doc.RootElement.GetProperty("models").EnumerateArray().Select(m => m.GetProperty("name").GetString() ?? "").Where(n => n.Length > 0).ToList();
        }
        catch (Exception e) when (e is HttpRequestException or OperationCanceledException or JsonException) { return null; }
    }

    /// <summary>Downloads a model into Ollama, reporting progress from 0 to 1.</summary>
    public static async Task PullModelAsync(string url, string model, IProgress<double>? progress = null, CancellationToken ct = default)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, $"{url.TrimEnd('/')}/api/pull") { Content = JsonContent.Create(new { model, stream = true }) };
        using var res = await Shared.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
        res.EnsureSuccessStatusCode();
        using var reader = new StreamReader(await res.Content.ReadAsStreamAsync(ct));
        while (await reader.ReadLineAsync(ct) is { } line)
        {
            if (line.Length == 0) continue;
            using var doc = JsonDocument.Parse(line);
            var r = doc.RootElement;
            if (r.TryGetProperty("error", out var err)) throw new DocketException($"Ollama: {err.GetString()}");
            if (r.TryGetProperty("total", out var total) && r.TryGetProperty("completed", out var done) && total.GetInt64() > 0)
                progress?.Report((double)done.GetInt64() / total.GetInt64());
        }
        progress?.Report(1);
    }
}

public sealed partial class DocketService
{
    /// <summary>Adds an AI-planned task (and its subtasks, at any depth) to the right list.</summary>
    public async Task<TaskInfo> AddPlannedAsync(PlannedTask plan, long fallbackProjectId, long? parentId = null, CancellationToken ct = default)
    {
        long projectId = fallbackProjectId;
        if (parentId is null && !string.IsNullOrWhiteSpace(plan.List))
        {
            var match = (await ProjectsAsync(false, ct)).FirstOrDefault(p => p.Title.Equals(plan.List.Trim(), StringComparison.OrdinalIgnoreCase));
            if (match is not null) projectId = match.Id;
        }
        DateTimeOffset? due = null;
        if (!string.IsNullOrWhiteSpace(plan.Due) && TryParseIso(plan.Due, Now, out var d)) due = d;
        var created = await client.CreateTaskAsync(projectId, plan.Title.Trim(), due, Math.Clamp(plan.Priority, 0, 5), ct)
            ?? throw new DocketException("Vikunja didn't return the new task.");
        if (parentId is { } pid) await client.AddRelationAsync(pid, created.Id, "subtask", ct);
        if (plan.Important) await SetImportantAsync(created.Id, true, ct);
        if (plan.MyDay) await SetMyDayAsync(created.Id, true, ct);
        if (!string.IsNullOrWhiteSpace(plan.Notes)) await SetNotesAndStepsAsync(created.Id, n => n with { Notes = plan.Notes.Trim() }, ct);
        foreach (var sub in plan.Subtasks ?? []) await AddPlannedAsync(sub, projectId, created.Id, ct);
        return new TaskInfo(await GetAsync(created.Id, ct), await ProjectNameAsync(projectId, ct));
    }

    /// <summary>Runs a slash command that acts on search matches. Returns how many tasks it changed.</summary>
    public async Task<int> ApplyToMatchesAsync(SlashCommand cmd, IEnumerable<long> ids, CancellationToken ct = default)
    {
        var n = 0;
        foreach (var id in ids)
        {
            switch (cmd.Kind)
            {
                case SlashKind.Done: await SetDoneAsync(id, true, ct); break;
                case SlashKind.MyDay: await SetMyDayAsync(id, true, ct); break;
                case SlashKind.Important: await SetImportantAsync(id, true, ct); break;
                default: continue;
            }
            n++;
        }
        return n;
    }
}
