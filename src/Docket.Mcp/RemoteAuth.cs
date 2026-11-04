using System.Collections.Concurrent;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace Docket.Mcp;

/// <summary>
/// Sign-in for the remote connector. Two ways in, both tied to one secret, the connector key (DOCKET_MCP_KEY):
/// - Clients that can send a header (Claude Code, LM Studio, scripts, ChatGPT actions): "Authorization: Bearer KEY".
/// - Clients that need OAuth (claude.ai custom connectors, ChatGPT connectors): a small OAuth 2.1 server with
///   dynamic client registration and PKCE. Its sign-in page asks for the connector key once, then issues
///   access and refresh tokens. Only SHA-256 hashes of tokens are stored, in DOCKET_DATA_DIR/oauth.json.
/// Protections: redirect hosts are allow-listed (DOCKET_OAUTH_REDIRECT_HOSTS), registration is size- and count-capped,
/// wrong keys are rate-limited per address, and changing the key revokes every OAuth client and token.
/// </summary>
public sealed class RemoteAuth
{
    private readonly byte[] _key;
    private readonly string _file;
    private readonly Lock _gate = new();
    private readonly Store _store;
    private readonly ConcurrentDictionary<string, Code> _codes = new();
    private readonly ConcurrentDictionary<string, ConcurrentQueue<DateTimeOffset>> _failures = new();
    private readonly string[] _redirectHosts;

    private static readonly TimeSpan AccessLife = TimeSpan.FromHours(12);
    private static readonly TimeSpan RefreshLife = TimeSpan.FromDays(90);
    private const int MaxClients = 200, MaxRedirects = 5, MaxUriLength = 2048, MaxBody = 16 * 1024;
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    /// <summary>Where the big AI apps send OAuth users back. Loopback (desktop apps) is always allowed.</summary>
    public const string DefaultRedirectHosts = "claude.ai,claude.com,chatgpt.com,chat.openai.com,platform.openai.com";

    public RemoteAuth(string key, string dataDir)
    {
        if (key.Length < 24) throw new InvalidOperationException("DOCKET_MCP_KEY must be at least 24 characters. Make one with: openssl rand -hex 32");
        _key = SHA256.HashData(Encoding.UTF8.GetBytes(key));
        _file = Path.Combine(dataDir, "oauth.json");
        _redirectHosts = (ConnectorOptions.Env("DOCKET_OAUTH_REDIRECT_HOSTS") ?? DefaultRedirectHosts)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(h => h.ToLowerInvariant()).ToArray();
        _store = LoadStore(_file);

        // A new connector key revokes everything issued under the old one.
        var keyId = Convert.ToHexString(SHA256.HashData(_key));
        if (_store.KeyId != keyId)
        {
            _store.KeyId = keyId;
            _store.Clients.Clear();
            _store.Tokens.Clear();
            Save();
        }
    }

    private sealed class Store
    {
        public string? KeyId { get; set; }
        public Dictionary<string, Client> Clients { get; set; } = [];
        public Dictionary<string, Grant> Tokens { get; set; } = []; // key: sha256(token)
    }
    private sealed record Client(string Name, string[] RedirectUris, DateTimeOffset Created);
    private sealed record Grant(string ClientId, bool Refresh, DateTimeOffset Expires);
    private sealed record Code(string ClientId, string RedirectUri, string Challenge, DateTimeOffset Expires);

    // ---- checking requests -------------------------------------------------------------------

