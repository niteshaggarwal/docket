using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Docket.Core;

/// <summary>
/// Shared settings for the Docket app, the docket CLI and the Claude connector, kept in
/// %LOCALAPPDATA%\Docket\settings.json. Tokens are encrypted with Windows DPAPI, so only
/// this Windows user on this PC can read them.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class AppSettings : IDocketState
{
    /// <summary>The day My Day was last reset (shared by the app, CLI and connector).</summary>
    public DateOnly? MyDayDate { get; set; }

    /// <summary>Sort per list key ("smart:MyDay", "list:12").</summary>
    public Dictionary<string, SortMode> Sorts { get; set; } = [];

    /// <summary>List keys whose completed section is hidden.</summary>
    public HashSet<string> HideCompleted { get; set; } = [];

    /// <summary>"system", "light" or "dark".</summary>
    public string Theme { get; set; } = "system";

    /// <summary>Smart lists shown in the sidebar (My Day and Tasks are always shown).</summary>
    public bool ShowImportant { get; set; } = true;
    public bool ShowPlanned { get; set; } = true;
    public bool ShowAll { get; set; } = true;
    public bool ShowCompletedList { get; set; } = true;
    public bool HideEmptySmartLists { get; set; }

    /// <summary>Reminder toasts: ask before deleting, play sound.</summary>
    public bool ConfirmDelete { get; set; } = true;
    public bool ReminderSound { get; set; } = true;

    /// <summary>Notifier bookmark: reminders up to this time have been shown.</summary>
    public DateTimeOffset? NotifiedUntil { get; set; }

    public string ServerUrl { get; set; } = "http://localhost:3456";
    public long DefaultProjectId { get; set; }

    /// <summary>Token used by the Docket app and the CLI.</summary>
    public string? ProtectedToken { get; set; }

    /// <summary>Separate token for Claude (no delete permissions), so it can be revoked on its own.</summary>
    public string? ClaudeProtectedToken { get; set; }

    /// <summary>Settings file: DOCKET_SETTINGS (an alternate path, used by tests) or %LOCALAPPDATA%\Docket\settings.json.</summary>
    public static string FilePath { get; } = Environment.GetEnvironmentVariable("DOCKET_SETTINGS") is { Length: > 0 } alt
        ? Path.GetFullPath(alt)
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Docket", "settings.json");
    public static string Dir { get; } = Path.GetDirectoryName(FilePath)!;

    public string? Token { get => Unprotect(ProtectedToken); set => ProtectedToken = Protect(value); }
    public string? ClaudeToken { get => Unprotect(ClaudeProtectedToken); set => ClaudeProtectedToken = Protect(value); }

    public bool IsConfigured => Uri.TryCreate(ServerUrl, UriKind.Absolute, out _) && !string.IsNullOrEmpty(Token);

    // ---------- AI in the add bar ----------
    /// <summary>"ollama", "anthropic", "openai", "gemini" or "off".</summary>
    public string AiProvider { get; set; } = "ollama";
    public Dictionary<string, string> AiModels { get; set; } = [];
    public string OllamaUrl { get; set; } = "http://localhost:11434";

    /// <summary>API keys by provider, each encrypted for this Windows account.</summary>
    public Dictionary<string, string> ProtectedAiKeys { get; set; } = [];

    public string? GetAiKey(string provider) => Unprotect(ProtectedAiKeys.GetValueOrDefault(provider));

    public void SetAiKey(string provider, string? key)
    {
        if (string.IsNullOrWhiteSpace(key)) ProtectedAiKeys.Remove(provider);
        else ProtectedAiKeys[provider] = Protect(key.Trim())!;
    }

    [System.Text.Json.Serialization.JsonIgnore]
    public AiConfig Ai => new(AiProvider,
        AiModels.GetValueOrDefault(AiProvider) is { Length: > 0 } m ? m : AiConfig.DefaultModel(AiProvider),
        GetAiKey(AiProvider), OllamaUrl);

    /// <summary>The view chosen per list (List, Table, Gantt, Board), kept across restarts.</summary>
    public Dictionary<string, string> ListViews { get; set; } = [];

    /// <summary>Path of the Vikunja program when the server runs on this PC (used to create accounts for people).</summary>
    public string? VikunjaExe { get; set; }

    public static AppSettings Load()
    {
        try { return File.Exists(FilePath) ? JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath)) ?? new() : new(); }
        catch (JsonException) { return new(); }
        catch (IOException) { return new(); }
    }

    public void Save()
    {
        Directory.CreateDirectory(Dir);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
    }

    public VikunjaClient? CreateClient() => IsConfigured ? new VikunjaClient(new Uri(ServerUrl), Token!) : null;

    /// <summary>Reloads just the My Day date from disk before a reset check, so two Docket tools don't both reset.</summary>
    public void RefreshMyDayDate() { try { MyDayDate = Load().MyDayDate; } catch (IOException) { } }

    public void SaveMyDayDate(DateOnly day)
    {
        MyDayDate = day;
        var fresh = File.Exists(FilePath) ? Load() : this;
        fresh.MyDayDate = day;
        fresh.Save();
    }

    /// <summary>The Claude token when set, else the app token.</summary>
    public VikunjaClient? CreateClaudeClient()
    {
        var token = ClaudeToken ?? Token;
        return Uri.TryCreate(ServerUrl, UriKind.Absolute, out var uri) && !string.IsNullOrEmpty(token) ? new VikunjaClient(uri, token) : null;
    }

    private static string? Protect(string? value) => string.IsNullOrEmpty(value) ? null
        : Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(value), null, DataProtectionScope.CurrentUser));

    private static string? Unprotect(string? value)
    {
        if (string.IsNullOrEmpty(value)) return null;
        try { return Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(value), null, DataProtectionScope.CurrentUser)); }
        catch (CryptographicException) { return null; }
        catch (FormatException) { return null; }
    }
}
