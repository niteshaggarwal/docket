using System.Globalization;
using Docket.Core;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.UI;

namespace Docket.App.Services;

/// <summary>Small helpers for building Fluent UI in code: resources, colours, avatars, priority badges.</summary>
public static class Ui
{
    public static T Res<T>(string key) => (T)Application.Current.Resources[key];
    public static Brush Brush(string key) => Res<Brush>(key);
    public static Style TextStyle(string key) => Res<Style>(key);

    public static Color ParseColor(string? hex, Color fallback)
    {
        var h = (hex ?? "").Trim().TrimStart('#');
        if (h.Length != 6 || !uint.TryParse(h, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var v)) return fallback;
        return Color.FromArgb(255, (byte)(v >> 16), (byte)(v >> 8), (byte)v);
    }

    public static bool IsHex(string? hex) => ParseColor(hex, Color.FromArgb(0, 0, 0, 0)).A != 0;

    public static SolidColorBrush Hex(string? hex, double alpha = 1, string fallback = "8a8886")
    {
        var c = ParseColor(hex, ParseColor(fallback, Color.FromArgb(255, 138, 136, 134)));
        c.A = (byte)Math.Round(Math.Clamp(alpha, 0, 1) * 255);
        return new SolidColorBrush(c);
    }

    public static string ToHex(Color c) => $"{c.R:x2}{c.G:x2}{c.B:x2}";

    /// <summary>The palette offered for lists, columns and backgrounds.</summary>
    public static readonly (string Name, string Hex)[] Palette =
    [
        ("Blue", "2564cf"), ("Sky", "0f8fd6"), ("Teal", "0e8a7e"), ("Green", "2f8f46"), ("Lime", "6e9a1f"), ("Amber", "d18b00"),
        ("Orange", "e8590c"), ("Red", "d13438"), ("Pink", "c2185b"), ("Purple", "7a4fd6"), ("Indigo", "4f5bd5"), ("Grey", "6b6b6b"),
    ];

    // ---------- priority ----------
    public static string PriorityHex(int p) => p switch
    {
        >= 5 => "c0175d", 4 => "d13438", 3 => "e8590c", 2 => "c98a00", 1 => "3a86c8", _ => "8a8886",
    };

    public static bool IsDark => Application.Current.RequestedTheme == ApplicationTheme.Dark;

    /// <summary>A colour made lighter in dark theme so coloured text stays readable on dark surfaces.</summary>
    public static string Readable(string hex)
    {
        if (!IsDark) return hex;
        var c = ParseColor(hex, Color.FromArgb(255, 138, 136, 134));
        static byte Mix(byte v) => (byte)(v + (255 - v) * 0.42);
        return ToHex(Color.FromArgb(255, Mix(c.R), Mix(c.G), Mix(c.B)));
    }

    /// <summary>A small coloured pill: "High", "Urgent"...</summary>
    public static Border PriorityBadge(int p, bool compact = false)
    {
        var text = new TextBlock
        {
            Text = DocketService.PriorityName(p), FontSize = compact ? 11 : 12, FontWeight = FontWeights.SemiBold,
            Foreground = Hex(Readable(PriorityHex(p))), VerticalAlignment = VerticalAlignment.Center,
        };
        var b = new Border
        {
            CornerRadius = new CornerRadius(4), Padding = new Thickness(6, 1, 6, 1), Background = Hex(PriorityHex(p), IsDark ? 0.24 : 0.14),
            Child = text, VerticalAlignment = VerticalAlignment.Center,
        };
        ToolTipService.SetToolTip(b, $"Priority: {DocketService.PriorityName(p)}");
        return b;
    }

    // ---------- avatars ----------
    private static readonly string[] AvatarColors = ["2564cf", "0e8a7e", "c2185b", "7a4fd6", "e8590c", "2f8f46", "4f5bd5", "b4009e", "0f8fd6", "a4262c"];

