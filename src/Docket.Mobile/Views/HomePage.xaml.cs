using Docket.Mobile.ViewModels;

namespace Docket.Mobile.Views;

public partial class HomePage : ContentPage
{
    private readonly HomeViewModel _vm;

    public HomePage(HomeViewModel vm)
    {
        InitializeComponent();
        BindingContext = _vm = vm;
    }

    private bool _appeared;

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (_appeared && SheetState.JustClosed) return;
        _appeared = true;
        await _vm.RefreshAsync();
    }
}
