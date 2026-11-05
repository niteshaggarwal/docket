using Docket.Core;
using Docket.Mobile.Services;

namespace Docket.Mobile.Views;

/// <summary>People you work with and your teams. Accounts themselves are created on the PC.</summary>
public sealed class PeoplePage : ContentPage
{
    private readonly Session _session;
    private readonly VerticalStackLayout _stack = new() { Padding = new Thickness(12, 4, 12, 24), Spacing = 8 };
    private readonly RefreshView _refresh;

    public PeoplePage(Session session)
    {
        _session = session;
        _refresh = new RefreshView { Content = new ScrollView { Content = _stack } };
        _refresh.Command = new Command(async () => await LoadAsync());
        Content = _refresh;
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
        _stack.Clear();
        _stack.Add(U.Title("People"));
        try
        {
            var me = await svc.MeAsync();
            var people = await svc.PeopleAsync();
            List<VTeam>? teams = null;
            string? teamError = null;
            try { teams = await svc.Client.GetTeamsAsync(); }
            catch (Exception ex) when (U.IsExpected(ex)) { teamError = U.Friendly(ex); }
            Render(svc, me, people, teams, teamError);
        }
        catch (Exception ex) when (U.IsExpected(ex)) { _stack.Add(U.Text(U.Friendly(ex), 15, false, Th.Danger)); }
        finally { _refresh.IsRefreshing = false; _loading = false; }
    }

    private void Render(DocketService svc, VUser me, List<VUser> people, List<VTeam>? teams, string? teamError)
    {
        var info = new Grid { ColumnDefinitions = [new(36), new(GridLength.Star)], ColumnSpacing = 6 };
        var icon = U.Glyph(IconChars.PersonAdd, 22, Th.Accent);
        icon.VerticalOptions = LayoutOptions.Start;
        info.Add(icon, 0);
        var text = new VerticalStackLayout { Spacing = 4 };
        text.Add(U.Text("Add people from Docket on your PC", 15, true));
        text.Add(U.Caption("New accounts live on your Docket server, so they're created from the Windows app (Settings > People). Once someone has an account, you can share lists with them, put them in a team and assign them tasks from this phone.", 13));
        info.Add(text, 1);
        var infoCard = U.Card(info);
        infoCard.Themed(VisualElement.BackgroundColorProperty, Th.AccentTint);
        _stack.Add(infoCard);

        _stack.Add(U.Section($"People you work with  ·  {people.Count}"));
        var list = new VerticalStackLayout { Spacing = 0 };
        for (var i = 0; i < people.Count; i++)
        {
            var u = people[i];
            list.Add(PersonRow(u, u.Id == me.Id ? "You" : null));
            if (i < people.Count - 1) list.Add(U.Divider(52));
        }
        _stack.Add(U.Card(list, new Thickness(12, 4)));
        if (people.Count <= 1) _stack.Add(U.Caption("Share a list (List menu > Share list) and the people on it show up here.", 13));

        var teamHead = new Grid { ColumnDefinitions = [new(GridLength.Star), new(GridLength.Auto)], Margin = new Thickness(0, 10, 0, 0) };
        teamHead.Add(U.Section("Teams"), 0);
        var newTeam = U.Text("New team", 14, true, Th.Accent);
        newTeam.VerticalOptions = LayoutOptions.End;
        teamHead.Add(newTeam.Tap(() => NewTeamAsync(svc)), 1);
        _stack.Add(teamHead);
        if (teamError is not null) { _stack.Add(U.Text(teamError, 14, false, Th.Danger)); return; }
        if (teams is null || teams.Count == 0)
        {
            _stack.Add(U.Card(U.Caption("A team is a group of people you share lists with in one go, like “Family” or “Studio”.", 13)));
            return;
        }
        foreach (var t in teams) _stack.Add(TeamCard(svc, t, people));
    }

    private static Grid PersonRow(VUser u, string? badge)
    {
        var g = new Grid { ColumnDefinitions = [new(44), new(GridLength.Star), new(GridLength.Auto)], MinimumHeightRequest = 56 };
        var a = U.Avatar(u, 34);
        a.HorizontalOptions = LayoutOptions.Start;
        g.Add(a, 0);
        var col = new VerticalStackLayout { VerticalOptions = LayoutOptions.Center, Margin = new Thickness(6, 0, 0, 0) };
        col.Add(U.Text(DocketService.DisplayName(u)));
        col.Add(U.Caption("@" + u.Username));
        g.Add(col, 1);
        if (badge is not null) g.Add(U.Badge(badge, Th.Accent, 11), 2);
        return g;
    }

    private Border TeamCard(DocketService svc, VTeam team, List<VUser> people)
    {
        var stack = new VerticalStackLayout { Spacing = 2 };
        var head = new Grid { ColumnDefinitions = [new(36), new(GridLength.Star)] };
        head.Add(U.Glyph(IconChars.Team, 22, Th.Accent), 0);
        var t = new VerticalStackLayout { VerticalOptions = LayoutOptions.Center };
        t.Add(U.Text(team.Name, 15, true));
        var members = team.Members ?? [];
        t.Add(U.Caption(members.Count == 1 ? "1 member" : $"{members.Count} members"));
        head.Add(t, 1);
        stack.Add(head);
        var avatars = new FlexLayout { Wrap = Microsoft.Maui.Layouts.FlexWrap.Wrap, Margin = new Thickness(36, 6, 0, 4) };
        foreach (var m in members)
        {
            var chip = new HorizontalStackLayout { Spacing = 6, Margin = new Thickness(0, 0, 12, 6) };
            chip.Add(U.Avatar(m, 24));
            var n = U.Text(DocketService.DisplayName(m), 13);
            n.VerticalTextAlignment = TextAlignment.Center;
            chip.Add(n);
            avatars.Add(chip);
        }
        stack.Add(avatars);
        stack.Add(U.Row(IconChars.PersonAdd, "Add member", () => AddMemberAsync(svc, team, people), glyphColor: Th.Accent, textColor: Th.Accent));
        return U.Card(stack, new Thickness(12, 10, 12, 0));
    }

    private async Task NewTeamAsync(DocketService svc)
    {
        var name = await DisplayPromptAsync("New team", "Team name", "Create", "Cancel", "Family");
        if (string.IsNullOrWhiteSpace(name)) return;
        await U.Guard(async () => { await svc.Client.CreateTeamAsync(name.Trim()); await LoadAsync(); });
    }

    private async Task AddMemberAsync(DocketService svc, VTeam team, List<VUser> people)
    {
        var candidates = people.Where(p => team.Members?.Any(m => m.Id == p.Id) != true).ToList();
        var choices = candidates.Select(p => new Choice(DocketService.DisplayName(p), IconChars.Person, "@" + p.Username)).Append(new Choice("Type a username", IconChars.Rename)).ToList();
        var pick = await ChoiceSheet.PickAsync($"Add to {team.Name}", choices, "They need an account on your Docket server.");
        if (pick < 0) return;
        var username = pick < candidates.Count ? candidates[pick].Username : await DisplayPromptAsync("Add member", "Username", "Add", "Cancel", keyboard: Keyboard.Plain);
        if (string.IsNullOrWhiteSpace(username)) return;
        await U.Guard(async () => { await svc.Client.AddTeamMemberAsync(team.Id, username.Trim()); await LoadAsync(); });
    }
}