    public static string AvatarHex(VUser u) => AvatarColors[(int)(Math.Abs(u.Id * 7 + u.Username.Length) % AvatarColors.Length)];

    /// <summary>A coloured circle with the person's initials.</summary>
    public static FrameworkElement Avatar(VUser u, double size = 28, bool ring = false)
    {
        var g = new Grid { Width = size, Height = size };
        g.Children.Add(new Ellipse
        {
            Fill = Hex(AvatarHex(u)),
            Stroke = ring ? Brush("SolidBackgroundFillColorBaseBrush") : null,
            StrokeThickness = ring ? Math.Max(1.5, size / 14) : 0,
        });
        g.Children.Add(new TextBlock
        {
            Text = DocketService.Initials(u), FontSize = Math.Max(8, size * 0.38), FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Color.FromArgb(255, 255, 255, 255)),
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
        });
        ToolTipService.SetToolTip(g, DocketService.DisplayName(u));
        AutomationProperties.SetName(g, DocketService.DisplayName(u));
        return g;
    }

    /// <summary>Overlapping avatars, at most <paramref name="max"/>, then "+N".</summary>
    public static FrameworkElement AvatarStack(IReadOnlyList<VUser> users, double size = 20, int max = 3)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        for (var i = 0; i < Math.Min(max, users.Count); i++)
        {
            var a = Avatar(users[i], size, ring: true);
            if (i > 0) a.Margin = new Thickness(-size * 0.3, 0, 0, 0);
            panel.Children.Add(a);
        }
        if (users.Count > max)
            panel.Children.Add(new TextBlock { Text = $"+{users.Count - max}", FontSize = 11, Margin = new Thickness(4, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, Opacity = 0.7 });
        ToolTipService.SetToolTip(panel, "Assigned to " + string.Join(", ", users.Select(DocketService.DisplayName)));
        return panel;
    }

    public static FontIcon Glyph(string glyph, double size = 14, Brush? foreground = null)
    {
        var f = new FontIcon { Glyph = glyph, FontSize = size };
        if (foreground is not null) f.Foreground = foreground;
        return f;
    }

    public static TextBlock Text(string text, string? style = null, double opacity = 1, TextWrapping wrap = TextWrapping.NoWrap)
    {
        var t = new TextBlock { Text = text, Opacity = opacity, TextWrapping = wrap, TextTrimming = TextTrimming.CharacterEllipsis };
        if (style is not null) t.Style = TextStyle(style);
        return t;
    }

    /// <summary>A Fluent card: layer background, subtle stroke, 8px corners.</summary>
    public static Border Card(UIElement child, double padding = 16) => new()
    {
        Background = Brush("CardBackgroundFillColorDefaultBrush"),
        BorderBrush = Brush("CardStrokeColorDefaultBrush"),
        BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8), Padding = new Thickness(padding), Child = child,
    };

    public static Button IconButton(string glyph, string name, RoutedEventHandler click, double size = 14)
    {
        var b = new Button
        {
            Content = Glyph(glyph, size), Background = new SolidColorBrush(Color.FromArgb(0, 0, 0, 0)), BorderThickness = new Thickness(0),
            Padding = new Thickness(8, 6, 8, 6), VerticalAlignment = VerticalAlignment.Center,
        };
        b.Click += click;
        ToolTipService.SetToolTip(b, name);
        AutomationProperties.SetName(b, name);
        return b;
    }

    public static string FriendlyDate(DateTimeOffset when)
    {
        var local = when.ToLocalTime();
        var today = DateTimeOffset.Now.Date;
        var day = local.Date;
        var c = CultureInfo.CurrentCulture;
        if (day == today) return "Today " + local.ToString("t", c);
        if (day == today.AddDays(-1)) return "Yesterday " + local.ToString("t", c);
        if (day == today.AddDays(1)) return "Tomorrow " + local.ToString("t", c);
        return day.Year == today.Year ? local.ToString("ddd d MMM", c) + ", " + local.ToString("t", c) : local.ToString("d MMM yyyy", c);
    }
}
