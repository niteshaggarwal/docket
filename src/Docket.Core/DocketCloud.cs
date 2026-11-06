using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Docket.Core;

/// <summary>The person's Docket Pro status, as Docket Cloud reports it.</summary>
public sealed class ProStatus
{
    [JsonPropertyName("plan")] public string Plan { get; set; } = "Free";
    [JsonPropertyName("planExpires")] public DateTimeOffset? PlanExpires { get; set; }
    [JsonPropertyName("seats")] public int Seats { get; set; }
    [JsonPropertyName("credits")] public ProCredits Credits { get; set; } = new();
    [JsonIgnore] public bool IsPaid => !Plan.Equals("Free", StringComparison.OrdinalIgnoreCase);
}

public sealed class ProCredits
{
    [JsonPropertyName("limit")] public int Limit { get; set; }
    [JsonPropertyName("left")] public int Left { get; set; }
    [JsonPropertyName("renewsAt")] public DateTimeOffset? RenewsAt { get; set; }
}

/// <summary>
/// Talks to Docket Cloud (Docket Pro): plan and credits, subscribing through Dodo Payments checkout, and the billing
/// portal. Every call carries the person's own Docket server token, which Docket Cloud checks with the server.
/// </summary>
public sealed class DocketCloud(string cloudUrl, string serverToken, HttpClient? http = null)
{
    private readonly HttpClient _http = http ?? new HttpClient { Timeout = TimeSpan.FromSeconds(20) };

    public static bool IsConfigured(string? cloudUrl) => Uri.TryCreate(cloudUrl, UriKind.Absolute, out var u) && u.Scheme is "https" or "http";

    public Task<ProStatus> StatusAsync(CancellationToken ct = default) => SendAsync<ProStatus>(HttpMethod.Get, "api/pro/me", null, ct);

    /// <summary>A Dodo checkout page for a plan ("Pro", "Family", "Team") billed each "month" or "year".</summary>
    public async Task<string> CheckoutUrlAsync(string plan, string interval, int? seats = null, CancellationToken ct = default) =>
        (await SendAsync<JsonElement>(HttpMethod.Post, "api/pro/checkout", new { plan, interval, seats }, ct)).GetProperty("url").GetString()
        ?? throw new DocketException("Docket Cloud didn't return a checkout page.");

    /// <summary>Dodo's customer portal: change plan, update the card, see invoices or cancel.</summary>
    public async Task<string> BillingPortalUrlAsync(CancellationToken ct = default) =>
        (await SendAsync<JsonElement>(HttpMethod.Post, "api/pro/billing-portal", new { }, ct)).GetProperty("url").GetString()
        ?? throw new DocketException("Docket Cloud didn't return a billing page.");

    public Task ActivateLicenceAsync(string key, CancellationToken ct = default) =>
        SendAsync<JsonElement>(HttpMethod.Post, "api/pro/licence/activate", new { key = key.Trim() }, ct);

    private async Task<T> SendAsync<T>(HttpMethod method, string path, object? body, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(method, $"{cloudUrl.TrimEnd('/')}/{path}");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", serverToken);
        if (body is not null) req.Content = JsonContent.Create(body);
        HttpResponseMessage res;
        try { res = await _http.SendAsync(req, ct); }
        catch (HttpRequestException e) { throw new DocketException($"Can't reach Docket Cloud ({e.Message})."); }
        using (res)
        {
            var text = await res.Content.ReadAsStringAsync(ct);
            if (!res.IsSuccessStatusCode)
            {
                var msg = text;
                try { using var d = JsonDocument.Parse(text); if (d.RootElement.TryGetProperty("detail", out var x)) msg = x.GetString() ?? text; }
                catch (JsonException) { }
                throw new DocketException(msg.Length > 300 ? msg[..300] : msg);
            }
            return JsonSerializer.Deserialize<T>(text, VikunjaClient.Json)!;
        }
    }
}
