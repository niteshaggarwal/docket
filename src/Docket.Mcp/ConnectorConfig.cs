using System.Text.Json;
using Docket.Core;

namespace Docket.Mcp;

/// <summary>
/// Where the connector gets its Vikunja address and token. Two sources, in this order:
/// 1. Environment variables (servers, Docker, Linux, macOS): DOCKET_VIKUNJA_URL and DOCKET_VIKUNJA_TOKEN.
/// 2. The Docket settings file on Windows (%LOCALAPPDATA%\Docket\settings.json, or DOCKET_SETTINGS),
///    written by the Docket app or the setup script. Uses the separate AI token when there is one.
/// </summary>
public sealed class ConnectorOptions
{
    public required Uri VikunjaUrl { get; init; }
    public required string VikunjaToken { get; init; }
    public long DefaultProjectId { get; init; }
    public required IDocketState State { get; init; }
    public required string Source { get; init; }

    public static ConnectorOptions Load()
    {
        var url = Env("DOCKET_VIKUNJA_URL");
        var token = Env("DOCKET_VIKUNJA_TOKEN") ?? ReadTokenFile();
        if (url is not null || token is not null)
        {
            if (url is null) throw new InvalidOperationException("Set DOCKET_VIKUNJA_URL (your Vikunja address).");
            if (token is null)
                throw new InvalidOperationException($"No Vikunja token yet. Run \"docket-mcp setup\" (Docker: docker compose exec connector docket-mcp setup), or set DOCKET_VIKUNJA_TOKEN.");
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
                throw new InvalidOperationException($"DOCKET_VIKUNJA_URL isn't a valid address: {url}");
            _ = long.TryParse(Env("DOCKET_DEFAULT_LIST"), out var list);
            return new ConnectorOptions
            {
                VikunjaUrl = uri, VikunjaToken = token, DefaultProjectId = list,
                State = new FileState(Path.Combine(DataDir, "state.json")), Source = "environment",
            };
        }

        if (OperatingSystem.IsWindows())
        {
            var s = AppSettings.Load();
            var t = s.ClaudeToken ?? s.Token;
            if (!string.IsNullOrEmpty(t) && Uri.TryCreate(s.ServerUrl, UriKind.Absolute, out var u))
                return new ConnectorOptions { VikunjaUrl = u, VikunjaToken = t, DefaultProjectId = s.DefaultProjectId, State = s, Source = AppSettings.FilePath };
        }

        throw new InvalidOperationException(
            "Docket isn't set up yet. Either sign in with the Docket app or run the setup script (Windows), " +
            "or set DOCKET_VIKUNJA_URL and DOCKET_VIKUNJA_TOKEN. See docs/ai/README.md.");
    }

    /// <summary>DOCKET_DATA_DIR, else ./data next to the working directory. Holds state.json and oauth.json.</summary>
    public static string DataDir => Env("DOCKET_DATA_DIR") ?? Path.Combine(Environment.CurrentDirectory, "data");

    /// <summary>DOCKET_VIKUNJA_TOKEN_FILE, else DOCKET_DATA_DIR/vikunja-token (written by "docket-mcp setup").</summary>
    public static string TokenFile => Env("DOCKET_VIKUNJA_TOKEN_FILE") ?? Path.Combine(DataDir, "vikunja-token");

    private static string? ReadTokenFile()
    {
        try { return File.Exists(TokenFile) && File.ReadAllText(TokenFile).Trim() is { Length: > 0 } t ? t : null; }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
    }

    internal static string? Env(string name) => Environment.GetEnvironmentVariable(name) is { Length: > 0 } v ? v.Trim() : null;
}

/// <summary>My Day's reset date in a small JSON file, for servers that have no Docket settings file.</summary>
public sealed class FileState(string path) : IDocketState
{
    private sealed record Data(DateOnly? MyDayDate);

    public DateOnly? MyDayDate { get; set; } = Read(path);

    public void RefreshMyDayDate() => MyDayDate = Read(path);

    public void SaveMyDayDate(DateOnly day)
    {
        MyDayDate = day;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(new Data(day)));
    }

    private static DateOnly? Read(string path)
    {
        try { return File.Exists(path) ? JsonSerializer.Deserialize<Data>(File.ReadAllText(path))?.MyDayDate : null; }
        catch (Exception e) when (e is IOException or JsonException) { return null; }
    }
}
