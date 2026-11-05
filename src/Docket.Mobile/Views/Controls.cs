using System.Globalization;
using System.Windows.Input;
using Microsoft.Maui.Controls.Shapes;

namespace Docket.Mobile.Views;

/// <summary>
/// A WinUI-style check box: 20 px square, 4 px corners, accent fill with a check mark when ticked.
/// Tapping runs <see cref="Command"/>; the view model decides the new state.
/// </summary>
public sealed class FluentCheck : ContentView
{
    public static readonly BindableProperty IsCheckedProperty = BindableProperty.Create(nameof(IsChecked), typeof(bool), typeof(FluentCheck), false, propertyChanged: (b, _, _) => ((FluentCheck)b).Update());
    public static readonly BindableProperty CommandProperty = BindableProperty.Create(nameof(Command), typeof(ICommand), typeof(FluentCheck));
    public static readonly BindableProperty CommandParameterProperty = BindableProperty.Create(nameof(CommandParameter), typeof(object), typeof(FluentCheck));

    private readonly Border _box;
    private readonly Label _mark;

    public FluentCheck()
    {
        _mark = new Label { FontFamily = "FluentIcons", Text = "", FontSize = 14, HorizontalTextAlignment = TextAlignment.Center, VerticalTextAlignment = TextAlignment.Center };
        _box = new Border { WidthRequest = 22, HeightRequest = 22, StrokeShape = new RoundRectangle { CornerRadius = 4 }, StrokeThickness = 1.5, Content = _mark };
        Content = new Grid { WidthRequest = 44, HeightRequest = 44, Children = { _box } };
        _box.HorizontalOptions = _box.VerticalOptions = LayoutOptions.Center;
        GestureRecognizers.Add(new TapGestureRecognizer { Command = new Command(() => { if (Command?.CanExecute(CommandParameter) == true) Command.Execute(CommandParameter); }) });
        SemanticProperties.SetDescription(this, "Mark as completed");
        if (Application.Current is { } app) app.RequestedThemeChanged += (_, _) => Update();
        Update();
    }

    public bool IsChecked { get => (bool)GetValue(IsCheckedProperty); set => SetValue(IsCheckedProperty, value); }
    public ICommand? Command { get => (ICommand?)GetValue(CommandProperty); set => SetValue(CommandProperty, value); }
    public object? CommandParameter { get => GetValue(CommandParameterProperty); set => SetValue(CommandParameterProperty, value); }

    private void Update()
    {
        var dark = Application.Current?.RequestedTheme == AppTheme.Dark;
        var accent = Color.FromArgb(dark ? "#60CDFF" : "#005FB8");
        _box.BackgroundColor = IsChecked ? accent : Colors.Transparent;
        _box.Stroke = IsChecked ? accent : Color.FromArgb(dark ? "#9A9A9A" : "#8A8A8A");
        _mark.TextColor = dark ? Colors.Black : Colors.White;
        _mark.IsVisible = IsChecked;
    }
}

/// <summary>Overdue dates in the danger colour, others in the secondary text colour.</summary>
public sealed class OverdueColorConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var dark = Application.Current?.RequestedTheme == AppTheme.Dark;
        return value is true ? Color.FromArgb(dark ? "#FF99A4" : "#C42B1C") : Color.FromArgb(dark ? "#C5C5C5" : "#5D5D5D");
    }
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}

/// <summary>Overdue in the danger colour, otherwise normal text (for the detail page's due row).</summary>
public sealed class OverdueOrTextConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var dark = Application.Current?.RequestedTheme == AppTheme.Dark;
        return value is true ? Color.FromArgb(dark ? "#FF99A4" : "#C42B1C") : (dark ? Colors.White : Color.FromArgb("#1A1A1A"));
    }
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}

/// <summary>A category colour at low opacity, for chip backgrounds.</summary>
public sealed class TintConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value is Color c ? c.WithAlpha(0.18f) : Colors.Transparent;
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}

/// <summary>A list's own colour, or the accent colour when it has none.</summary>
public sealed class TintOrAccentConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value as Color ?? Color.FromArgb(Application.Current?.RequestedTheme == AppTheme.Dark ? "#60CDFF" : "#005FB8");
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}

public sealed class ChevronRotationConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value is true ? 90d : 0d;
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}

/// <summary>White on a list background (picture or colour), the theme's text colour otherwise.</summary>
public sealed class OnImageTextConverter : IValueConverter
{
    public bool Secondary { get; set; }
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is true) return Secondary ? Colors.White.WithAlpha(0.8f) : Colors.White;
        var dark = Application.Current?.RequestedTheme == AppTheme.Dark;
        return Secondary ? Color.FromArgb(dark ? "#C5C5C5" : "#5D5D5D") : (dark ? Colors.White : Color.FromArgb("#1A1A1A"));
    }
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}

/// <summary>A colour when one is set (a priority's), otherwise the theme's text colour.</summary>
public sealed class TextOrConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value as Color ?? (Application.Current?.RequestedTheme == AppTheme.Dark ? Colors.White : Color.FromArgb("#1A1A1A"));
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}
