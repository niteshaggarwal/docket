using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Docket.Core;

/// <summary>How a board column looks: a colour and an icon (a Fluent glyph name or an emoji).</summary>
public sealed record ColumnStyle(string? Color = null, string? Icon = null);

/// <summary>
/// A list's look: icon, colour and background, plus each board column's colour and icon.
/// Vikunja has no fields for these, so they ride along in the list's description as an HTML comment,
/// which Vikunja keeps but never shows. Every Docket app (and anyone the list is shared with) sees the same look.
/// </summary>
public sealed partial record ListStyle
{
    /// <summary>An emoji, or a Fluent icon name such as "Home".</summary>
    public string? Icon { get; init; }

    /// <summary>"none", "color", "gradient", "accent" or "image".</summary>
    public string Background { get; init; } = "none";

    /// <summary>For color: "#RRGGBB". For gradient: "#RRGGBB,#RRGGBB[,angle]". For accent: an accent name. For image: "uploaded".</summary>
    public string? BackgroundValue { get; init; }

    public Dictionary<string, ColumnStyle> Columns { get; init; } = [];

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public ColumnStyle ColumnFor(long bucketId) => Columns.GetValueOrDefault(bucketId.ToString()) ?? new();

    public ListStyle WithColumn(long bucketId, ColumnStyle style)
    {
        var cols = new Dictionary<string, ColumnStyle>(Columns) { [bucketId.ToString()] = style };
        return this with { Columns = cols };
    }

    public bool IsEmpty => Icon is null && Background == "none" && Columns.Count == 0;

    public static ListStyle Parse(string? description)
    {
        if (string.IsNullOrEmpty(description)) return new();
        var m = MetaRx().Match(description);
        if (!m.Success) return new();
        try { return JsonSerializer.Deserialize<ListStyle>(m.Groups["json"].Value, Json) ?? new(); }
        catch (JsonException) { return new(); }
    }

    /// <summary>The description with this style stored in it, replacing any older one. The visible text is untouched.</summary>
    public string ApplyTo(string? description)
    {
        var rest = MetaRx().Replace(description ?? "", "");
        if (IsEmpty) return rest;
        var json = JsonSerializer.Serialize(this, Json).Replace("--", "\\u002D\\u002D");
        return rest + $"<!--docket:{json}-->";
    }

    /// <summary>The description without the stored style, for showing as notes.</summary>
    public static string VisibleText(string? description) => MetaRx().Replace(description ?? "", "");

    [GeneratedRegex(@"<!--docket:(?<json>\{.*?\})-->", RegexOptions.Singleline)]
    private static partial Regex MetaRx();
}
