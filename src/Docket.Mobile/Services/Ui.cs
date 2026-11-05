using Docket.Core;
using Microsoft.Maui.Controls.Shapes;

namespace Docket.Mobile.Services;

/// <summary>A light/dark colour pair from the Fluent palette in Fluent.xaml.</summary>
public readonly record struct Duo(Color Light, Color Dark)
{
    public Color Now => Th.Dark ? Dark : Light;
    public static Duo Of(string light, string dark) => new(Color.FromArgb(light), Color.FromArgb(dark));
}

/// <summary>Theme colours for views built in code (the XAML pages use the same values from Fluent.xaml).</summary>
public static class Th
{
    public static readonly Duo Bg = Duo.Of("#F3F3F3", "#202020");
    public static readonly Duo Card = Duo.Of("#FFFFFF", "#2B2B2B");
    public static readonly Duo Stroke = Duo.Of("#E5E5E5", "#383838");
    public static readonly Duo Control = Duo.Of("#FBFBFB", "#2D2D2D");
    public static readonly Duo Text = Duo.Of("#1A1A1A", "#FFFFFF");
    public static readonly Duo Text2 = Duo.Of("#5D5D5D", "#C5C5C5");
    public static readonly Duo Accent = Duo.Of("#005FB8", "#60CDFF");
    public static readonly Duo OnAccent = Duo.Of("#FFFFFF", "#000000");
    public static readonly Duo Danger = Duo.Of("#C42B1C", "#FF99A4");
    public static readonly Duo Subtle = Duo.Of("#0F000000", "#1AFFFFFF");
    public static readonly Duo AccentTint = Duo.Of("#1A005FB8", "#2660CDFF");

    public static bool Dark => Application.Current?.RequestedTheme == AppTheme.Dark;

    public static T Themed<T>(this T view, BindableProperty property, Duo color) where T : BindableObject
    {
        view.SetAppThemeColor(property, color.Light, color.Dark);
        return view;
    }

    /// <summary>Badge colour for a priority (1 low .. 5 do now).</summary>
    public static Duo Priority(int p) => p switch
    {
        >= 5 => Duo.Of("#C42B1C", "#FF99A4"),
        4 => Duo.Of("#C94F0F", "#FFB27A"),
        3 => Duo.Of("#946200", "#FFD66B"),
        2 => Duo.Of("#005FB8", "#60CDFF"),
        _ => Duo.Of("#6B6B6B", "#B5B5B5"),
    };

    private static readonly string[] AvatarColors = ["#0F6CBD", "#8764B8", "#C239B3", "#E3008C", "#CA5010", "#498205", "#038387", "#4F6BED", "#986F0B", "#69797E"];

    /// <summary>A stable colour per person, so the same initials always get the same disc.</summary>
    public static Color AvatarColor(string key)
    {
        var h = 17;
        foreach (var c in key) h = unchecked(h * 31 + c);
        return Color.FromArgb(AvatarColors[(int)((uint)h % AvatarColors.Length)]);
    }

    public static Color Hex(string? hex, Color fallback) =>
        (hex ?? "").Trim().TrimStart('#') is { Length: 6 } h && int.TryParse(h, System.Globalization.NumberStyles.HexNumber, null, out _) ? Color.FromArgb("#" + h) : fallback;
}

/// <summary>Small factory for the Fluent building blocks used by pages written in C#.</summary>
public static class U
{
    public const string Icons = "FluentIcons";
    public const string IconsFilled = "FluentIconsFilled";

    public static Label Text(string text, double size = 15, bool strong = false, Duo? color = null) =>
        new Label { Text = text, FontSize = size, FontFamily = strong ? "SelawikSemibold" : "Selawik", LineBreakMode = LineBreakMode.WordWrap }
            .Themed(Label.TextColorProperty, color ?? Th.Text);

    public static Label Caption(string text, double size = 12) => Text(text, size, false, Th.Text2);

    public static Label Title(string text, double size = 28) => Text(text, size, true);

    public static Label Section(string text) => new Label
    {
        Text = text, FontFamily = "SelawikSemibold", FontSize = 15, Margin = new Thickness(4, 14, 0, 2),
    }.Themed(Label.TextColorProperty, Th.Text);

