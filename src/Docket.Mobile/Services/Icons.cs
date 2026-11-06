using Docket.Core;

namespace Docket.Mobile.Services;

/// <summary>Fluent System Icons code points used by the pages written in C#.</summary>
public static class IconChars
{
    public const string Sun = "", Star = "", StarFilled = "", Calendar = "", All = "", Done = "", Home = "";
    public const string Add = "", Search = "", Bell = "", Repeat = "", Note = "", Attach = "", Tag = "";
    public const string Delete = "", Settings = "", Sort = "", More = "", List = "", ChevronRight = "", ChevronDown = "";
    public const string Dismiss = "", Bulb = "", Send = "", People = "", Team = "", Person = "", PersonAdd = "";
    public const string Share = "", Link = "", Board = "", Table = "", Gantt = "", Sparkle = "", History = "";
    public const string Tree = "", Copy = "", Palette = "", Chart = "", Grid = "", CalendarNext = "", CalendarDone = "";
    public const string Flag = "", Circle = "", Check = "", Info = "", Key = "", Lock = "", Globe = "";
    public const string Image = "", Emoji = "", Paint = "", Rename = "", Move = "", ArrowRight = "", Back = "";
    public const string Parent = "", Bot = "", Stop = "", Warning = "", Clock = "", Play = "", TaskList = "", Folder = "";
}

/// <summary>
/// List and column icons, as the Windows app stores them: a Fluent icon name from <see cref="Catalog"/>
/// ("Home"), or any other text, which is shown as an emoji.
/// </summary>
public static class Icons
{
    public static readonly (string Name, string Glyph)[] Catalog =
    [
        ("List", ""), ("Home", ""), ("Work", ""), ("Calendar", ""), ("Star", ""), ("Sun", ""),
        ("Flag", ""), ("Pin", ""), ("Tag", ""), ("Heart", ""), ("Idea", ""), ("Important", ""),
        ("Cart", ""), ("Shop", ""), ("Bank", ""), ("Money", ""), ("Mail", ""), ("Phone", ""),
        ("People", ""), ("Person", ""), ("Chat", ""), ("Globe", ""), ("Airplane", ""), ("Car", ""),
        ("Map", ""), ("Book", ""), ("Education", ""), ("Code", ""), ("Bug", ""), ("Settings", ""),
        ("Tools", ""), ("Lock", ""), ("Shield", ""), ("Cloud", ""), ("Folder", ""), ("Document", ""),
        ("Edit", ""), ("Camera", ""), ("Picture", ""), ("Video", ""), ("Music", ""), ("Game", ""),
        ("Health", ""), ("Clock", ""), ("Alarm", ""), ("Sync", ""), ("Play", ""), ("Pause", ""),
        ("Check", ""), ("Circle", ""), ("Robot", ""), ("Rocket", ""), ("Leaf", ""), ("Emoji", ""),
    ];

    public static readonly string[] Emoji =
    [
        "\U0001F3E0", "\U0001F4BC", "\U0001F6D2", "\U0001F381", "\U0001F4DA", "\U0001F393", "\U0001F4A1", "\U0001F680",
        "\U0001F3AF", "⭐", "❤️", "\U0001F525", "\U0001F4B0", "\U0001F4C5", "✈️", "\U0001F697",
        "\U0001F3CB️", "\U0001F9D8", "\U0001F37D️", "☕", "\U0001F3B5", "\U0001F3AE", "\U0001F3A8", "\U0001F4F7",
        "\U0001F331", "\U0001F436", "\U0001F476", "\U0001F527", "\U0001F4BB", "\U0001F41E", "\U0001F4DD", "\U0001F389",
    ];

    /// <summary>Names the other app's picker has, shown here with the nearest icon, so a list styled on one platform looks right on the other.</summary>
    public static readonly (string Name, string Glyph)[] Aliases =
    [
        ("Card", ""), ("Lightning", ""),
    ];

    public static string? GlyphOf(string? icon) =>
        icon is null ? null : Catalog.Concat(Aliases).FirstOrDefault(c => c.Name.Equals(icon, StringComparison.OrdinalIgnoreCase)).Glyph;

    public static bool IsEmoji(string? icon) => !string.IsNullOrWhiteSpace(icon) && GlyphOf(icon) is null;

    /// <summary>The text to draw for an icon: the glyph for a Fluent name, the emoji itself otherwise, or the fallback.</summary>
    public static string Display(string? icon, string fallback) => GlyphOf(icon) ?? (IsEmoji(icon) ? icon!.Trim() : fallback);

    public static string DefaultColumnGlyph(BoardColumn c) => c.IsDone ? IconChars.Done : c.IsDefault ? IconChars.Circle : IconChars.Play;

    public static readonly (string Name, string Hex)[] Colors =
    [
        ("Blue", "#0F6CBD"), ("Teal", "#038387"), ("Green", "#498205"), ("Lime", "#8CBD18"), ("Yellow", "#C19C00"), ("Orange", "#CA5010"),
        ("Red", "#D13438"), ("Pink", "#E3008C"), ("Purple", "#8764B8"), ("Indigo", "#4F6BED"), ("Brown", "#8E562E"), ("Grey", "#69797E"),
    ];

    public static readonly (string Name, string Value)[] Gradients =
    [
        ("Ocean", "#2564CF,#7A4FD6,135"), ("Sunset", "#F2683F,#C239B3,135"), ("Forest", "#0B6A0B,#1AAD8E,160"), ("Dusk", "#1B1A3A,#5B3E96,180"),
        ("Peach", "#FFB3A7,#FFD39A,135"), ("Mint", "#7FD1C6,#B5E8A0,135"), ("Steel", "#3B4A5A,#7D8E9E,160"), ("Berry", "#7A1F5C,#E3416E,135"),
    ];

    /// <summary>The brush behind a list for a colour, gradient or accent background. Null for none or image.</summary>
    public static Brush? BackgroundBrush(ListStyle style)
    {
        switch (style.Background)
        {
            case "color":
                return new SolidColorBrush(Th.Hex(style.BackgroundValue, Color.FromArgb("#2564CF")));
            case "gradient":
            {
                var parts = (style.BackgroundValue ?? "").Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
                var a = Th.Hex(parts.ElementAtOrDefault(0), Color.FromArgb("#2564CF"));
                var b = Th.Hex(parts.ElementAtOrDefault(1), Color.FromArgb("#7A4FD6"));
                var angle = double.TryParse(parts.ElementAtOrDefault(2), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var deg) ? deg : 135;
                var rad = (angle - 90) * Math.PI / 180;
                var dx = Math.Cos(rad) / 2; var dy = Math.Sin(rad) / 2;
                return new LinearGradientBrush([new GradientStop(a, 0), new GradientStop(b, 1)], new Point(0.5 - dx, 0.5 - dy), new Point(0.5 + dx, 0.5 + dy));
            }
            case "accent":
                return new LinearGradientBrush([new GradientStop(Color.FromArgb("#0F6CBD"), 0), new GradientStop(Color.FromArgb("#003E92"), 1)], new Point(0, 0), new Point(1, 1));
            default:
                return null;
        }
    }
}
