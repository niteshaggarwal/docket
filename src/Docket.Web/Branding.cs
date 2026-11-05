using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Docket.Web;

/// <summary>Rewrites Vikunja's HTML shell and web manifest to Docket's name, icons and skin.</summary>
public static partial class Branding
{
    public const string Name = "Docket";
    private const string Description = "Docket: your tasks, organised.";

    public static string RewriteHtml(string html, string publicOrigin, string skinVersion)
    {
        html = TitleRx().Replace(html, $"<title>{Name}</title>", 1);
        html = DescriptionRx().Replace(html, $"<meta name=\"description\" content=\"{Description}\">", 1);
        html = ThemeColorRx().Replace(html, "<meta name=\"theme-color\" content=\"#202020\" media=\"(prefers-color-scheme: dark)\"/>"
            + "<meta name=\"theme-color\" content=\"#f3f3f3\" media=\"(prefers-color-scheme: light)\"/>", 1);
        html = IconLinkRx().Replace(html, "<link rel=\"icon\" type=\"image/png\" sizes=\"32x32\" href=\"/docket-skin/docket-32.png\">"
            + "<link rel=\"icon\" type=\"image/x-icon\" href=\"/docket-skin/favicon.ico\">", 1);
        html = AppleIconRx().Replace(html, "<link rel=\"apple-touch-icon\" href=\"/docket-skin/docket-180.png\"/>", 1);
        html = html.Replace("<html lang=", "<html class=\"docket\" lang=", StringComparison.Ordinal);
        // The app talks to its API through this server, wherever the browser reached it.
        if (SafeOriginRx().IsMatch(publicOrigin))
            html = ApiUrlRx().Replace(html, $"window.API_URL = '{publicOrigin}'", 1);
        html = LogoRx().Replace(html, "window.CUSTOM_LOGO_URL = '/docket-skin/logo.svg'", 1);
        html = LogoDarkRx().Replace(html, "window.CUSTOM_LOGO_URL_DARK = '/docket-skin/logo-dark.svg'", 1);
        html = html.Replace("Vikunja doesn't work", "Docket doesn't work", StringComparison.Ordinal);

        var inject = $"<link rel=\"stylesheet\" href=\"/docket-skin/skin.css?v={skinVersion}\">"
            + $"<script src=\"/docket-skin/skin.js?v={skinVersion}\"></script>";
        var head = html.IndexOf("</head>", StringComparison.OrdinalIgnoreCase);
        return head < 0 ? inject + html : html.Insert(head, inject);
    }

    public static string RewriteManifest(string json)
    {
        try
        {
            if (JsonNode.Parse(json) is not JsonObject m) return json;
            m["name"] = Name;
            m["short_name"] = Name;
            m["description"] = Description;
            m["background_color"] = "#202020";
            m["theme_color"] = "#202020";
            m["icons"] = new JsonArray(
                Icon("/docket-skin/docket-192.png", "192x192", "any"),
                Icon("/docket-skin/docket-512.png", "512x512", "any"),
                Icon("/docket-skin/docket-512.png", "512x512", "maskable"));
            return m.ToJsonString();
        }
        catch (System.Text.Json.JsonException)
        {
            return json;
        }

        static JsonObject Icon(string src, string sizes, string purpose) =>
            new() { ["src"] = src, ["sizes"] = sizes, ["type"] = "image/png", ["purpose"] = purpose };
    }

    public const string UnavailablePage = """
        <!DOCTYPE html><html lang="en"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">
        <title>Docket</title><link rel="icon" href="/docket-skin/docket-32.png">
        <style>:root{color-scheme:light dark}body{margin:0;min-height:100vh;display:grid;place-items:center;background:#f3f3f3;color:#1a1a1a;
        font:14px/1.5 "Segoe UI Variable Text","Segoe UI",system-ui,sans-serif}@media(prefers-color-scheme:dark){body{background:#202020;color:#fff}}
        main{text-align:center;max-width:28rem;padding:2rem}img{width:64px;height:64px}h1{font-size:20px;font-weight:600;margin:16px 0 4px}p{opacity:.7;margin:0}</style>
        </head><body><main><img src="/docket-skin/docket-64.png" alt=""><h1>Docket can't reach its task server</h1>
        <p>The server may be starting or stopped. Try again in a moment.</p></main></body></html>
        """;

    [GeneratedRegex(@"<title>[^<]*</title>", RegexOptions.IgnoreCase)]
    private static partial Regex TitleRx();
    [GeneratedRegex(@"<meta\s+name=""description""[^>]*>", RegexOptions.IgnoreCase)]
    private static partial Regex DescriptionRx();
    [GeneratedRegex(@"<meta\s+name=""theme-color""[^>]*>", RegexOptions.IgnoreCase)]
    private static partial Regex ThemeColorRx();
    [GeneratedRegex(@"<link\s+rel=""icon""[^>]*>", RegexOptions.IgnoreCase)]
    private static partial Regex IconLinkRx();
    [GeneratedRegex(@"<link\s+rel=""apple-touch-icon""[^>]*>", RegexOptions.IgnoreCase)]
    private static partial Regex AppleIconRx();
    [GeneratedRegex(@"^https?://[A-Za-z0-9.\-:\[\]]+$")]
    private static partial Regex SafeOriginRx();
    [GeneratedRegex(@"window\.API_URL\s*=\s*'[^']*'")]
    private static partial Regex ApiUrlRx();
    [GeneratedRegex(@"window\.CUSTOM_LOGO_URL\s*=\s*'[^']*'")]
    private static partial Regex LogoRx();
    [GeneratedRegex(@"window\.CUSTOM_LOGO_URL_DARK\s*=\s*'[^']*'")]
    private static partial Regex LogoDarkRx();
}
