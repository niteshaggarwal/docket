using Docket.Mobile.ViewModels;

namespace Docket.Mobile.Views;

public partial class ListPage : ContentPage
{
    private readonly ListViewModel _vm;

    public ListPage(ListViewModel vm)
    {
        InitializeComponent();
        BindingContext = _vm = vm;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _vm.RefreshAsync();
    }
}

/// <summary>Picks the heading or task template for Planned's grouped rows.</summary>
public sealed class RowSelector : DataTemplateSelector
{
    public DataTemplate? Header { get; set; }
    public DataTemplate? Task { get; set; }
    protected override DataTemplate OnSelectTemplate(object item, BindableObject container) => item is GroupHeaderRow ? Header! : Task!;
}
