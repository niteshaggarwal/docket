using System.Globalization;
using Docket.App.Services;
using Docket.Core;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Windows.ApplicationModel.DataTransfer;

namespace Docket.App.Pages;

/// <summary>People (accounts on the Docket server) and teams: add people by email or locally, create teams, add members.</summary>
public sealed partial class PeoplePage : Page, IRefreshable
{
    /// <summary>Accounts created in this session that aren't on a shared list yet, so they still show up here.</summary>
    private static readonly List<VUser> Created = [];
    private bool _userEdited;

    public PeoplePage() => InitializeComponent();

    private static DocketService? Svc => App.Service;
    private static MainWindow? Win => App.Window;
    private string? ServerExe => LocalAccounts.FindVikunja(App.Settings.ServerUrl, App.Settings.VikunjaExe);

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        UserBox.TextChanged += (_, _) => { if (UserBox.FocusState != FocusState.Unfocused) _userEdited = UserBox.Text.Length > 0; };
        var local = ServerExe is not null;
        CreateButton.IsEnabled = local;
        ServerNote.IsOpen = !local;
        ServerNote.Message = "Docket can create accounts only when the server runs on this computer. On the computer that runs your Docket server, "
            + "create the account with the server program (user create -u <username> -e <email> -p <password>), give the person their password, "
            + "then share a list with them here by username.";
        Kind_Changed(KindChoice, null!);
        await RefreshAsync();
    }

    public async Task RefreshAsync()
    {
        if (Svc is not { } svc || Win is null) return;
        Spinner.IsActive = true;
        Spinner.Visibility = Visibility.Visible;
        await Win.RunAsync("Couldn't load people", async () =>
        {
            var people = await svc.PeopleAsync();
            var me = await svc.MeAsync();
            var known = people.Select(p => p.Id).ToHashSet();
            var extra = Created.Where(c => c.Id == 0 ? people.All(p => p.Username != c.Username) : !known.Contains(c.Id)).ToList();
            PeopleGrid.Children.Clear();
            foreach (var u in people) PeopleGrid.Children.Add(PersonCard(u, u.Id == me.Id, shared: true));
            foreach (var u in extra) PeopleGrid.Children.Add(PersonCard(u, false, shared: false));
            var n = people.Count + extra.Count;
            PeopleCount.Text = n == 1 ? "1 person" : $"{n} people";

            var teams = await svc.Client.GetTeamsAsync();
            TeamsPanel.Children.Clear();
            foreach (var t in teams) TeamsPanel.Children.Add(TeamCard(t, people.Concat(extra).ToList()));
            if (teams.Count == 0)
                TeamsPanel.Children.Add(Ui.Card(Ui.Text("No teams yet. A team groups people so you can share lists with all of them at once.", "BodyTextBlockStyle", 0.7, TextWrapping.Wrap)));
        });
        Spinner.IsActive = false;
        Spinner.Visibility = Visibility.Collapsed;
    }

    private static FrameworkElement PersonCard(VUser u, bool isMe, bool shared)
    {
        var g = new Grid { ColumnSpacing = 14 };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Spacing = 2 };
        var name = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        name.Children.Add(new TextBlock { Text = DocketService.DisplayName(u), FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 170 });
        if (isMe) name.Children.Add(new Border { CornerRadius = new CornerRadius(4), Padding = new Thickness(6, 0, 6, 1), Background = Ui.Brush("AccentFillColorDefaultBrush"), Child = new TextBlock { Text = "You", FontSize = 11, Foreground = Ui.Brush("TextOnAccentFillColorPrimaryBrush") } });
        text.Children.Add(name);
        text.Children.Add(Ui.Text("@" + u.Username, "CaptionTextBlockStyle", 0.65));
        if (!shared) text.Children.Add(Ui.Text("New account · not on a shared list yet", "CaptionTextBlockStyle", 0.65));
        Grid.SetColumn(text, 1);
        g.Children.Add(Ui.Avatar(u, 44));
        g.Children.Add(text);
        var card = Ui.Card(g, 16);
        card.Margin = new Thickness(0, 0, 12, 12);
        AutomationProperties.SetName(card, DocketService.DisplayName(u) + (isMe ? " (you)" : ""));
        return card;
    }

    private FrameworkElement TeamCard(VTeam team, List<VUser> people)
    {
        var p = new StackPanel { Spacing = 12 };
        var head = new Grid { ColumnSpacing = 12 };
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var badge = new Border { Width = 40, Height = 40, CornerRadius = new CornerRadius(8), Background = Ui.Brush("SubtleFillColorSecondaryBrush"), Child = Ui.Glyph("", 18, Ui.Brush("AccentTextFillColorPrimaryBrush")) };
        var names = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        names.Children.Add(new TextBlock { Text = team.Name, Style = Ui.TextStyle("BodyStrongTextBlockStyle") });
        var count = team.Members?.Count ?? 0;
        names.Children.Add(Ui.Text(count == 1 ? "1 member" : $"{count} members", "CaptionTextBlockStyle", 0.65));
        var add = new AutoSuggestBox { PlaceholderText = "Add member by username", Width = 240, QueryIcon = new SymbolIcon(Symbol.Add) };
        AutomationProperties.SetName(add, $"Add a member to {team.Name}");
        add.TextChanged += (s, e) =>
        {
            if (e.Reason != AutoSuggestionBoxTextChangeReason.UserInput) return;
            var q = s.Text.Trim();
            s.ItemsSource = q.Length == 0 ? null : people.Where(u => (team.Members ?? []).All(m => m.Id != u.Id) && (u.Username.Contains(q, StringComparison.OrdinalIgnoreCase) || u.Name.Contains(q, StringComparison.OrdinalIgnoreCase))).Select(u => u.Username).ToList();
        };
        add.QuerySubmitted += async (s, e) =>
        {
            var who = (e.ChosenSuggestion as string ?? e.QueryText).Trim();
            if (who.Length == 0 || Svc is not { } svc || Win is null) return;
            await Win.RunAsync("Couldn't add the member", async () => { await svc.Client.AddTeamMemberAsync(team.Id, who); Win.SetStatus($"Added {who} to {team.Name}"); await RefreshAsync(); });
        };
        Grid.SetColumn(names, 1);
        Grid.SetColumn(add, 2);
        head.Children.Add(badge);
        head.Children.Add(names);
        head.Children.Add(add);
        p.Children.Add(head);
        var members = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 16 };
        foreach (var m in team.Members ?? [])
        {
            var chip = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            chip.Children.Add(Ui.Avatar(m, 28));
            chip.Children.Add(new TextBlock { Text = DocketService.DisplayName(m), VerticalAlignment = VerticalAlignment.Center });
            members.Children.Add(chip);
        }
        if (members.Children.Count > 0) p.Children.Add(members);
        return Ui.Card(p, 16);
    }

    // ---------- add a person ----------
    private void Kind_Changed(object sender, SelectionChangedEventArgs e)
    {
        var byEmail = KindChoice.SelectedIndex == 0;
        EmailBox.Visibility = byEmail ? Visibility.Visible : Visibility.Collapsed;
        CreateResult.IsOpen = false;
    }

    private void Name_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_userEdited) return;
        var basis = KindChoice.SelectedIndex == 0 && EmailBox.Text.Contains('@') ? EmailBox.Text.Trim() : NameBox.Text.Trim();
        UserBox.Text = basis.Length == 0 ? "" : LocalAccounts.UsernameFrom(basis);
    }

    private async void Create_Click(object sender, RoutedEventArgs e)
    {
        if (ServerExe is not { } exe || Svc is not { } svc || Win is null) return;
        var byEmail = KindChoice.SelectedIndex == 0;
        var email = byEmail ? EmailBox.Text.Trim() : null;
        var name = NameBox.Text.Trim();
        var username = UserBox.Text.Trim();
        if (byEmail && (email is null || !email.Contains('@') || !email.Contains('.')))
        { Result(InfoBarSeverity.Warning, "Check the email", "Type the person's email address, or choose Local person."); return; }
        if (username.Length < 3) { Result(InfoBarSeverity.Warning, "Pick a username", "Usernames need at least 3 letters or digits."); return; }

        CreateButton.IsEnabled = false;
        CreateBusy.IsActive = true;
        try
        {
            var account = await LocalAccounts.CreateAsync(exe, username, email);
            VUser? user = null;
            try { user = (await svc.Client.SearchUsersAsync(account.Username)).FirstOrDefault(u => u.Username == account.Username); }
            catch (VikunjaException) { }
            user ??= new VUser { Username = account.Username };
            if (name.Length > 0 && user.Name.Length == 0) user.Name = name;
            Created.Add(user);
            NameBox.Text = EmailBox.Text = UserBox.Text = "";
            _userEdited = false;
            CreateResult.IsOpen = false;
            await ShowAccountAsync(account, name.Length > 0 ? name : account.Username);
            await RefreshAsync();
        }
        catch (Exception ex) when (ex is DocketException or System.ComponentModel.Win32Exception or IOException)
        {
            var message = ex.Message.Contains("unknown error", StringComparison.OrdinalIgnoreCase)
                ? $"The server didn't accept it. The username \"{username}\" or the email may already be in use; try another."
                : ex.Message;
            Result(InfoBarSeverity.Error, "Couldn't create the account", message);
        }
        finally
        {
            CreateButton.IsEnabled = true;
            CreateBusy.IsActive = false;
        }
    }

    private async Task ShowAccountAsync(NewAccount account, string who)
    {
        if (Svc is not { } svc || Win is null) return;
        var panel = new StackPanel { Spacing = 12, Width = 420 };
        panel.Children.Add(Ui.Text($"Share these details with {who} so they can sign in to Docket. The password is shown only once.", "BodyTextBlockStyle", 1, TextWrapping.Wrap));
        var grid = new Grid { ColumnSpacing = 12, RowSpacing = 8 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var rows = new[] { ("Server", App.Settings.ServerUrl), ("Username", account.Username), ("Email", account.Email), ("Password", account.Password) };
        for (var i = 0; i < rows.Length; i++)
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var k = Ui.Text(rows[i].Item1, "BodyTextBlockStyle", 0.65);
            var v = new TextBlock { Text = rows[i].Item2, IsTextSelectionEnabled = true };
            if (rows[i].Item1 == "Password")
            {
                v.FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Cascadia Mono, Consolas");
                v.FontWeight = FontWeights.SemiBold;
            }
            Grid.SetRow(k, i); Grid.SetRow(v, i); Grid.SetColumn(v, 1);
            grid.Children.Add(k); grid.Children.Add(v);
        }
        panel.Children.Add(Ui.Card(grid, 14));
        var copy = new Button { Content = "Copy sign-in details" };
        copy.Click += (_, _) =>
        {
            var pkg = new DataPackage();
            pkg.SetText($"Docket server: {App.Settings.ServerUrl}\nUsername: {account.Username}\nPassword: {account.Password}");
            Clipboard.SetContent(pkg);
            copy.Content = "Copied";
        };
        panel.Children.Add(copy);
        var lists = new ComboBox { Header = "Share a list with them now (optional)", PlaceholderText = "Choose a list", MinWidth = 260 };
        lists.Items.Add("Don't share yet");
        foreach (var p in Win.Projects) lists.Items.Add(p.Title);
        lists.SelectedIndex = 0;
        panel.Children.Add(lists);
        var d = new ContentDialog { XamlRoot = XamlRoot, Title = $"{who} can now sign in", Content = panel, PrimaryButtonText = "Done", DefaultButton = ContentDialogButton.Primary };
        await d.ShowAsync();
        if (lists.SelectedIndex > 0)
        {
            var project = Win.Projects[lists.SelectedIndex - 1];
            await Win.RunAsync("Couldn't share the list", async () =>
            {
                await svc.ShareAsync(project.Id, account.Username, SharePermission.ReadWrite);
                Win.SetStatus($"Shared \"{project.Title}\" with {who}");
                await Win.LoadListsAsync();
            });
        }
    }

    private void Result(InfoBarSeverity severity, string title, string message)
    {
        CreateResult.Severity = severity;
        CreateResult.Title = title;
        CreateResult.Message = message;
        CreateResult.IsOpen = true;
    }

    // ---------- teams ----------
    private async void NewTeam_Click(object sender, RoutedEventArgs e)
    {
        if (Svc is not { } svc || Win is null) return;
        var name = await Dialogs.PromptAsync(XamlRoot, "New team", "Team name", "", "Create");
        if (string.IsNullOrWhiteSpace(name)) return;
        await Win.RunAsync("Couldn't create the team", async () => { await svc.Client.CreateTeamAsync(name); await RefreshAsync(); });
    }

    public static string Count(int n) => n.ToString(CultureInfo.CurrentCulture);
}
