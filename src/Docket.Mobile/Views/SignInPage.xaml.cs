using Docket.Core;
using Docket.Mobile.Services;

namespace Docket.Mobile.Views;

public partial class SignInPage : ContentPage
{
    private readonly Session _session;

    public SignInPage(Session session)
    {
        InitializeComponent();
        _session = session;
        ServerBox.Text = session.ServerUrl ?? "";
    }

    private async void SignIn_Clicked(object? sender, EventArgs e)
    {
        ErrorText.IsVisible = false;
        SignInButton.IsEnabled = false;
        Busy.IsRunning = true;
        try
        {
            await _session.SignInAsync(ServerBox.Text ?? "", UserBox.Text ?? "", PassBox.Text ?? "", TotpBox.Text);
            PassBox.Text = "";
            await Reminders.EnsurePermissionAsync();
            await Shell.Current.GoToAsync("//home");
        }
        catch (Exception ex) when (ex is DocketException or HttpRequestException or TaskCanceledException or UriFormatException)
        {
            ErrorText.Text = ex is HttpRequestException or TaskCanceledException
                ? "Can't reach that server. Check the address, and that Tailscale is on (on this phone and on the PC)."
                : ex.Message;
            ErrorText.IsVisible = true;
        }
        finally
        {
            SignInButton.IsEnabled = true;
            Busy.IsRunning = false;
        }
    }
}
