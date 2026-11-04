using Docket.Core;
using Docket.Mcp;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

// Docket connector: an MCP server that lets AI assistants work on your Vikunja tasks. No tool deletes anything,
// and the token it is given should have no delete rights either (the setup scripts make one like that).
//
//   docket-mcp              stdio, for Claude Desktop, Claude Code, LM Studio, Ollama clients and other local apps
//   docket-mcp --http       Streamable HTTP on DOCKET_MCP_URLS (default http://127.0.0.1:8080), for remote AIs:
//                           /mcp (MCP), /api/tools/{name} + /openapi.json (REST for GPT actions), /oauth/* (sign-in)
//   docket-mcp --version | --help
//
// Settings come from environment variables or the Windows Docket settings file; see ConnectorConfig.cs and docs/ai.

if (args.Contains("--version")) { Console.WriteLine(typeof(ConnectorOptions).Assembly.GetName().Version?.ToString(3)); return 0; }
if (args.Contains("--help") || args.Contains("-h"))
{
    Console.WriteLine("""
        docket-mcp            MCP over stdio (local AI apps)
        docket-mcp --http     MCP over HTTP for remote AIs, plus REST + OpenAPI
        docket-mcp setup      sign in to Vikunja once and save a no-delete token for the connector

        Environment
          DOCKET_VIKUNJA_URL     Vikunja address, e.g. http://vikunja:3456  (or use the Windows settings file)
          DOCKET_VIKUNJA_TOKEN   Vikunja API token, ideally without delete rights
                                 (or DOCKET_VIKUNJA_TOKEN_FILE, default DOCKET_DATA_DIR/vikunja-token)
          DOCKET_DEFAULT_LIST    list id for new tasks (optional)
          DOCKET_MCP_KEY         HTTP only, required: connector key (24+ characters)
          DOCKET_PUBLIC_URL      HTTP only: public https address, e.g. https://tasks.example.com
          DOCKET_MCP_URLS        HTTP only: listen address, default http://127.0.0.1:8080 (Docker: 0.0.0.0:8080)
          DOCKET_DATA_DIR        where state.json and oauth.json are kept, default ./data
        """);
    return 0;
}

if (args.FirstOrDefault() == "setup") return await Setup.RunAsync();

ConnectorOptions opts;
try { opts = ConnectorOptions.Load(); }
catch (InvalidOperationException ex) when (args.Contains("--http"))
{
    // Not set up yet (e.g. a fresh Docker install): say so over HTTP and wait for "docket-mcp setup" to write the token,
    // then exit so the container restarts with it.
    Console.Error.WriteLine(ex.Message);
    var waiting = WebApplication.CreateBuilder(args);
    waiting.WebHost.UseUrls(ConnectorOptions.Env("DOCKET_MCP_URLS") ?? "http://127.0.0.1:8080");
    waiting.Logging.SetMinimumLevel(LogLevel.Warning);
    var w = waiting.Build();
    w.MapFallback(() => Results.Json(new { status = "needs setup", error = ex.Message }, statusCode: 503));
    _ = Task.Run(async () =>
    {
        while (true)
        {
            await Task.Delay(TimeSpan.FromSeconds(3));
            try { ConnectorOptions.Load(); Console.Error.WriteLine("Token found; restarting."); await w.StopAsync(); return; }
            catch (InvalidOperationException) { }
        }
    });
    await w.RunAsync();
    return 3;
}
catch (InvalidOperationException ex) { Console.Error.WriteLine(ex.Message); return 2; }

DocketService MakeService(IServiceProvider _) => new(new VikunjaClient(opts.VikunjaUrl, opts.VikunjaToken)) { State = opts.State };
var info = new ModelContextProtocol.Protocol.Implementation
{
    Name = "docket", Title = "Docket tasks", Version = typeof(ConnectorOptions).Assembly.GetName().Version?.ToString(3) ?? "0.0.0",
};