    public bool IsAuthorized(HttpContext ctx)
    {
        var header = ctx.Request.Headers.Authorization.ToString();
        if (!header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)) return false;
        var token = header[7..].Trim();
        if (token.Length == 0) return false;
        if (KeyMatches(token)) return true;
        var h = Hash(token);
        lock (_gate)
            return _store.Tokens.TryGetValue(h, out var g) && !g.Refresh && g.Expires > DateTimeOffset.UtcNow;
    }

    public static async Task Challenge(HttpContext ctx)
    {
        ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
        ctx.Response.Headers.WWWAuthenticate = $"Bearer resource_metadata=\"{BaseUrl(ctx)}/.well-known/oauth-protected-resource\"";
        await ctx.Response.WriteAsJsonAsync(new { error = "unauthorized", hint = "Send Authorization: Bearer <connector key>, or sign in with OAuth." });
    }

    private bool KeyMatches(string candidate) =>
        CryptographicOperations.FixedTimeEquals(_key, SHA256.HashData(Encoding.UTF8.GetBytes(candidate)));

    /// <summary>Public address: DOCKET_PUBLIC_URL when set, else the request's scheme and host (behind a proxy, via X-Forwarded-*).</summary>
    public static string BaseUrl(HttpContext ctx) =>
        ConnectorOptions.Env("DOCKET_PUBLIC_URL")?.TrimEnd('/') ?? $"{ctx.Request.Scheme}://{ctx.Request.Host}";

    // ---- OAuth endpoints -----------------------------------------------------------------------

    public void Map(WebApplication app)
    {
        app.MapGet("/.well-known/oauth-protected-resource/{**rest}", (HttpContext c) => Resource(c));
        app.MapGet("/.well-known/oauth-protected-resource", (HttpContext c) => Resource(c));
        app.MapGet("/.well-known/oauth-authorization-server/{**rest}", (HttpContext c) => Metadata(c));
        app.MapGet("/.well-known/oauth-authorization-server", (HttpContext c) => Metadata(c));
        app.MapPost("/oauth/register", (Func<HttpContext, Task<IResult>>)Register);
        app.MapGet("/oauth/authorize", (HttpContext c) => AuthorizePage(c, null));
        app.MapPost("/oauth/authorize", (Func<HttpContext, Task<IResult>>)AuthorizeSubmit).DisableAntiforgery();
        app.MapPost("/oauth/token", (Func<HttpContext, Task<IResult>>)Token).DisableAntiforgery();
    }

    private static IResult Resource(HttpContext c)
    {
        var b = BaseUrl(c);
        return Results.Json(new { resource = $"{b}/mcp", authorization_servers = new[] { b }, bearer_methods_supported = new[] { "header" }, resource_name = "Docket tasks" });
    }

    private static IResult Metadata(HttpContext c)
    {
        var b = BaseUrl(c);
        return Results.Json(new Dictionary<string, object>
        {
            ["issuer"] = b,
            ["authorization_endpoint"] = $"{b}/oauth/authorize",
            ["token_endpoint"] = $"{b}/oauth/token",
            ["registration_endpoint"] = $"{b}/oauth/register",
            ["response_types_supported"] = new[] { "code" },
            ["grant_types_supported"] = new[] { "authorization_code", "refresh_token" },
            ["code_challenge_methods_supported"] = new[] { "S256" },
            ["token_endpoint_auth_methods_supported"] = new[] { "none" },
            ["scopes_supported"] = new[] { "tasks" },
        });
    }

    private async Task<IResult> Register(HttpContext c)
    {
        if (c.Request.ContentLength is > MaxBody) return OAuthError("invalid_client_metadata", "Registration is too large.");
        JsonElement body;
        try
        {
            using var limited = new MemoryStream();
            var buffer = new byte[4096];
            int n;
            while ((n = await c.Request.Body.ReadAsync(buffer, c.RequestAborted)) > 0)
            {
                if (limited.Length + n > MaxBody) return OAuthError("invalid_client_metadata", "Registration is too large.");
                limited.Write(buffer, 0, n);
            }
            body = JsonSerializer.Deserialize<JsonElement>(limited.ToArray());
        }
        catch (JsonException) { return OAuthError("invalid_client_metadata", "Body must be JSON."); }
        if (body.ValueKind != JsonValueKind.Object) return OAuthError("invalid_client_metadata", "Body must be a JSON object.");

        var uris = body.TryGetProperty("redirect_uris", out var r) && r.ValueKind == JsonValueKind.Array
            ? r.EnumerateArray().Select(x => x.ValueKind == JsonValueKind.String ? x.GetString() ?? "" : "").ToArray() : [];
        if (uris.Length == 0 || uris.Length > MaxRedirects || uris.Any(u => u.Length is 0 or > MaxUriLength))
            return OAuthError("invalid_redirect_uri", $"Give 1 to {MaxRedirects} redirect_uris.");
        if (uris.FirstOrDefault(u => !AllowedRedirect(u)) is { } bad)
            return OAuthError("invalid_redirect_uri",
                $"{bad} isn't an allowed return address. Allowed: https on {string.Join(", ", _redirectHosts)}, or localhost. " +
                "The server owner can add hosts with DOCKET_OAUTH_REDIRECT_HOSTS.");
        var name = body.TryGetProperty("client_name", out var nm) && nm.ValueKind == JsonValueKind.String ? nm.GetString() ?? "" : "";
        if (name.Length == 0) name = "AI app";

        var id = NewSecret(16);
        lock (_gate)
        {
            // Hard cap: forget the oldest clients that never finished signing in, then the oldest overall.
            while (_store.Clients.Count >= MaxClients)
            {
                var used = _store.Tokens.Values.Select(t => t.ClientId).ToHashSet();
                var victim = _store.Clients.OrderBy(kv => used.Contains(kv.Key)).ThenBy(kv => kv.Value.Created).First().Key;
                _store.Clients.Remove(victim);
                foreach (var t in _store.Tokens.Where(kv => kv.Value.ClientId == victim).Select(kv => kv.Key).ToList()) _store.Tokens.Remove(t);
            }
            _store.Clients[id] = new Client(name[..Math.Min(80, name.Length)], uris, DateTimeOffset.UtcNow);
            Save();
        }
        return NoStore(c, Results.Json(new Dictionary<string, object>
        {
            ["client_id"] = id,
            ["client_name"] = name,
            ["redirect_uris"] = uris,
            ["grant_types"] = new[] { "authorization_code", "refresh_token" },
            ["response_types"] = new[] { "code" },
            ["token_endpoint_auth_method"] = "none",
            ["client_id_issued_at"] = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
        }, statusCode: StatusCodes.Status201Created));
    }

    private IResult AuthorizePage(HttpContext c, string? error)
    {
        var q = c.Request.HasFormContentType ? c.Request.Form.ToDictionary(k => k.Key, k => k.Value.ToString()) : c.Request.Query.ToDictionary(k => k.Key, k => k.Value.ToString());
        string P(string k) => q.TryGetValue(k, out var v) ? v : "";

        if (!TryClient(P("client_id"), P("redirect_uri"), out var client))
            return Html(c, "This sign-in link isn't valid", "<p>The app asking for access isn't registered with this Docket server, or its return address doesn't match. Start the connection again from your AI app.</p>", 400);
        if (P("response_type") != "code" || P("code_challenge").Length < 43 || P("code_challenge_method") != "S256")
            return Html(c, "This sign-in link isn't valid", "<p>The request is missing PKCE (S256). Start the connection again from your AI app.</p>", 400);

        var host = new Uri(P("redirect_uri")).Host;
        var hidden = string.Concat(new[] { "client_id", "redirect_uri", "state", "code_challenge", "code_challenge_method", "response_type", "scope", "resource" }
            .Select(k => $"<input type=\"hidden\" name=\"{k}\" value=\"{WebUtility.HtmlEncode(P(k))}\">"));
        var err = error is null ? "" : $"<p class=\"err\">{WebUtility.HtmlEncode(error)}</p>";
        var body = $"""
            <p>An app calling itself <b>{WebUtility.HtmlEncode(client.Name)}</b> wants to read and change your Docket tasks. It can't delete anything.</p>
            <p class="host">Access goes to <b>{WebUtility.HtmlEncode(host)}</b></p>
            <form method="post">{hidden}
              <label for="key">Connector key</label>
              <input id="key" name="key" type="password" autocomplete="off" autofocus required>
              {err}
              <button type="submit">Allow access</button>
            </form>
            <p class="small">Only continue if you just started connecting this app yourself and the address above is the app you expect.
            The connector key is DOCKET_MCP_KEY in your server's .env file.</p>
            """;
        return Html(c, "Connect to Docket", body, 200);
    }

    private async Task<IResult> AuthorizeSubmit(HttpContext c)
    {
        var f = await c.Request.ReadFormAsync();
        string P(string k) => f[k].ToString();
        var who = c.Connection.RemoteIpAddress?.ToString() ?? "unknown";

        if (TooManyFailures(who)) return Html(c, "Try again later", "<p>Too many wrong keys. Wait 15 minutes, then try again.</p>", 429);
        if (!TryClient(P("client_id"), P("redirect_uri"), out _)) return AuthorizePage(c, null);
        if (P("response_type") != "code" || P("code_challenge").Length < 43 || P("code_challenge_method") != "S256") return AuthorizePage(c, null);
        if (!KeyMatches(P("key")))
        {
            _failures.GetOrAdd(who, _ => new()).Enqueue(DateTimeOffset.UtcNow);
            await Task.Delay(600);
            return AuthorizePage(c, "That key isn't right.");
        }

        foreach (var old in _codes.Where(kv => kv.Value.Expires < DateTimeOffset.UtcNow).Select(kv => kv.Key).ToList()) _codes.TryRemove(old, out _);
        var code = NewSecret(32);
        _codes[code] = new Code(P("client_id"), P("redirect_uri"), P("code_challenge"), DateTimeOffset.UtcNow.AddMinutes(2));
        var sep = P("redirect_uri").Contains('?') ? '&' : '?';
        var state = P("state");
        var target = $"{P("redirect_uri")}{sep}code={Uri.EscapeDataString(code)}{(state.Length > 0 ? "&state=" + Uri.EscapeDataString(state) : "")}&iss={Uri.EscapeDataString(BaseUrl(c))}";
        return Results.Redirect(target);
    }

    private async Task<IResult> Token(HttpContext c)
    {
        if (!c.Request.HasFormContentType) return NoStore(c, OAuthError("invalid_request", "Use application/x-www-form-urlencoded."));
        var f = await c.Request.ReadFormAsync();
        string P(string k) => f[k].ToString();
        var clientId = P("client_id");
        if (clientId.Length == 0 && c.Request.Headers.Authorization.ToString() is var basic && basic.StartsWith("Basic ", StringComparison.OrdinalIgnoreCase))
        {
            try { clientId = Uri.UnescapeDataString(Encoding.UTF8.GetString(Convert.FromBase64String(basic[6..])).Split(':')[0]); } catch (FormatException) { }
        }

        switch (P("grant_type"))
        {
            case "authorization_code":
                if (!_codes.TryRemove(P("code"), out var code) || code.Expires < DateTimeOffset.UtcNow)
                    return NoStore(c, OAuthError("invalid_grant", "The code is unknown or expired."));
                if (code.ClientId != clientId || code.RedirectUri != P("redirect_uri"))
                    return NoStore(c, OAuthError("invalid_grant", "client_id or redirect_uri doesn't match."));
                var verifier = P("code_verifier");
                var expected = Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
                if (verifier.Length < 43 || !CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(expected), Encoding.ASCII.GetBytes(code.Challenge)))
                    return NoStore(c, OAuthError("invalid_grant", "PKCE check failed."));
                return NoStore(c, Results.Json(Issue(clientId)));

            case "refresh_token":
                var h = Hash(P("refresh_token"));
                lock (_gate)
                {
                    if (!_store.Tokens.TryGetValue(h, out var g) || !g.Refresh || g.Expires < DateTimeOffset.UtcNow || (clientId.Length > 0 && g.ClientId != clientId))
                        return NoStore(c, OAuthError("invalid_grant", "The refresh token is unknown or expired."));
                    _store.Tokens.Remove(h); // rotate
                    return NoStore(c, Results.Json(Issue(g.ClientId)));
                }

            default:
                return NoStore(c, OAuthError("unsupported_grant_type", "Use authorization_code or refresh_token."));
        }
    }

    private object Issue(string clientId)
    {
        var access = NewSecret(32);
        var refresh = NewSecret(32);
        var now = DateTimeOffset.UtcNow;
        lock (_gate)
        {
            foreach (var dead in _store.Tokens.Where(kv => kv.Value.Expires < now).Select(kv => kv.Key).ToList()) _store.Tokens.Remove(dead);
            _store.Tokens[Hash(access)] = new Grant(clientId, false, now + AccessLife);
            _store.Tokens[Hash(refresh)] = new Grant(clientId, true, now + RefreshLife);
            Save();
        }
        return new Dictionary<string, object>
        {
            ["access_token"] = access, ["token_type"] = "Bearer", ["expires_in"] = (int)AccessLife.TotalSeconds,
            ["refresh_token"] = refresh, ["scope"] = "tasks",
        };
    }

    // ---- helpers -------------------------------------------------------------------------------

    private bool TryClient(string id, string redirect, out Client client)
    {
        lock (_gate)
        {
            if (_store.Clients.TryGetValue(id, out var c) && c.RedirectUris.Contains(redirect)) { client = c; return true; }
        }
        client = null!;
        return false;
    }

    /// <summary>10 wrong keys per address per 15 minutes, and 100 in total, pause sign-in.</summary>
    private bool TooManyFailures(string who)
    {
        var cutoff = DateTimeOffset.UtcNow.AddMinutes(-15);
        var total = 0;
        foreach (var (addr, q) in _failures)
        {
            while (q.TryPeek(out var t) && t < cutoff) q.TryDequeue(out _);
            if (q.IsEmpty) _failures.TryRemove(addr, out _);
            total += q.Count;
        }
        return total >= 100 || (_failures.TryGetValue(who, out var mine) && mine.Count >= 10);
    }

    private bool AllowedRedirect(string uri)
    {
        if (!Uri.TryCreate(uri, UriKind.Absolute, out var u) || !string.IsNullOrEmpty(u.Fragment) || !string.IsNullOrEmpty(u.UserInfo)) return false;
        if (u.IsLoopback || u.Host == "localhost") return u.Scheme == Uri.UriSchemeHttp || u.Scheme == Uri.UriSchemeHttps;
        if (u.Scheme != Uri.UriSchemeHttps) return false;
        var host = u.Host.ToLowerInvariant();
        return _redirectHosts.Any(h => h == "*" || host == h || host.EndsWith("." + h, StringComparison.Ordinal));
    }

    private static IResult OAuthError(string error, string description) =>
        Results.Json(new { error, error_description = description }, statusCode: StatusCodes.Status400BadRequest);

    private static IResult NoStore(HttpContext c, IResult r)
    {
        c.Response.Headers.CacheControl = "no-store";
        c.Response.Headers.Pragma = "no-cache";
        return r;
    }

    private static IResult Html(HttpContext c, string title, string body, int status)
    {
        c.Response.Headers.ContentSecurityPolicy = "default-src 'none'; style-src 'unsafe-inline'; form-action 'self' https: http://localhost:* http://127.0.0.1:*; frame-ancestors 'none'; base-uri 'none'";
        c.Response.Headers.XFrameOptions = "DENY";
        c.Response.Headers.CacheControl = "no-store";
        c.Response.Headers["Referrer-Policy"] = "no-referrer";
        return Results.Content(Page(title, body), "text/html; charset=utf-8", statusCode: status);
    }

    private static string NewSecret(int bytes) => Base64Url(RandomNumberGenerator.GetBytes(bytes));
    private static string Base64Url(byte[] b) => Convert.ToBase64String(b).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    private static string Hash(string s) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(s)));

    private static Store LoadStore(string file)
    {
        try { return File.Exists(file) ? JsonSerializer.Deserialize<Store>(File.ReadAllText(file)) ?? new() : new(); }
        catch (Exception e) when (e is IOException or JsonException) { return new(); }
    }

    private void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_file)!);
        var tmp = _file + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(_store, Json));
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(tmp, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        File.Move(tmp, _file, overwrite: true);
    }

    private static string Page(string title, string body) => $$"""
        <!doctype html><html lang="en"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">
        <title>{{WebUtility.HtmlEncode(title)}}</title>
        <style>
          :root{color-scheme:light dark;--bg:#f3f3f3;--card:#fff;--fg:#1b1b1b;--muted:#5f5f5f;--accent:#1f4fd8;--err:#c42b1c;--chip:#eef2fb}
          @media (prefers-color-scheme:dark){:root{--bg:#202020;--card:#2b2b2b;--fg:#fff;--muted:#c5c5c5;--accent:#6ea0ff;--err:#ff99a4;--chip:#33384a} }
          body{margin:0;min-height:100vh;display:grid;place-items:center;background:var(--bg);color:var(--fg);font:15px/1.5 "Segoe UI Variable","Segoe UI",system-ui,sans-serif;padding:16px;box-sizing:border-box}
          main{background:var(--card);border-radius:8px;padding:28px;max-width:400px;width:100%;box-shadow:0 2px 8px rgba(0,0,0,.12)}
          h1{font-size:20px;margin:0 0 12px} label{display:block;font-weight:600;margin:16px 0 6px}
          .host{background:var(--chip);border-radius:4px;padding:8px 10px;margin:12px 0;word-break:break-all}
          input[type=password]{width:100%;box-sizing:border-box;padding:8px 10px;border-radius:4px;border:1px solid #8a8a8a;background:transparent;color:inherit;font:inherit}
          button{margin-top:16px;width:100%;padding:9px;border:0;border-radius:4px;background:var(--accent);color:#fff;font:inherit;font-weight:600;cursor:pointer}
          .err{color:var(--err);margin:8px 0 0}.small{color:var(--muted);font-size:13px;margin-top:16px}
        </style></head><body><main><h1>{{WebUtility.HtmlEncode(title)}}</h1>{{body}}</main></body></html>
        """;
}
