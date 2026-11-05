using System.Net.Http.Json;
using System.Text.Json;
using Docket.Core;

namespace Docket.Mobile.Services;

/// <summary>My Day's reset date, kept in app preferences so it resets once a day on the phone.</summary>
public sealed class PhoneDocketState : IDocketState
{
    private const string Key = "myday.date";
    public DateOnly? MyDayDate { get; set; } = Read();
    public void RefreshMyDayDate() => MyDayDate = Read();
    public void SaveMyDayDate(DateOnly day) { MyDayDate = day; Preferences.Default.Set(Key, day.ToString("O")); }
    private static DateOnly? Read() => DateOnly.TryParse(Preferences.Default.Get(Key, ""), out var d) ? d : null;
}

/// <summary>
/// The signed-in server. Works with any Vikunja server (a Tailscale address now, a hosted one later).
/// Signing in creates an API token for this phone; the password itself is never stored.
/// </summary>
public sealed class Session
{
    private const string ServerKey = "docket.server";
    private const string TokenKey = "docket.token";

    public string? ServerUrl { get; private set; }
    public DocketService? Service { get; private set; }
    public bool IsSignedIn => Service is not null;

    public event EventHandler? Changed;

    public async Task<bool> RestoreAsync()
    {
        ServerUrl = await SecureStorage.Default.GetAsync(ServerKey);
        var token = await SecureStorage.Default.GetAsync(TokenKey);
        if (string.IsNullOrEmpty(ServerUrl) || string.IsNullOrEmpty(token)) return false;
        Use(ServerUrl, token);
        return true;
    }

    /// <summary>Signs in with username and password, then makes a token named after this device.</summary>
    public async Task SignInAsync(string server, string username, string password, string? totp, CancellationToken ct = default)
    {
        var baseUrl = Normalise(server);
        using var http = new HttpClient { BaseAddress = new Uri(baseUrl + "/api/v1/"), Timeout = TimeSpan.FromSeconds(20) };

        var login = new Dictionary<string, object?> { ["username"] = username.Trim(), ["password"] = password, ["long_token"] = false };
        if (!string.IsNullOrWhiteSpace(totp)) login["totp_passcode"] = totp.Trim();
        using var res = await http.PostAsJsonAsync("login", login, ct);
        if (!res.IsSuccessStatusCode) throw new DocketException(await ErrorMessage(res, ct, "Couldn't sign in. Check the username and password."));
        var jwt = (await res.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("token").GetString()!;
        http.DefaultRequestHeaders.Authorization = new("Bearer", jwt);

        // Everything this app needs (tasks, lists, labels, files, comments, sharing, teams), nothing for CalDAV, migration or webhooks.
        var routes = await http.GetFromJsonAsync<Dictionary<string, Dictionary<string, JsonElement>>>("routes", ct) ?? [];
        string[] skip = ["caldav", "migration", "projects_webhooks", "webhooks"];
        var perms = routes.Where(r => !skip.Contains(r.Key)).ToDictionary(r => r.Key, r => r.Value.Keys.ToArray());
        var body = new { title = $"Docket on {DeviceInfo.Current.Name}".Trim(), expires_at = DateTime.UtcNow.AddYears(1).ToString("yyyy-MM-ddTHH:mm:ssZ"), permissions = perms };
        using var tokRes = await http.PutAsJsonAsync("tokens", body, ct);
        if (!tokRes.IsSuccessStatusCode) throw new DocketException(await ErrorMessage(tokRes, ct, "Signed in, but couldn't create a token for this phone."));
        var token = (await tokRes.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("token").GetString()!;

        await SecureStorage.Default.SetAsync(ServerKey, baseUrl);
        await SecureStorage.Default.SetAsync(TokenKey, token);
        Use(baseUrl, token);
    }

    public void SignOut()
    {
        SecureStorage.Default.Remove(TokenKey);
        Service?.Client.Dispose();
        Service = null;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void Use(string server, string token)
    {
        Service?.Client.Dispose();
        ServerUrl = server;
        Service = new DocketService(new VikunjaClient(new Uri(server), token)) { State = new PhoneDocketState() };
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Accepts "pc.tailnet.ts.net", "https://tasks.example.com/" and similar.</summary>
    public static string Normalise(string server)
    {
        var s = server.Trim().TrimEnd('/');
        if (s.Length == 0) throw new DocketException("Enter your server's address.");
        if (!s.StartsWith("http://", StringComparison.OrdinalIgnoreCase) && !s.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) s = "https://" + s;
        if (s.EndsWith("/api/v1", StringComparison.OrdinalIgnoreCase)) s = s[..^7];
        return Uri.TryCreate(s, UriKind.Absolute, out _) ? s : throw new DocketException($"\"{server}\" isn't a valid address.");
    }

    private static async Task<string> ErrorMessage(HttpResponseMessage res, CancellationToken ct, string fallback)
    {
        try
        {
            var e = await res.Content.ReadFromJsonAsync<JsonElement>(ct);
            if (e.TryGetProperty("message", out var m) && m.GetString() is { Length: > 0 } msg) return msg;
        }
        catch (JsonException) { }
        return fallback;
    }
}