if (!args.Contains("--http"))
{
    var builder = Host.CreateApplicationBuilder(args);
    builder.Logging.ClearProviders();
    builder.Logging.AddConsole(o => o.LogToStandardErrorThreshold = LogLevel.Trace); // stdout is the MCP channel
    builder.Logging.SetMinimumLevel(LogLevel.Warning);
    builder.Logging.AddFilter("ModelContextProtocol", LogLevel.Critical); // tool errors already reach the AI as messages
    builder.Services.AddSingleton(opts);
    builder.Services.AddSingleton(MakeService);
    builder.Services.AddMcpServer(o => o.ServerInfo = info).WithStdioServerTransport().WithToolsFromAssembly();
    await builder.Build().RunAsync();
    return 0;
}

var key = ConnectorOptions.Env("DOCKET_MCP_KEY");
if (key is null) { Console.Error.WriteLine("Set DOCKET_MCP_KEY (24+ characters, e.g. from: openssl rand -hex 32) before using --http."); return 2; }
RemoteAuth auth;
try { auth = new RemoteAuth(key, ConnectorOptions.DataDir); }
catch (InvalidOperationException ex) { Console.Error.WriteLine(ex.Message); return 2; }

var web = WebApplication.CreateBuilder(args);
web.WebHost.UseUrls(ConnectorOptions.Env("DOCKET_MCP_URLS") ?? "http://127.0.0.1:8080");
web.Logging.SetMinimumLevel(LogLevel.Information);
web.Logging.AddFilter("Microsoft.AspNetCore", LogLevel.Warning);
web.Logging.AddFilter("ModelContextProtocol", LogLevel.Warning);
web.Services.Configure<ForwardedHeadersOptions>(o =>
{
    // Runs behind Caddy or another reverse proxy that sets X-Forwarded-*. Trust it only from this machine or a
    // private network (the Docker network, a LAN proxy), never from the internet.
    o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedHost;
    o.KnownIPNetworks.Clear();
    o.KnownProxies.Clear();
    foreach (var net in new[] { "127.0.0.0/8", "10.0.0.0/8", "172.16.0.0/12", "192.168.0.0/16", "::1/128", "fc00::/7" })
        o.KnownIPNetworks.Add(System.Net.IPNetwork.Parse(net));
});
web.Services.AddSingleton(opts);
web.Services.AddSingleton(MakeService);
web.Services.AddMcpServer(o => o.ServerInfo = info)
    .WithHttpTransport(o => o.Stateless = true)
    .WithToolsFromAssembly();

var app = web.Build();
app.UseForwardedHeaders();

// Everything under /mcp needs the key or an OAuth token.
app.Use(async (ctx, next) =>
{
    if (ctx.Request.Path.StartsWithSegments("/mcp") && !auth.IsAuthorized(ctx)) { await RemoteAuth.Challenge(ctx); return; }
    await next();
});

app.MapGet("/", () => Results.Text($"Docket connector {info.Version} is running. MCP: /mcp  REST: /openapi.json  Docs: https://github.com/quartifex/docket/tree/main/docs/ai\n"));
app.MapGet("/health", async (DocketService svc, CancellationToken ct) =>
{
    try { await svc.Client.GetProjectsAsync(ct); return Results.Json(new { status = "ok", version = info.Version }); }
    catch (Exception ex) when (ex is HttpRequestException or VikunjaException or TaskCanceledException)
    { return Results.Json(new { status = "vikunja unreachable" }, statusCode: 503); }
});
auth.Map(app);
ActionsApi.Map(app, auth.IsAuthorized);
app.MapMcp("/mcp");

if (ConnectorOptions.Env("DOCKET_PUBLIC_URL") is null)
    app.Logger.LogWarning("DOCKET_PUBLIC_URL isn't set; OAuth addresses will follow each request's host. Set it to your public https address.");
app.Logger.LogInformation("Docket connector {Version}: Vikunja {Vikunja} ({Source}), {Count} tools", info.Version, opts.VikunjaUrl, opts.Source, ActionsApi.ToolNames.Count);
await app.RunAsync();
return 0;
