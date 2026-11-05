using System.Collections.Concurrent;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;

namespace Docket.Web;

/// <summary>The Docket skin files (CSS, JS, icons, logo), embedded in this program or read from a folder.</summary>
public sealed class SkinAssets
{
    private sealed record Asset(byte[] Bytes, string ContentType, string ETag);

    private readonly string? _devDir;
    private readonly ConcurrentDictionary<string, Asset> _embedded = new(StringComparer.Ordinal);

    public SkinAssets(string? devDir)
    {
        _devDir = string.IsNullOrWhiteSpace(devDir) ? null : Path.GetFullPath(devDir);
        var asm = typeof(SkinAssets).Assembly;
        foreach (var res in asm.GetManifestResourceNames().Where(n => n.StartsWith("skin/", StringComparison.Ordinal)))
        {
            using var s = asm.GetManifestResourceStream(res)!;
            using var ms = new MemoryStream();
            s.CopyTo(ms);
            Add(res["skin/".Length..], ms.ToArray());
        }
        // The wordmark used where Vikunja shows its logo: the Docket icon and "Docket", one per theme.
        if (_embedded.TryGetValue("docket-64.png", out var icon))
        {
            Add("logo.svg", Encoding.UTF8.GetBytes(Wordmark(icon.Bytes, "#1a1a1a")));
            Add("logo-dark.svg", Encoding.UTF8.GetBytes(Wordmark(icon.Bytes, "#ffffff")));
        }
    }

    /// <summary>Changes whenever skin.css or skin.js change, so pages can link them with a cache-busting query.</summary>
    public string Version
    {
        get
        {
            if (_devDir is not null) return DateTime.UtcNow.Ticks.ToString(System.Globalization.CultureInfo.InvariantCulture);
            return _version ??= Hash([.. Get("skin.css")?.Bytes ?? [], .. Get("skin.js")?.Bytes ?? []])[..12];
        }
    }
    private string? _version;

    /// <summary>Maps one of Vikunja's /images/icons/* files to the Docket icon of about the same size.</summary>
    public static string IconFor(string path)
    {
        var name = Path.GetFileName(path).ToLowerInvariant();
        if (name.EndsWith(".svg", StringComparison.Ordinal)) return "logo.svg";
        if (name.Contains("512", StringComparison.Ordinal) || name.Contains("maskable", StringComparison.Ordinal)) return "docket-512.png";
        if (name.Contains("192", StringComparison.Ordinal)) return "docket-192.png";
        if (name.Contains("apple", StringComparison.Ordinal) || name.Contains("180", StringComparison.Ordinal)) return "docket-180.png";
        if (name.Contains("16", StringComparison.Ordinal) || name.Contains("32", StringComparison.Ordinal)) return "docket-32.png";
        return "docket-256.png";
    }

    public async Task ServeAsync(string name, HttpContext ctx)
    {
        var asset = Get(name);
        if (asset is null)
        {
            ctx.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }
        var res = ctx.Response;
        res.ContentType = asset.ContentType;
        res.Headers.ETag = asset.ETag;
        // Versioned links (?v=...) never change; anything else is revalidated so a new release shows at once.
        res.Headers.CacheControl = ctx.Request.Query.ContainsKey("v") && _devDir is null
            ? "public, max-age=31536000, immutable"
            : "no-cache";
        if (ctx.Request.Headers.IfNoneMatch == asset.ETag)
        {
            res.StatusCode = StatusCodes.Status304NotModified;
            return;
        }
        res.ContentLength = asset.Bytes.Length;
        if (!HttpMethods.IsHead(ctx.Request.Method))
            await res.Body.WriteAsync(asset.Bytes, ctx.RequestAborted);
    }

    private Asset? Get(string name)
    {
        if (name.Contains('/') || name.Contains('\\') || name.Contains("..", StringComparison.Ordinal)) return null;
        if (_devDir is not null)
        {
            var file = Path.Combine(_devDir, name);
            if (File.Exists(file)) return Make(name, File.ReadAllBytes(file));
        }
        return _embedded.GetValueOrDefault(name);
    }

    private void Add(string name, byte[] bytes) => _embedded[name] = Make(name, bytes);

    private static Asset Make(string name, byte[] bytes) => new(bytes, ContentType(name), $"\"{Hash(bytes)[..16]}\"");

    private static string Hash(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));

    private static string ContentType(string name) => Path.GetExtension(name).ToLowerInvariant() switch
    {
        ".css" => "text/css; charset=utf-8",
        ".js" => "text/javascript; charset=utf-8",
        ".svg" => "image/svg+xml",
        ".png" => "image/png",
        ".ico" => "image/x-icon",
        ".json" => "application/json",
        _ => "application/octet-stream",
    };

    private static string Wordmark(byte[] png, string textColor) => $"""
        <svg xmlns="http://www.w3.org/2000/svg" xmlns:xlink="http://www.w3.org/1999/xlink" width="132" height="32" viewBox="0 0 132 32">
          <image width="32" height="32" href="data:image/png;base64,{Convert.ToBase64String(png)}" xlink:href="data:image/png;base64,{Convert.ToBase64String(png)}"/>
          <text x="41" y="23" fill="{textColor}" font-family="'Segoe UI Variable Display','Segoe UI Variable Text','Segoe UI',system-ui,-apple-system,sans-serif" font-size="20" font-weight="600" letter-spacing="-0.2">Docket</text>
        </svg>
        """;
}
