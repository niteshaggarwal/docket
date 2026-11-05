using Docket.Core;
using Docket.Mobile.Services;
using Permissions = Docket.Core.Permissions;

namespace Docket.Mobile.Views;

/// <summary>Who a list is shared with: people (with a permission each), teams and public links.</summary>
public sealed class SharePage : ContentPage, IQueryAttributable
{
    private static readonly SharePermission[] Levels = [SharePermission.Read, SharePermission.ReadWrite, SharePermission.Admin];
    private readonly Session _session;
    private readonly VerticalStackLayout _stack = new() { Padding = new Thickness(12, 4, 12, 24), Spacing = 8 };
    private readonly RefreshView _refresh;
    private long _id;
    private VProject? _project;

    public SharePage(Session session)
    {
        _session = session;
        _refresh = new RefreshView { Content = new ScrollView { Content = _stack } };
        _refresh.Command = new Command(async () => await LoadAsync());
        Content = _refresh;
    }

    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        if (query.TryGetValue("id", out var v) && long.TryParse(v.ToString(), out var id)) _id = id;
    }

    private bool _appeared;

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (_appeared && SheetState.JustClosed) return;
        _appeared = true;
        await LoadAsync();
    }

    private bool _loading;

    private async Task LoadAsync()
    {
        // Setting IsRefreshing runs the refresh command again; one load at a time.
        if (_session.Service is not { } svc || _loading) return;
        _loading = true;
        _refresh.IsRefreshing = true;
        try
        {
            _project = (await svc.ProjectsAsync()).FirstOrDefault(p => p.Id == _id);
            var sharing = await svc.SharingAsync(_id);
            Render(svc, sharing);
        }
        catch (Exception ex) when (U.IsExpected(ex))
        {
            _stack.Clear();
            _stack.Add(U.Title("Share"));
            _stack.Add(U.Text(U.Friendly(ex), 15, false, Th.Danger));
        }
        finally { _refresh.IsRefreshing = false; _loading = false; }
    }

    private void Render(DocketService svc, Sharing s)
    {
        _stack.Clear();
        _stack.Add(U.Title("Share"));
        _stack.Add(U.Caption(_project?.Title ?? "", 15));

        _stack.Add(U.Section("People"));
        var people = new VerticalStackLayout { Spacing = 0 };
        foreach (var u in s.People)
        {
            var user = new VUser { Id = u.Id, Username = u.Username, Name = u.Name };
            var g = new Grid { ColumnDefinitions = [new(44), new(GridLength.Star), new(GridLength.Auto), new(24)], MinimumHeightRequest = 56 };
            g.Add(U.Avatar(user, 32), 0);
            var col = new VerticalStackLayout { VerticalOptions = LayoutOptions.Center, Margin = new Thickness(6, 0, 0, 0) };
            col.Add(U.Text(DocketService.DisplayName(user)));
            col.Add(U.Caption("@" + u.Username));
            g.Add(col, 1);
            var perm = U.Caption(Permissions.Describe(u.Permission), 13);
            perm.VerticalOptions = LayoutOptions.Center;
            g.Add(perm, 2);
            g.Add(U.Glyph(IconChars.ChevronDown, 12, Th.Text2), 3);
            people.Add(g.Tap(() => PersonMenuAsync(svc, u)));
            people.Add(U.Divider(50));
        }
        people.Add(U.Row(IconChars.PersonAdd, "Share with a person", () => AddPersonAsync(svc, s), glyphColor: Th.Accent, textColor: Th.Accent));
        _stack.Add(U.Card(people, new Thickness(12, 2, 4, 2)));
        _stack.Add(U.Caption("Can view: see tasks. Can edit: add, change and finish tasks. Can edit and share: also share the list with others.", 12));

        _stack.Add(U.Section("Teams"));
        var teams = new VerticalStackLayout { Spacing = 0 };
        foreach (var t in s.Teams)
        {
            var g = new Grid { ColumnDefinitions = [new(44), new(GridLength.Star), new(GridLength.Auto)], MinimumHeightRequest = 52, Padding = new Thickness(0, 0, 8, 0) };
            g.Add(U.Glyph(IconChars.Team, 22, Th.Accent), 0);
            var col = new VerticalStackLayout { VerticalOptions = LayoutOptions.Center };
            col.Add(U.Text(t.Name));
            col.Add(U.Caption(Permissions.Describe(t.Permission)));
            g.Add(col, 1);
            g.Add(U.IconButton(IconChars.Dismiss, () => RemoveTeamAsync(svc, t), $"Stop sharing with {t.Name}", Th.Text2, 16), 2);
            teams.Add(g);
            teams.Add(U.Divider(44));
        }
        teams.Add(U.Row(IconChars.Team, "Share with a team", () => AddTeamAsync(svc, s), glyphColor: Th.Accent, textColor: Th.Accent));
        _stack.Add(U.Card(teams, new Thickness(0, 2)));

        _stack.Add(U.Section("Public links"));
        var links = new VerticalStackLayout { Spacing = 0 };
        foreach (var l in s.Links)
        {
            var url = DocketService.LinkShareUrl(_session.ServerUrl ?? "", l);
            var g = new Grid { ColumnDefinitions = [new(44), new(GridLength.Star), new(44), new(44), new(44)], MinimumHeightRequest = 60 };
            g.Add(U.Glyph(l.SharingType == 2 ? IconChars.Lock : IconChars.Link, 20, Th.Accent), 0);
            var col = new VerticalStackLayout { VerticalOptions = LayoutOptions.Center, Padding = new Thickness(0, 6) };
            col.Add(U.Text(string.IsNullOrWhiteSpace(l.Name) ? "Public link" : l.Name));
            col.Add(U.Caption(Permissions.Describe(l.Permission) + (l.SharingType == 2 ? "  ·  password" : "")));
            var link = U.Caption(url, 11);
            link.LineBreakMode = LineBreakMode.MiddleTruncation;
            link.MaxLines = 1;
            col.Add(link);
            g.Add(col, 1);
            g.Add(U.IconButton(IconChars.Copy, async () =>
            {
                await Clipboard.Default.SetTextAsync(url);
                HapticFeedback.Default.Perform(HapticFeedbackType.Click);
                await DisplayAlertAsync("Link copied", "Anyone with this link can open the list.", "OK");
            }, "Copy link", Th.Text, 18), 2);
            g.Add(U.IconButton(IconChars.Share, () => Share.Default.RequestAsync(new ShareTextRequest { Title = _project?.Title, Text = url, Uri = url }), "Share link", Th.Text, 18), 3);
            g.Add(U.IconButton(IconChars.Delete, () => DeleteLinkAsync(svc, l), "Delete link", Th.Danger, 18), 4);
            links.Add(g);
            links.Add(U.Divider(44));
        }
        links.Add(U.Row(IconChars.Link, "Create a link", () => CreateLinkAsync(svc), glyphColor: Th.Accent, textColor: Th.Accent));
        _stack.Add(U.Card(links, new Thickness(0, 2)));
        _stack.Add(U.Caption("Anyone with a public link can open this list without an account. Add a password to limit who can.", 12));
    }

    private static List<Choice> PermissionChoices(SharePermission? current) =>
        Levels.Select(p => new Choice(Permissions.Describe(p), p switch { SharePermission.Read => "", SharePermission.ReadWrite => IconChars.Rename, _ => IconChars.Share }, Selected: p == current)).ToList();

    private async Task<SharePermission?> PickPermissionAsync(string title, SharePermission? current = SharePermission.ReadWrite)
    {
        var i = await ChoiceSheet.PickAsync(title, PermissionChoices(current));
        return i < 0 ? null : Levels[i];
    }

    private async Task PersonMenuAsync(DocketService svc, VProjectUser u)
    {
        var choices = PermissionChoices(u.Permission);
        choices.Add(new Choice("Stop sharing", IconChars.Dismiss, Danger: true));
        var i = await ChoiceSheet.PickAsync(string.IsNullOrWhiteSpace(u.Name) ? u.Username : u.Name, choices);
        if (i < 0) return;
        await U.Guard(async () =>
        {
            if (i == 3) await svc.Client.UnshareUserAsync(_id, u.Id);
            else if (Levels[i] != u.Permission) await svc.Client.SetUserPermissionAsync(_id, u.Id, Levels[i]);
            await LoadAsync();
        });
    }

    private async Task AddPersonAsync(DocketService svc, Sharing s)
    {
        var me = await svc.MeAsync();
        var candidates = (await svc.PeopleAsync()).Where(p => p.Id != me.Id && s.People.All(x => x.Id != p.Id)).ToList();
        var choices = candidates.Select(p => new Choice(DocketService.DisplayName(p), IconChars.Person, "@" + p.Username)).Append(new Choice("Type a username or email", IconChars.Rename)).ToList();
        var pick = await ChoiceSheet.PickAsync("Share with", choices, "They need an account on your Docket server. Add people from Docket on your PC.");
        if (pick < 0) return;
        var who = pick < candidates.Count ? candidates[pick].Username : await DisplayPromptAsync("Share with", "Username or email", "Next", "Cancel", keyboard: Keyboard.Email);
        if (string.IsNullOrWhiteSpace(who)) return;
        if (await PickPermissionAsync($"What can {who.Trim()} do?") is not { } perm) return;
        await U.Guard(async () => { await svc.ShareAsync(_id, who, perm); await LoadAsync(); });
    }

    private async Task AddTeamAsync(DocketService svc, Sharing s)
    {
        var teams = (await svc.Client.GetTeamsAsync()).Where(t => s.Teams.All(x => x.Id != t.Id)).ToList();
        if (teams.Count == 0)
        {
            if (await DisplayAlertAsync("No teams to add", "Make a team first on the People screen.", "Open People", "OK")) await Shell.Current.GoToAsync("people");
            return;
        }
        var i = await ChoiceSheet.PickAsync("Share with a team", teams.Select(t => new Choice(t.Name, IconChars.Team, $"{t.Members?.Count ?? 0} members")).ToList());
        if (i < 0) return;
        if (await PickPermissionAsync($"What can {teams[i].Name} do?") is not { } perm) return;
        await U.Guard(async () => { await svc.Client.ShareWithTeamAsync(_id, teams[i].Id, perm); await LoadAsync(); });
    }

    private async Task RemoveTeamAsync(DocketService svc, VTeam t)
    {
        if (!await DisplayAlertAsync("Stop sharing?", $"{t.Name} will no longer see this list.", "Stop sharing", "Cancel")) return;
        await svc.Client.UnshareTeamAsync(_id, t.Id);
        await LoadAsync();
    }

    private async Task CreateLinkAsync(DocketService svc)
    {
        var form = await Sheet<LinkForm>.ShowAsync(new LinkSheet());
        if (form is null) return;
        var share = await svc.Client.CreateLinkShareAsync(_id, form.Name, form.Permission, form.Password);
        await LoadAsync();
        if (share is not null)
        {
            var url = DocketService.LinkShareUrl(_session.ServerUrl ?? "", share);
            await Share.Default.RequestAsync(new ShareTextRequest { Title = _project?.Title, Text = url, Uri = url });
        }
    }

    private async Task DeleteLinkAsync(DocketService svc, VLinkShare l)
    {
        if (!await DisplayAlertAsync("Delete link?", "The link stops working for everyone who has it.", "Delete", "Cancel")) return;
        await svc.Client.DeleteLinkShareAsync(_id, l.Id);
        await LoadAsync();
    }
}

