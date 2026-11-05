using Docket.Core;

namespace Docket.Mobile.Services;

/// <summary>
/// The phone's AI choice. Provider and model live in preferences; API keys in SecureStorage (Android keystore).
/// "Local" means Ollama on the user's PC, reached over the network (default: the Docket server's host, port 11434).
/// </summary>
public static class AiSettings
{
    public static readonly (string Id, string Name, string Blurb)[] Providers =
    [
        ("ollama", "Local AI on your PC", "Ollama on your PC, over your network. Nothing leaves your devices."),
        ("anthropic", "Claude", "Anthropic's Claude, with your API key."),
        ("openai", "ChatGPT", "OpenAI's models, with your API key."),
        ("gemini", "Gemini", "Google's Gemini, with your API key."),
        ("off", "Off", "No AI. The add bar adds tasks as typed."),
    ];

    public static string Provider { get => Preferences.Default.Get("ai.provider", "off"); set => Preferences.Default.Set("ai.provider", value); }

    public static string ModelFor(string provider) => Preferences.Default.Get("ai.model." + provider, AiConfig.DefaultModel(provider));
    public static void SetModel(string provider, string model) => Preferences.Default.Set("ai.model." + provider, model.Trim());

    /// <summary>The PC's Ollama address. Defaults to the Docket server's host on port 11434.</summary>
    public static string OllamaUrl(string? serverUrl) => Preferences.Default.Get("ai.ollama", DefaultOllamaUrl(serverUrl));
    public static void SetOllamaUrl(string url) => Preferences.Default.Set("ai.ollama", url.Trim());

    public static string DefaultOllamaUrl(string? serverUrl) =>
        Uri.TryCreate(serverUrl, UriKind.Absolute, out var u) ? $"http://{u.Host}:11434" : "http://192.168.1.10:11434";

    public static Task<string?> KeyAsync(string provider) => SecureStorage.Default.GetAsync("ai.key." + provider);

    public static async Task SetKeyAsync(string provider, string? key)
    {
        if (string.IsNullOrWhiteSpace(key)) SecureStorage.Default.Remove("ai.key." + provider);
        else await SecureStorage.Default.SetAsync("ai.key." + provider, key.Trim());
    }

    public static async Task<AiConfig> ConfigAsync(string? serverUrl)
    {
        var p = Provider;
        var key = p is "anthropic" or "openai" or "gemini" ? await KeyAsync(p) : null;
        return new AiConfig(p, ModelFor(p), key, OllamaUrl(serverUrl));
    }

    public static bool IsOn => Provider != "off";

    public static string NameOf(string provider) => Providers.FirstOrDefault(x => x.Id == provider).Name ?? "Off";
}
