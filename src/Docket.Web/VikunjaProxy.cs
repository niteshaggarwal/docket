using System.Net;
using System.Net.WebSockets;
using System.Text;
using Microsoft.AspNetCore.Http.Features;

namespace Docket.Web;

/// <summary>Forwards requests and WebSockets to Vikunja, rebranding HTML pages and the web manifest on the way back.</summary>
public sealed class VikunjaProxy
{
    // Headers that describe one connection, not the message; they are never forwarded.
    private static readonly HashSet<string> HopByHop = new(StringComparer.OrdinalIgnoreCase)
    {
        "Connection", "Keep-Alive", "Proxy-Connection", "Proxy-Authenticate", "Proxy-Authorization", "TE", "Trailer",
        "Transfer-Encoding", "Upgrade", "Host", "Alt-Svc",
    };

    private readonly Uri _upstream;
    private readonly string _upstreamOrigin;
    private readonly string _upstreamBase; // scheme://host[:port][/path] without a trailing slash
    private readonly HttpMessageInvoker _http;
    private readonly ILogger _log;

    public VikunjaProxy(Uri upstream, ILogger<VikunjaProxy> log)
    {
        _upstream = upstream;
        _log = log;
        _upstreamOrigin = upstream.GetLeftPart(UriPartial.Authority);
        _upstreamBase = upstream.GetLeftPart(UriPartial.Path).TrimEnd('/');
        _http = new HttpMessageInvoker(new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            UseCookies = false,
            UseProxy = false,
            AutomaticDecompression = DecompressionMethods.None,
            ConnectTimeout = TimeSpan.FromSeconds(10),
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
            EnableMultipleHttp2Connections = true,
        });
    }

    public Task HandleAsync(HttpContext ctx, SkinAssets skin) =>
        ctx.WebSockets.IsWebSocketRequest ? RelayWebSocketAsync(ctx) : ForwardAsync(ctx, skin);

    private async Task ForwardAsync(HttpContext ctx, SkinAssets skin)
    {
        var req = ctx.Request;
        var path = req.Path.Value ?? "/";
        var isPage = IsPageRequest(path, req.Method);
        using var msg = new HttpRequestMessage(new HttpMethod(req.Method), _upstreamBase + req.Path.ToUriComponent() + req.QueryString.ToUriComponent());

        if (req.ContentLength > 0 || req.Headers.TransferEncoding.Count > 0)
            msg.Content = new StreamContent(req.Body);

        foreach (var (name, values) in req.Headers)
        {
            if (HopByHop.Contains(name) || name.StartsWith(':')) continue;
            // Pages are fetched uncompressed so they can be rewritten, and always fresh so the skin link is current.
            if (isPage && (name.Equals("Accept-Encoding", StringComparison.OrdinalIgnoreCase)
                || name.Equals("If-None-Match", StringComparison.OrdinalIgnoreCase)
                || name.Equals("If-Modified-Since", StringComparison.OrdinalIgnoreCase))) continue;
            string?[] v = values.ToArray();
            // Vikunja checks Origin against its own address; the browser's origin here is this proxy.
            if (name.Equals("Origin", StringComparison.OrdinalIgnoreCase) && values.ToString() != "null")
                v = [_upstreamOrigin];
            if (!msg.Headers.TryAddWithoutValidation(name, v) && msg.Content is not null)
                msg.Content.Headers.TryAddWithoutValidation(name, v);
        }
        AddForwardedHeaders(ctx, msg.Headers);

        HttpResponseMessage resp;
        try
        {
            resp = await _http.SendAsync(msg, ctx.RequestAborted);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !ctx.RequestAborted.IsCancellationRequested)
        {
            _log.LogWarning("{Method} {Path}: Vikunja unreachable: {Error}", req.Method, path, ex.GetBaseException().Message);
            await WriteUnavailableAsync(ctx, isPage);
            return;
        }
        catch (OperationCanceledException)
        {
            return; // The browser went away.
        }

        using (resp)
        {
            var res = ctx.Response;
            res.StatusCode = (int)resp.StatusCode;
            var mediaType = resp.Content.Headers.ContentType?.MediaType;
            var encoded = resp.Content.Headers.ContentEncoding.Count > 0;
            var rewrite = !encoded && !HttpMethods.IsHead(req.Method) && (mediaType == "text/html"
                || (path.EndsWith(".webmanifest", StringComparison.OrdinalIgnoreCase) && resp.IsSuccessStatusCode));

            CopyResponseHeaders(resp, res, rewrite);

            if (rewrite)
            {
                var text = await resp.Content.ReadAsStringAsync(ctx.RequestAborted);
                text = mediaType == "text/html"
                    ? Branding.RewriteHtml(text, PublicOrigin(ctx), skin.Version)
                    : Branding.RewriteManifest(text);
                var bytes = Encoding.UTF8.GetBytes(text);
                res.Headers.CacheControl = "no-cache";
                res.ContentLength = bytes.Length;
                await res.Body.WriteAsync(bytes, ctx.RequestAborted);
                return;
            }

            ctx.Features.Get<IHttpResponseBodyFeature>()?.DisableBuffering();
            if (HttpMethods.IsHead(req.Method) || res.StatusCode is 204 or 304) return;
            try
            {
                await using var body = await resp.Content.ReadAsStreamAsync(ctx.RequestAborted);
                await body.CopyToAsync(res.Body, ctx.RequestAborted);
            }
            catch (OperationCanceledException) { }
            catch (IOException) when (ctx.RequestAborted.IsCancellationRequested) { }
        }
    }

    private void CopyResponseHeaders(HttpResponseMessage resp, HttpResponse res, bool rewritten)
    {
        foreach (var (name, values) in resp.Headers.Concat(resp.Content.Headers))
        {
            if (HopByHop.Contains(name)) continue;
            if (rewritten && (name.Equals("Content-Length", StringComparison.OrdinalIgnoreCase)
                || name.Equals("ETag", StringComparison.OrdinalIgnoreCase)
                || name.Equals("Last-Modified", StringComparison.OrdinalIgnoreCase)
                || name.Equals("Cache-Control", StringComparison.OrdinalIgnoreCase))) continue;
            if (name.Equals("Location", StringComparison.OrdinalIgnoreCase))
            {
                res.Headers[name] = values.Select(RewriteLocation).ToArray();
                continue;
            }
            res.Headers[name] = values.ToArray();
        }
    }

    // A redirect to Vikunja's own address becomes a redirect to the same path here.
    private string RewriteLocation(string location) =>
        location.StartsWith(_upstreamOrigin, StringComparison.OrdinalIgnoreCase)
            ? (location.Length == _upstreamOrigin.Length ? "/" : location[_upstreamOrigin.Length..])
            : location;

    private async Task RelayWebSocketAsync(HttpContext ctx)
    {
        var req = ctx.Request;
        var target = new UriBuilder(_upstreamBase + req.Path.ToUriComponent() + req.QueryString.ToUriComponent())
        {
            Scheme = _upstream.Scheme == Uri.UriSchemeHttps ? "wss" : "ws",
        }.Uri;

        using var up = new ClientWebSocket();
        up.Options.KeepAliveInterval = TimeSpan.FromSeconds(30);
        up.Options.Proxy = null;
        up.Options.CollectHttpResponseDetails = true;
        foreach (var p in ctx.WebSockets.WebSocketRequestedProtocols) up.Options.AddSubProtocol(p);
        foreach (var name in new[] { "Cookie", "Authorization", "User-Agent", "Accept-Language" })
            if (req.Headers.TryGetValue(name, out var v)) up.Options.SetRequestHeader(name, v.ToString());
        up.Options.SetRequestHeader("Origin", _upstreamOrigin);
        up.Options.SetRequestHeader("X-Forwarded-For", ForwardedFor(ctx));
        up.Options.SetRequestHeader("X-Forwarded-Proto", ForwardedProto(ctx));
        up.Options.SetRequestHeader("X-Forwarded-Host", ForwardedHost(ctx));

        try
        {
            await up.ConnectAsync(target, ctx.RequestAborted);
        }
        catch (Exception ex) when (ex is WebSocketException or HttpRequestException or OperationCanceledException)
        {
            if (ctx.RequestAborted.IsCancellationRequested) return;
            // Pass Vikunja's own refusal through (401, 429 rate limit, ...) so the app backs off as it would directly.
            var status = (int)up.HttpStatusCode;
            if (status is >= 400 and < 600)
            {
                ctx.Response.StatusCode = status;
                return;
            }
            _log.LogWarning("WebSocket to {Target} failed: {Error}", target, ex.GetBaseException().Message);
            ctx.Response.StatusCode = StatusCodes.Status502BadGateway;
            return;
        }

        using var down = await ctx.WebSockets.AcceptWebSocketAsync(up.SubProtocol);
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ctx.RequestAborted);
        var a = PumpAsync(down, up, cts.Token);
        var b = PumpAsync(up, down, cts.Token);
        await Task.WhenAny(a, b);
        // One side closed: give the other a moment to finish its close handshake, then drop it.
        cts.CancelAfter(TimeSpan.FromSeconds(5));
        try { await Task.WhenAll(a, b); } catch (Exception) { /* Either side may already be gone. */ }
    }

    private static async Task PumpAsync(WebSocket from, WebSocket to, CancellationToken ct)
    {
        var buffer = new byte[16 * 1024];
        try
        {
            while (!ct.IsCancellationRequested)
            {
                var r = await from.ReceiveAsync(buffer, ct);
                if (r.MessageType == WebSocketMessageType.Close)
                {
                    if (to.State is WebSocketState.Open or WebSocketState.CloseReceived)
                        await to.CloseOutputAsync(from.CloseStatus ?? WebSocketCloseStatus.NormalClosure, from.CloseStatusDescription, ct);
                    return;
                }
                await to.SendAsync(buffer.AsMemory(0, r.Count), r.MessageType, r.EndOfMessage, ct);
            }
        }
        catch (Exception ex) when (ex is WebSocketException or OperationCanceledException or IOException)
        {
            if (to.State == WebSocketState.Open)
            {
                try { await to.CloseOutputAsync(WebSocketCloseStatus.EndpointUnavailable, null, CancellationToken.None); }
                catch (Exception) { /* Already closed. */ }
            }
        }
    }

    private static void AddForwardedHeaders(HttpContext ctx, System.Net.Http.Headers.HttpRequestHeaders headers)
    {
        headers.Remove("X-Forwarded-For");
        if (ForwardedFor(ctx) is { } chain) headers.TryAddWithoutValidation("X-Forwarded-For", chain);
        headers.Remove("X-Forwarded-Proto");
        headers.TryAddWithoutValidation("X-Forwarded-Proto", ForwardedProto(ctx));
        headers.Remove("X-Forwarded-Host");
        headers.TryAddWithoutValidation("X-Forwarded-Host", ForwardedHost(ctx));
    }

    private static string? ForwardedFor(HttpContext ctx)
    {
        var ip = ctx.Connection.RemoteIpAddress?.ToString();
        var prior = ctx.Request.Headers["X-Forwarded-For"].ToString();
        if (ip is null) return string.IsNullOrEmpty(prior) ? null : prior;
        return string.IsNullOrEmpty(prior) ? ip : $"{prior}, {ip}";
    }

    // Behind Caddy (or another proxy) the browser's scheme and host arrive in X-Forwarded-*.
    private static string ForwardedProto(HttpContext ctx) =>
        FirstValue(ctx.Request.Headers["X-Forwarded-Proto"]) ?? ctx.Request.Scheme;

    private static string ForwardedHost(HttpContext ctx) =>
        FirstValue(ctx.Request.Headers["X-Forwarded-Host"]) ?? ctx.Request.Host.Value ?? "localhost";

    private static string PublicOrigin(HttpContext ctx) => $"{ForwardedProto(ctx)}://{ForwardedHost(ctx)}";

    private static string? FirstValue(Microsoft.Extensions.Primitives.StringValues v)
    {
        var s = v.ToString();
        if (string.IsNullOrWhiteSpace(s)) return null;
        var comma = s.IndexOf(',', StringComparison.Ordinal);
        return (comma < 0 ? s : s[..comma]).Trim();
    }

    // The HTML shell of the app: "/", client-side routes (no file extension), *.html and the manifest.
    private static bool IsPageRequest(string path, string method)
    {
        if (!HttpMethods.IsGet(method) && !HttpMethods.IsHead(method)) return false;
        if (path.StartsWith("/api/", StringComparison.Ordinal) || path.StartsWith("/dav/", StringComparison.Ordinal)
            || path.StartsWith("/.well-known/", StringComparison.Ordinal)) return false;
        var ext = Path.GetExtension(path);
        return ext.Length == 0 || ext.Equals(".html", StringComparison.OrdinalIgnoreCase)
            || ext.Equals(".webmanifest", StringComparison.OrdinalIgnoreCase);
    }

    private static async Task WriteUnavailableAsync(HttpContext ctx, bool page)
    {
        if (ctx.Response.HasStarted) return;
        ctx.Response.StatusCode = StatusCodes.Status502BadGateway;
        if (!page)
        {
            ctx.Response.ContentType = "application/json";
            await ctx.Response.WriteAsync("{\"message\":\"Docket can't reach the task server.\"}");
            return;
        }
        ctx.Response.ContentType = "text/html; charset=utf-8";
        ctx.Response.Headers.CacheControl = "no-store";
        await ctx.Response.WriteAsync(Branding.UnavailablePage);
    }
}