public sealed record LinkForm(string Name, SharePermission Permission, string? Password);

/// <summary>Name, permission and optional password for a new public link.</summary>
public sealed class LinkSheet : Sheet<LinkForm>
{
    public LinkSheet() : base("Create a public link")
    {
        var name = U.Entry("Name (optional), e.g. “For the plumber”");
        Body.Add(U.Field(name));
        var perm = SharePermission.Read;
        var pills = new HorizontalStackLayout { Spacing = 8, Margin = new Thickness(0, 4) };
        void Draw()
        {
            pills.Clear();
            foreach (var p in new[] { SharePermission.Read, SharePermission.ReadWrite, SharePermission.Admin })
                pills.Add(U.Pill(Permissions.Describe(p), p == perm, () => { perm = p; Draw(); return Task.CompletedTask; }));
        }
        Draw();
        Body.Add(new ScrollView { Orientation = ScrollOrientation.Horizontal, Content = pills, HorizontalScrollBarVisibility = ScrollBarVisibility.Never });
        var password = U.Entry("Password (optional)", password: true);
        Body.Add(U.Field(password));
        Body.Add(U.Caption("With a password, people need both the link and the password.", 12));
        var buttons = new HorizontalStackLayout { Spacing = 8, Margin = new Thickness(0, 8, 0, 0) };
        buttons.Add(U.Accent("Create link", () => { Close(new LinkForm(name.Text?.Trim() ?? "", perm, string.IsNullOrEmpty(password.Text) ? null : password.Text)); return Task.CompletedTask; }));
        buttons.Add(U.Plain("Cancel", () => { Close(null); return Task.CompletedTask; }));
        Body.Add(buttons);
    }
}
