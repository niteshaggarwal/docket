using System.ComponentModel;
using Docket.Mobile.Services;
using Docket.Mobile.ViewModels;

namespace Docket.Mobile.Views;

public partial class ListPage : ContentPage
{
    private readonly ListViewModel _vm;

    public ListPage(ListViewModel vm)
    {
        InitializeComponent();
        BindingContext = _vm = vm;
        _vm.DataChanged += (_, _) => Redraw();
        _vm.PropertyChanged += OnVmChanged;
        if (Application.Current is { } app) app.RequestedThemeChanged += (_, _) => Redraw();
    }

    private bool _appeared;

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        _visible = true;
        ApplyStatusBar();
        if (_appeared && SheetState.JustClosed) return;
        _appeared = true;
        await _vm.RefreshAsync();
        ApplyStatusBar();
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        _visible = false;
        AppShell.ApplyStatusBar();
    }

    private bool _visible;

    private void ApplyStatusBar()
    {
        if (!_visible) return;
#if ANDROID
        if (_vm.StatusBarColor is not { } c) { AppShell.ApplyStatusBar(); return; }
        try
        {
            CommunityToolkit.Maui.Core.Platform.StatusBar.SetColor(c);
            CommunityToolkit.Maui.Core.Platform.StatusBar.SetStyle(CommunityToolkit.Maui.Core.StatusBarStyle.LightContent);
        }
        catch (InvalidOperationException) { }
#endif
    }

    private void OnVmChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ListViewModel.HasBackground) or nameof(ListViewModel.BackgroundBrush)) ApplyStatusBar();
        if (e.PropertyName is nameof(ListViewModel.ViewMode) or nameof(ListViewModel.Views)) BuildPills();
    }

    private void Redraw()
    {
        BuildPills();
        AltHost.Content = _vm.ViewMode switch
        {
            "table" => ListViews.Table(_vm),
            "gantt" => ListViews.Gantt(_vm),
            "board" => ListViews.Board(_vm),
            _ => null,
        };
    }

    private void BuildPills()
    {
        Pills.Clear();
        foreach (var (id, name, glyph) in _vm.Views)
            Pills.Add(U.Pill(name, _vm.ViewMode == id, () => _vm.SetViewAsync(id), glyph));
        if (_vm.IsUpcoming)
        {
            Pills.Add(new BoxView { WidthRequest = 1, HeightRequest = 20, VerticalOptions = LayoutOptions.Center, Margin = new Thickness(4, 0) }.Themed(BoxView.ColorProperty, Th.Stroke));
            foreach (var d in new[] { 7, 14, 30 })
                Pills.Add(U.Pill($"{d} days", Prefs.UpcomingDays == d, () => _vm.SetUpcomingDaysAsync(d)));
        }
        Pills.Add(new BoxView { WidthRequest = 8, Color = Colors.Transparent });
    }
}

/// <summary>Picks the heading or task template for grouped rows.</summary>
public sealed class RowSelector : DataTemplateSelector
{
    public DataTemplate? Header { get; set; }
    public DataTemplate? Task { get; set; }
    protected override DataTemplate OnSelectTemplate(object item, BindableObject container) => item is GroupHeaderRow ? Header! : Task!;
}