    public static Label Glyph(string glyph, double size = 20, Duo? color = null, bool filled = false) => new Label
    {
        Text = glyph, FontFamily = filled ? IconsFilled : Icons, FontSize = size,
        HorizontalTextAlignment = TextAlignment.Center, VerticalTextAlignment = TextAlignment.Center,
    }.Themed(Label.TextColorProperty, color ?? Th.Text);

    /// <summary>An icon that is a Fluent glyph or an emoji (from a list's or column's look).</summary>
    public static Label ListIcon(string? icon, double size, Duo color, string fallback = IconChars.List)
    {
        var glyph = Docket.Mobile.Services.Icons.GlyphOf(icon);
        if (glyph is null && !string.IsNullOrWhiteSpace(icon))
            return new Label { Text = icon, FontSize = size * 0.9, HorizontalTextAlignment = TextAlignment.Center, VerticalTextAlignment = TextAlignment.Center };
        return Glyph(glyph ?? fallback, size, color);
    }

    public static Border Card(View content, Thickness? padding = null) => new Border
    {
        Content = content, StrokeThickness = 1, StrokeShape = new RoundRectangle { CornerRadius = 8 }, Padding = padding ?? new Thickness(12),
    }.Themed(VisualElement.BackgroundColorProperty, Th.Card).Themed(Border.StrokeProperty, Th.Stroke);

    public static Border Field(View content) => new Border
    {
        Content = content, StrokeThickness = 1, StrokeShape = new RoundRectangle { CornerRadius = 4 }, Padding = new Thickness(10, 0),
    }.Themed(VisualElement.BackgroundColorProperty, Th.Control).Themed(Border.StrokeProperty, Th.Stroke);

    public static Entry Entry(string placeholder, string? text = null, Keyboard? keyboard = null, bool password = false)
    {
        var e = new Entry { Placeholder = placeholder, Text = text ?? "", IsPassword = password, FontFamily = "Selawik", FontSize = 15, MinimumHeightRequest = 44, BackgroundColor = Colors.Transparent };
        if (keyboard is not null) e.Keyboard = keyboard;
        return e.Themed(Microsoft.Maui.Controls.Entry.TextColorProperty, Th.Text).Themed(Microsoft.Maui.Controls.Entry.PlaceholderColorProperty, Th.Text2);
    }

    public static BoxView Divider(double left = 0) => new BoxView { HeightRequest = 1, Margin = new Thickness(left, 0, 0, 0) }.Themed(BoxView.ColorProperty, Th.Stroke);

    public static T Tap<T>(this T view, Func<Task> action) where T : View
    {
        view.GestureRecognizers.Add(new TapGestureRecognizer { Command = new Command(async () => await Guard(action)) });
        return view;
    }

    public static T Tap<T>(this T view, Action action) where T : View
    {
        view.GestureRecognizers.Add(new TapGestureRecognizer { Command = new Command(action) });
        return view;
    }

    public static Button Accent(string text, Func<Task> click)
    {
        var b = new Button { Text = text, Style = (Style)Application.Current!.Resources["Accent"] };
        b.Clicked += async (_, _) => await Guard(click);
        return b;
    }

    public static Button Plain(string text, Func<Task> click)
    {
        var b = new Button { Text = text };
        b.Clicked += async (_, _) => await Guard(click);
        return b;
    }

    public static Button IconButton(string glyph, Func<Task> click, string description, Duo? color = null, double size = 20)
    {
        var b = new Button { Text = glyph, Style = (Style)Application.Current!.Resources["IconButton"], FontSize = size };
        if (color is { } c) b.Themed(Button.TextColorProperty, c);
        SemanticProperties.SetDescription(b, description);
        b.Clicked += async (_, _) => await Guard(click);
        return b;
    }

    /// <summary>A 52 px tappable row: icon, text and an optional detail on the right.</summary>
    public static Grid Row(string glyph, string text, Func<Task>? tap, string? detail = null, Duo? glyphColor = null, Duo? textColor = null)
    {
        var g = new Grid { ColumnDefinitions = [new(44), new(GridLength.Star), new(GridLength.Auto)], MinimumHeightRequest = 52, Padding = new Thickness(0, 0, 12, 0) };
        g.Add(Glyph(glyph, 20, glyphColor), 0);
        var label = Text(text, 15, false, textColor);
        label.VerticalTextAlignment = TextAlignment.Center;
        label.VerticalOptions = LayoutOptions.Center;
        label.Margin = new Thickness(0, 8);
        g.Add(label, 1);
        if (detail is not null) { var d = Caption(detail, 13); d.VerticalOptions = LayoutOptions.Center; g.Add(d, 2); }
        if (tap is not null) g.Tap(tap);
        return g;
    }

