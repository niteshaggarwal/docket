using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Docket.Mcp;

/// <summary>
/// "docket-mcp setup": signs in to Vikunja once and makes the connector's own API token, with no delete,
/// sharing, team, webhook, migration or CalDAV rights. The token is written to DOCKET_VIKUNJA_TOKEN_FILE
/// (default DOCKET_DATA_DIR/vikunja-token), readable only by this user. The password is never stored.
/// Used by the Docker setup: docker compose exec connector docket-mcp setup
/// </summary>
public static class Setup
{
    private static readonly string[] SkipGroups = ["caldav", "migration", "projects_shares", "projects_users", "projects_teams", "teams", "teams_members", "projects_webhooks", "webhooks"];

    public static async Task<int> RunAsync()
    {
        var url = ConnectorOptions.Env("DOCKET_VIKUNJA_URL") ?? Ask("Vikunja address (e.g. http://localhost:3456)");
        if (!Uri.TryCreate(url.TrimEnd('/') + "/api/v1/", UriKind.Absolute, out var api)) { Console.Error.WriteLine("That isn't a valid address."); return 2; }
        using var http = new HttpClient { BaseAddress = api, Timeout = TimeSpan.FromSeconds(30) };

        var user = Ask("Vikunja username");
        var password = AskSecret("Vikunja password");
        var totp = Ask("Two-factor code (Enter if not turned on)", allowEmpty: true);

        var login = new JsonObject { ["username"] = user, ["password"] = password, ["long_token"] = false };
        if (totp.Length > 0) login["totp_passcode"] = totp;
        var res = await http.PostAsJsonAsync("login", login);
        password = ""; login["password"] = "";
        if (!res.IsSuccessStatusCode) { Console.Error.WriteLine($"Sign-in failed ({(int)res.StatusCode}): {await res.Content.ReadAsStringAsync()}"); return 1; }
        var jwt = (await res.Content.ReadFromJsonAsync<JsonObject>())?["token"]?.GetValue<string>();
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", jwt);

        var routes = await http.GetFromJsonAsync<JsonObject>("routes") ?? [];
        var perms = new JsonObject();
        foreach (var (group, actions) in routes)
        {
            if (SkipGroups.Contains(group) || actions is not JsonObject a) continue;
            var safe = a.Select(kv => kv.Key).Where(k => !k.Contains("delete", StringComparison.OrdinalIgnoreCase)).ToArray();
            if (safe.Length > 0) perms[group] = new JsonArray([.. safe.Select(s => (JsonNode)s)]);
        }
        var body = new JsonObject
        {
            ["title"] = "Docket AI connector",
            ["expires_at"] = DateTimeOffset.UtcNow.AddYears(1).ToString("yyyy-MM-ddTHH:mm:ssZ"),
            ["permissions"] = perms,
        };
        var put = await http.PutAsync("tokens", new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json"));
        if (!put.IsSuccessStatusCode) { Console.Error.WriteLine($"Couldn't create the token ({(int)put.StatusCode}): {await put.Content.ReadAsStringAsync()}"); return 1; }
        var token = (await put.Content.ReadFromJsonAsync<JsonObject>())?["token"]?.GetValue<string>();
        if (string.IsNullOrEmpty(token)) { Console.Error.WriteLine("Vikunja didn't return a token."); return 1; }

        var file = ConnectorOptions.TokenFile;
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        await File.WriteAllTextAsync(file, token);
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(file, UnixFileMode.UserRead | UnixFileMode.UserWrite);

        Console.WriteLine($"""
            Done. The connector's token (valid one year, no delete rights) is saved in {file}.
            A running connector picks it up within a few seconds (or: docker compose restart connector).
            Revoke it any time in Vikunja > Settings > API Tokens ("Docket AI connector").
            """);
        return 0;
    }

    private static string Ask(string prompt, bool allowEmpty = false)
    {
        while (true)
        {
            Console.Write(prompt + ": ");
            var s = Console.ReadLine()?.Trim() ?? "";
            if (s.Length > 0 || allowEmpty) return s;
        }
    }

    private static string AskSecret(string prompt)
    {
        Console.Write(prompt + ": ");
        if (Console.IsInputRedirected) return Console.ReadLine() ?? "";
        var sb = new StringBuilder();
        while (true)
        {
            var k = Console.ReadKey(intercept: true);
            if (k.Key == ConsoleKey.Enter) break;
            if (k.Key == ConsoleKey.Backspace) { if (sb.Length > 0) sb.Length--; continue; }
            if (!char.IsControl(k.KeyChar)) sb.Append(k.KeyChar);
        }
        Console.WriteLine();
        return sb.ToString();
    }
}
