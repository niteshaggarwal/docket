using Docket.App.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;

namespace Docket.App.Controls;

/// <summary>The result of a picker: Cancelled, or a chosen value (null value = "none").</summary>
public readonly record struct Picked<T>(bool Ok, T? Value);

/// <summary>Icon and colour pickers shown as dialogs.</summary>
public static class Pickers
{
    /// <summary>Fluent icons in a grid plus an emoji box. Returns a Fluent icon name, an emoji, or null for "no icon".</summary>
    public static async Task<Picked<string>> IconAsync(XamlRoot root, string title, string? current, string? tintHex = null)
    {
        string? choice = current;
        var preview = new Border { Width = 48, Height = 48, CornerRadius = new CornerRadius(8), Background = Ui.Brush("SubtleFillColorSecondaryBrush") };
        void UpdatePreview() => preview.Child = Icons.Element(choice, 24, tintHex is null ? Ui.Brush("AccentTextFillColorPrimaryBrush") : Ui.Hex(tintHex));
        UpdatePreview();

        var grid = new GridView { SelectionMode = ListViewSelectionMode.Single, MaxHeight = 280, Padding = new Thickness(0) };
        foreach (var (name, glyph) in Icons.Catalog)
        {
            var item = new GridViewItem
            {
                Content = new FontIcon { Glyph = glyph, FontSize = 18 }, Width = 44, Height = 44, Tag = name, Margin = new Thickness(2),
            };
            ToolTipService.SetToolTip(item, name);
            AutomationProperties.SetName(item, name);
            grid.Items.Add(item);
            if (name.Equals(current, StringComparison.OrdinalIgnoreCase)) grid.SelectedItem = item;
        }

        var emoji = new TextBox { Header = "Or an emoji", PlaceholderText = "Press Win + . to pick one", MaxLength = 16, Width = 220, Text = Icons.IsEmoji(current) ? current! : "" };
        var none = new HyperlinkButton { Content = "No icon", VerticalAlignment = VerticalAlignment.Bottom };
        grid.SelectionChanged += (_, _) =>
        {
            if (grid.SelectedItem is GridViewItem { Tag: string n }) { choice = n; emoji.Text = ""; UpdatePreview(); }
        };
        emoji.TextChanged += (_, _) =>
        {
            var t = (emoji.Text ?? "").Trim();
            if (t.Length == 0) return;
            choice = t;
            grid.SelectedItem = null;
            UpdatePreview();
        };
        none.Click += (_, _) => { choice = null; grid.SelectedItem = null; emoji.Text = ""; UpdatePreview(); };

        var top = new Grid { ColumnSpacing = 16 };
        top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(emoji, 1);
        Grid.SetColumn(none, 2);
        top.Children.Add(preview);
        top.Children.Add(emoji);
        top.Children.Add(none);

        var panel = new StackPanel { Spacing = 12, Width = 460 };
        panel.Children.Add(top);
        panel.Children.Add(Ui.Text("Icons", "BodyStrongTextBlockStyle"));
        panel.Children.Add(grid);
        var d = new ContentDialog { XamlRoot = root, Title = title, Content = panel, PrimaryButtonText = "Save", CloseButtonText = "Cancel", DefaultButton = ContentDialogButton.Primary };
        return await d.ShowAsync() == ContentDialogResult.Primary ? new(true, choice) : new(false, null);
    }

    /// <summary>A row of colour swatches plus a full colour picker. Returns "rrggbb" or null for "default".</summary>
    public static async Task<Picked<string>> ColorAsync(XamlRoot root, string title, string? current, bool allowNone = true)
    {
        string? choice = current;
        var picker = new ColorPicker
        {
            Color = Ui.ParseColor(current, Icons.Accent), IsAlphaEnabled = false, IsMoreButtonVisible = false,
            IsColorSliderVisible = true, IsColorChannelTextInputVisible = false, IsHexInputVisible = true, Visibility = Visibility.Collapsed,
        };
        var swatches = Swatches(current, allowNone, hex =>
        {
            choice = hex;
            picker.Color = Ui.ParseColor(hex, Icons.Accent);
        });
        var custom = new HyperlinkButton { Content = "Custom colour…" };
        custom.Click += (_, _) => picker.Visibility = picker.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;
        picker.ColorChanged += (_, e) => { if (picker.Visibility == Visibility.Visible) choice = Ui.ToHex(e.NewColor); };
        var panel = new StackPanel { Spacing = 12, Width = 380 };
        panel.Children.Add(swatches);
        panel.Children.Add(custom);
        panel.Children.Add(picker);
        var d = new ContentDialog { XamlRoot = root, Title = title, Content = new ScrollViewer { Content = panel, MaxHeight = 520 }, PrimaryButtonText = "Save", CloseButtonText = "Cancel", DefaultButton = ContentDialogButton.Primary };
        return await d.ShowAsync() == ContentDialogResult.Primary ? new(true, choice) : new(false, null);
    }

    /// <summary>Round colour swatches; the chosen one gets a ring.</summary>
    public static FrameworkElement Swatches(string? current, bool allowNone, Action<string?> picked)
    {
        var wrap = new VariableSizedWrapGrid { Orientation = Orientation.Horizontal, ItemWidth = 40, ItemHeight = 40, MaximumRowsOrColumns = 8 };
        var rings = new List<(Ellipse Ring, string? Hex)>();
        void Mark(string? hex) { foreach (var (r, h) in rings) r.Opacity = string.Equals(h, hex, StringComparison.OrdinalIgnoreCase) ? 1 : 0; }
        IEnumerable<(string Name, string? Hex)> all = allowNone ? new (string, string?)[] { ("Default", null) }.Concat(Ui.Palette.Select(p => (p.Name, (string?)p.Hex))) : Ui.Palette.Select(p => (p.Name, (string?)p.Hex));
        foreach (var (name, hex) in all)
        {
            var g = new Grid { Width = 32, Height = 32 };
            var ring = new Ellipse { Stroke = Ui.Brush("TextFillColorPrimaryBrush"), StrokeThickness = 2, Opacity = 0 };
            var dot = new Ellipse { Margin = new Thickness(4), Fill = hex is null ? Ui.Brush("ControlStrongFillColorDefaultBrush") : Ui.Hex(hex) };
            g.Children.Add(dot);
            if (hex is null) g.Children.Add(new FontIcon { Glyph = "", FontSize = 10, Foreground = Ui.Brush("TextOnAccentFillColorPrimaryBrush") });
            g.Children.Add(ring);
            rings.Add((ring, hex));
            var b = new Button { Content = g, Padding = new Thickness(0), Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent), BorderThickness = new Thickness(0), CornerRadius = new CornerRadius(16) };
            ToolTipService.SetToolTip(b, name);
            AutomationProperties.SetName(b, name);
            b.Click += (_, _) => { Mark(hex); picked(hex); };
            wrap.Children.Add(b);
        }
        Mark(current);
        return wrap;
    }
}
