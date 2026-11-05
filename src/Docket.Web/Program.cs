// docket-web: serves Vikunja's web app with the Docket look. Every request goes to Vikunja unchanged, except that
// HTML pages get the Docket skin (CSS + a small branding script), Docket's title and icons, and the web manifest
// says "Docket". /docket-skin/* is served from files embedded in this program.
//   DOCKET_VIKUNJA_URL  Vikunja server to forward to          (default http://127.0.0.1:3456)
//   DOCKET_WEB_URLS     address(es) to listen on, ;-separated  (default http://127.0.0.1:3457)
//   DOCKET_WEB_SKIN_DIR optional folder to serve the skin from instead of the embedded copy (for skin development)
using Docket.Web;

var upstreamText = Environment.GetEnvironmentVariable("DOCKET_VIKUNJA_URL");
var upstream = new Uri(string.IsNullOrWhiteSpace(upstreamText) ? "http://127.0.0.1:3456" : upstreamText.Trim());
var urls = Environment.GetEnvironmentVariable("DOCKET_WEB_URLS");
if (string.IsNullOrWhiteSpace(urls)) urls = "http://127.0.0.1:3457";

if (args.Contains("--help") || args.Contains("-h"))
{
    Console.WriteLine("""
        docket-web: Vikunja's web app with the Docket look.
          DOCKET_VIKUNJA_URL   Vikunja server (default http://127.0.0.1:3456)
          DOCKET_WEB_URLS      listen address(es) (default http://127.0.0.1:3457)
        """);
    return;
}

var builder = WebApplication.CreateSlimBuilder(args);
builder.WebHost.UseUrls(urls.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
builder.WebHost.ConfigureKestrel(k =>
{
    k.AddServerHeader = false;
    k.Limits.MaxRequestBodySize = null; // Vikunja enforces its own upload limit.
});
builder.Services.AddSingleton(new SkinAssets(Environment.GetEnvironmentVariable("DOCKET_WEB_SKIN_DIR")));
builder.Services.AddSingleton(sp => new VikunjaProxy(upstream, sp.GetRequiredService<ILogger<VikunjaProxy>>()));
// Per-request logs would drown out the useful ones (startup, Vikunja unreachable).
builder.Logging.AddFilter("Microsoft.AspNetCore", LogLevel.Warning);

var app = builder.Build();
app.UseWebSockets(new WebSocketOptions { KeepAliveInterval = TimeSpan.FromSeconds(30) });

var skin = app.Services.GetRequiredService<SkinAssets>();
var proxy = app.Services.GetRequiredService<VikunjaProxy>();

app.Run(ctx =>
{
    var path = ctx.Request.Path.Value ?? "/";
    var isRead = HttpMethods.IsGet(ctx.Request.Method) || HttpMethods.IsHead(ctx.Request.Method);
    if (isRead && path.StartsWith("/docket-skin/", StringComparison.Ordinal))
        return skin.ServeAsync(path["/docket-skin/".Length..], ctx);
    if (isRead && path == "/docket-web/health")
        return ctx.Response.WriteAsync("ok");
    // Vikunja's own icons, wherever they are referenced from (bookmarks, the service worker, the manifest).
    if (isRead && path == "/favicon.ico")
        return skin.ServeAsync("favicon.ico", ctx);
    if (isRead && path.StartsWith("/images/icons/", StringComparison.Ordinal))
        return skin.ServeAsync(SkinAssets.IconFor(path), ctx);
    return proxy.HandleAsync(ctx, skin);
});

app.Logger.LogInformation("docket-web: {Urls} -> {Upstream}", urls, upstream);
await app.RunAsync();
