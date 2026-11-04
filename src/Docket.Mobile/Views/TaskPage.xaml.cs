using Docket.Mobile.ViewModels;

namespace Docket.Mobile.Views;

public partial class TaskPage : ContentPage
{
    private readonly TaskViewModel _vm;

    public TaskPage(TaskViewModel vm)
    {
        InitializeComponent();
        BindingContext = _vm = vm;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _vm.LoadAsync();
    }

    private async void Title_Unfocused(object? sender, FocusEventArgs e) => await _vm.SaveTitleCommand.ExecuteAsync(null);
    private async void Notes_Unfocused(object? sender, FocusEventArgs e) => await _vm.SaveNotesCommand.ExecuteAsync(null);
}
