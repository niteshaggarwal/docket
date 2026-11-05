using Docket.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.UI;

namespace Docket.App.Services;

/// <summary>
/// List and column icons. A stored icon is either a Fluent icon name from <see cref="Catalog"/> ("Home")
/// or any other text, which is shown as an emoji.
/// </summary>
public static class Icons
{
    public const string ListGlyph = "";

    public static readonly (string Name, string Glyph)[] Catalog =
    [
        ("List", ""), ("Home", ""), ("Work", ""), ("Calendar", ""), ("Star", ""), ("Sun", ""),
        ("Flag", ""), ("Pin", ""), ("Tag", ""), ("Heart", ""), ("Idea", ""), ("Important", ""),
        ("Cart", ""), ("Shop", ""), ("Bank", ""), ("Card", ""), ("Mail", ""), ("Phone", ""),
        ("People", ""), ("Person", ""), ("Chat", ""), ("Globe", ""), ("Airplane", ""), ("Car", ""),
        ("Map", ""), ("Book", ""), ("Education", ""), ("Code", ""), ("Bug", ""), ("Settings", ""),
        ("Tools", ""), ("Lock", ""), ("Shield", ""), ("Cloud", ""), ("Folder", ""), ("Document", ""),
        ("Edit", ""), ("Camera", ""), ("Picture", ""), ("Video", ""), ("Music", ""), ("Game", ""),
        ("Health", ""), ("Clock", ""), ("Alarm", ""), ("Sync", ""), ("Play", ""), ("Pause", ""),
        ("Check", ""), ("Circle", ""), ("Robot", ""), ("Lightning", ""), ("Leaf", ""), ("Emoji", ""),
    ];

    public static string? GlyphOf(string? icon) =>
        icon is null ? null : Catalog.FirstOrDefault(c => c.Name.Equals(icon, StringComparison.OrdinalIgnoreCase)).Glyph;

    public static bool IsEmoji(string? icon) => !string.IsNullOrWhiteSpace(icon) && GlyphOf(icon) is null;

    /// <summary>An element for an icon: FontIcon for a Fluent name, emoji text otherwise, <paramref name="fallbackGlyph"/> when empty.</summary>
    public static FrameworkElement Element(string? icon, double size, Brush? foreground = null, string fallbackGlyph = ListGlyph)
    {
        if (IsEmoji(icon))
            return new TextBlock
            {
                Text = icon, FontSize = size, FontFamily = new FontFamily("Segoe UI Emoji"), LineHeight = size * 1.2,
                VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center,
                TextLineBounds = TextLineBounds.Tight,
            };
        var f = new FontIcon { Glyph = GlyphOf(icon) ?? fallbackGlyph, FontSize = size };
        if (foreground is not null) f.Foreground = foreground;
        return f;
    }

    /// <summary>A NavigationView icon. Emoji become a bitmap-free FontIcon in the emoji font.</summary>
    public static IconElement NavIcon(string? icon, Brush? foreground, string fallbackGlyph = ListGlyph)
    {
        if (IsEmoji(icon)) return new FontIcon { Glyph = icon!, FontFamily = new FontFamily("Segoe UI Emoji") };
        var f = new FontIcon { Glyph = GlyphOf(icon) ?? fallbackGlyph };
        if (foreground is not null) f.Foreground = foreground;
        return f;
    }

    public static string DefaultColumnGlyph(BoardColumn c) => c.IsDone ? "" : c.IsDefault ? "" : "";

    /// <summary>The system accent colour (for the "Accent" background).</summary>
    public static Color Accent => (Color)Application.Current.Resources["SystemAccentColor"];

    /// <summary>
    /// The brush behind a list: a colour, a gradient, the accent or an image. Null means none.
    /// <paramref name="image"/> is the uploaded background, when the style is "image".
    /// </summary>
    public static Brush? Background(ListStyle style, BitmapImage? image)
    {
        switch (style.Background)
        {
            case "color" when Ui.IsHex(style.BackgroundValue):
                return Ui.Hex(style.BackgroundValue);
            case "gradient":
            {
                var parts = (style.BackgroundValue ?? "").Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
                var a = Ui.ParseColor(parts.ElementAtOrDefault(0), Color.FromArgb(255, 37, 100, 207));
                var b = Ui.ParseColor(parts.ElementAtOrDefault(1), Color.FromArgb(255, 122, 79, 214));
                var angle = double.TryParse(parts.ElementAtOrDefault(2), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var deg) ? deg : 135;
                var rad = (angle - 90) * Math.PI / 180;
                var dx = Math.Cos(rad) / 2; var dy = Math.Sin(rad) / 2;
                var g = new LinearGradientBrush { StartPoint = new(0.5 - dx, 0.5 - dy), EndPoint = new(0.5 + dx, 0.5 + dy) };
                g.GradientStops.Add(new GradientStop { Color = a, Offset = 0 });
                g.GradientStops.Add(new GradientStop { Color = b, Offset = 1 });
                return g;
            }
            case "accent":
            {
                var acc = Accent;
                var dark = (Color)Application.Current.Resources["SystemAccentColorDark2"];
                var g = new LinearGradientBrush { StartPoint = new(0, 0), EndPoint = new(1, 1) };
                g.GradientStops.Add(new GradientStop { Color = acc, Offset = 0 });
                g.GradientStops.Add(new GradientStop { Color = dark, Offset = 1 });
                return g;
            }
            case "image" when image is not null:
                return new ImageBrush { ImageSource = image, Stretch = Stretch.UniformToFill };
            default:
                return null;
        }
    }
}