    /// <summary>A round initials avatar, coloured per person.</summary>
    public static Border Avatar(VUser u, double size = 28)
    {
        var initials = DocketService.Initials(u);
        return new Border
        {
            WidthRequest = size, HeightRequest = size, StrokeThickness = 0, Padding = 0,
            StrokeShape = new RoundRectangle { CornerRadius = size / 2 },
            BackgroundColor = Th.AvatarColor(u.Username.Length > 0 ? u.Username : initials),
            Content = new Label
            {
                Text = initials, TextColor = Colors.White, FontFamily = "SelawikSemibold", FontSize = size * 0.4,
                HorizontalTextAlignment = TextAlignment.Center, VerticalTextAlignment = TextAlignment.Center,
            },
        };
    }

    /// <summary>A small rounded badge, tinted with its own colour.</summary>
    public static Border Badge(string text, Duo color, double size = 12)
    {
        var l = new Label { Text = text, FontFamily = "SelawikSemibold", FontSize = size }.Themed(Label.TextColorProperty, color);
        var b = new Border { Content = l, StrokeThickness = 0, Padding = new Thickness(6, 0), StrokeShape = new RoundRectangle { CornerRadius = 4 }, VerticalOptions = LayoutOptions.Center };
        b.SetAppThemeColor(VisualElement.BackgroundColorProperty, color.Light.WithAlpha(0.14f), color.Dark.WithAlpha(0.2f));
        return b;
    }

    /// <summary>A pill used for segmented choices (views, day ranges, permissions).</summary>
    public static Border Pill(string text, bool selected, Func<Task> tap, string? glyph = null)
    {
        var row = new HorizontalStackLayout { Spacing = 6, VerticalOptions = LayoutOptions.Center };
        var color = selected ? Th.OnAccent : Th.Text;
        if (glyph is not null) row.Add(Glyph(glyph, 16, color));
        var l = Text(text, 14, selected, color);
        l.VerticalTextAlignment = TextAlignment.Center;
        row.Add(l);
        var b = new Border { Content = row, StrokeShape = new RoundRectangle { CornerRadius = 16 }, Padding = new Thickness(12, 0), HeightRequest = 32, StrokeThickness = selected ? 0 : 1 };
        b.Themed(VisualElement.BackgroundColorProperty, selected ? Th.Accent : Th.Control).Themed(Border.StrokeProperty, Th.Stroke);
        return b.Tap(tap);
    }

    /// <summary>Runs a UI action and shows a server or input error instead of crashing.</summary>
    public static async Task Guard(Func<Task> action)
    {
        try { await action(); }
        catch (Exception ex) when (IsExpected(ex))
        {
            if (Shell.Current is { } s) await s.DisplayAlertAsync("Something went wrong", Friendly(ex), "OK");
        }
    }

    public static bool IsExpected(Exception ex) =>
        ex is HttpRequestException or VikunjaException or DocketException or TaskCanceledException or IOException or InvalidOperationException or UriFormatException or FormatException;

    /// <summary>An error worded for people: no "Vikunja", no status-code soup.</summary>
    public static string Friendly(Exception ex) => ex switch
    {
        HttpRequestException => "Can't reach the Docket server. Check your connection (and Tailscale, if you use it).",
        TaskCanceledException => "The Docket server took too long to answer. Try again.",
        VikunjaException { Status: System.Net.HttpStatusCode.Unauthorized } =>
            "This phone's sign-in has expired or doesn't cover this yet (sharing and teams need a sign-in from this version). Sign out and sign in again in Settings > Account.",
        VikunjaException { Status: System.Net.HttpStatusCode.Forbidden } => "You don't have permission to do that on this list.",
        _ => Clean(ex.Message),
    };

    public static string Clean(string message)
    {
        var m = message.Replace("Vikunja: ", "", StringComparison.Ordinal).Replace("Vikunja", "the Docket server", StringComparison.Ordinal);
        m = System.Text.RegularExpressions.Regex.Replace(m, @"^\d{3}\s+", "");
        return m.Length > 0 ? char.ToUpperInvariant(m[0]) + m[1..] : "Something went wrong.";
    }
}
