using Docket.App.Services;
using Docket.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Windows.ApplicationModel.DataTransfer;

namespace Docket.App.Controls;

/// <summary>Share a list: with people (by account or email), with teams, and through public links.</summary>
public static class ShareDialog
{
    private static readonly SharePermission[] Levels = [SharePermission.Read, SharePermission.ReadWrite, SharePermission.Admin];

    private static ComboBox PermissionBox(SharePermission current, string name)
    {
        var box = new ComboBox { MinWidth = 170, VerticalAlignment = VerticalAlignment.Center };
        foreach (var l in Levels) box.Items.Add(Permissions.Describe(l));
        box.SelectedIndex = Array.IndexOf(Levels, current);
        AutomationProperties.SetName(box, name);
        return box;
    }

    public static async Task ShowAsync(XamlRoot root, VProject project, Func<Task> changed)
    {
        if (App.Service is not { } svc || App.Window is not { } win) return;
        var status = new InfoBar { IsClosable = true, IsOpen = false };
        var people = new StackPanel { Spacing = 4 };
        var teams = new StackPanel { Spacing = 4 };
        var links = new StackPanel { Spacing = 4 };
        var anyChange = false;

        void Fail(Exception ex) { status.Severity = InfoBarSeverity.Error; status.Title = "Couldn't change sharing"; status.Message = ex.Message; status.IsOpen = true; }
        void Note(string title, string msg) { status.Severity = InfoBarSeverity.Success; status.Title = title; status.Message = msg; status.IsOpen = true; }

        async Task Do(Func<Task> action, string? done = null)
        {
            try { await action(); anyChange = true; if (done is not null) Note(done, ""); await Reload(); }
            catch (Exception ex) when (ex is HttpRequestException or VikunjaException or DocketException or TaskCanceledException) { Fail(ex); }
        }

        Grid Row(FrameworkElement lead, string title, string? sub, params FrameworkElement[] trailing)
        {
            var g = new Grid { ColumnSpacing = 12, Padding = new Thickness(4, 6, 0, 6) };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            text.Children.Add(Ui.Text(title, "BodyTextBlockStyle"));
            if (sub is not null) text.Children.Add(Ui.Text(sub, "CaptionTextBlockStyle", 0.65));
            Grid.SetColumn(text, 1);
            g.Children.Add(lead);
            g.Children.Add(text);
            var col = 2;
            foreach (var t in trailing)
            {
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                Grid.SetColumn(t, col++);
                g.Children.Add(t);
            }
            return g;
        }

        async Task Reload()
        {
            var sharing = await svc.SharingAsync(project.Id);
            people.Children.Clear();
            var me = await svc.MeAsync();
            people.Children.Add(Row(Ui.Avatar(me, 32), DocketService.DisplayName(me) + " (you)", "@" + me.Username));
            foreach (var u in sharing.People)
            {
                var user = new VUser { Id = u.Id, Username = u.Username, Name = u.Name };
                var perm = PermissionBox(u.Permission, $"Permission for {DocketService.DisplayName(user)}");
                perm.SelectionChanged += async (_, _) => await Do(() => svc.Client.SetUserPermissionAsync(project.Id, u.Id, Levels[perm.SelectedIndex]));
                var remove = Ui.IconButton("", $"Stop sharing with {DocketService.DisplayName(user)}", async (_, _) => await Do(() => svc.Client.UnshareUserAsync(project.Id, u.Id)));
                people.Children.Add(Row(Ui.Avatar(user, 32), DocketService.DisplayName(user), "@" + u.Username, perm, remove));
            }
            teams.Children.Clear();
            foreach (var t in sharing.Teams)
            {
                var badge = new Border { Width = 32, Height = 32, CornerRadius = new CornerRadius(6), Background = Ui.Brush("SubtleFillColorSecondaryBrush"), Child = Ui.Glyph("", 14) };
                var remove = Ui.IconButton("", $"Stop sharing with {t.Name}", async (_, _) => await Do(() => svc.Client.UnshareTeamAsync(project.Id, t.Id)));
                var teamPermBox = PermissionBox(t.Permission, $"Permission for {t.Name}");
                teamPermBox.SelectionChanged += async (_, _) => await Do(() => svc.Client.SetTeamPermissionAsync(project.Id, t.Id, Levels[teamPermBox.SelectedIndex]));
                teams.Children.Add(Row(badge, t.Name, "Team", teamPermBox, remove));
            }
            if (sharing.Teams.Count == 0) teams.Children.Add(Ui.Text("Not shared with a team.", "CaptionTextBlockStyle", 0.6));
            links.Children.Clear();
            foreach (var l in sharing.Links)
            {
                var url = DocketService.LinkShareUrl(App.Settings.ServerUrl, l);
                var icon = new Border { Width = 32, Height = 32, CornerRadius = new CornerRadius(6), Background = Ui.Brush("SubtleFillColorSecondaryBrush"), Child = Ui.Glyph(l.SharingType == 2 ? "" : "", 14) };
                var copy = new Button { Content = "Copy link", VerticalAlignment = VerticalAlignment.Center };
                copy.Click += (_, _) =>
                {
                    var pkg = new DataPackage(); pkg.SetText(url); Clipboard.SetContent(pkg);
                    Note("Link copied", url);
                };
                var del = Ui.IconButton("", $"Delete link {l.Name}", async (_, _) => await Do(() => svc.Client.DeleteLinkShareAsync(project.Id, l.Id)));
                var name = string.IsNullOrWhiteSpace(l.Name) ? "Public link" : l.Name;
                links.Children.Add(Row(icon, name, $"{Permissions.Describe(l.Permission)}{(l.SharingType == 2 ? " · password" : "")}", copy, del));
            }
            if (sharing.Links.Count == 0) links.Children.Add(Ui.Text("No public links. Anyone with a link can open the list without an account.", "CaptionTextBlockStyle", 0.6, TextWrapping.Wrap));
        }

        // ---- add a person ----
        var who = new AutoSuggestBox { PlaceholderText = "Username or email", MinWidth = 220 };
        AutomationProperties.SetName(who, "Username or email to share with");
        var known = new List<VUser>();
        try { known = await svc.PeopleAsync(); } catch (Exception ex) when (ex is HttpRequestException or VikunjaException) { }
        who.TextChanged += (s, e) =>
        {
            if (e.Reason != AutoSuggestionBoxTextChangeReason.UserInput) return;
            var q = s.Text.Trim();
            s.ItemsSource = q.Length == 0 ? null : known.Where(u => u.Username.Contains(q, StringComparison.OrdinalIgnoreCase) || u.Name.Contains(q, StringComparison.OrdinalIgnoreCase)).Select(u => u.Username).ToList();
        };
        var whoPerm = PermissionBox(SharePermission.ReadWrite, "Permission for the new person");
        var share = new Button { Content = "Share", Style = Ui.Res<Style>("AccentButtonStyle") };
        share.Click += async (_, _) =>
        {
            if (who.Text.Trim().Length == 0) return;
            var name = who.Text.Trim();
            await Do(() => svc.ShareAsync(project.Id, name, Levels[whoPerm.SelectedIndex]), $"Shared with {name}");
            who.Text = "";
        };

        // ---- add a team ----
        var teamPick = new ComboBox { PlaceholderText = "Choose a team", MinWidth = 220 };
        AutomationProperties.SetName(teamPick, "Team to share with");
        var allTeams = new List<VTeam>();
        try { allTeams = await svc.Client.GetTeamsAsync(); } catch (Exception ex) when (ex is HttpRequestException or VikunjaException) { }
        foreach (var t in allTeams) teamPick.Items.Add(t.Name);
        var teamPerm = PermissionBox(SharePermission.ReadWrite, "Permission for the team");
        var shareTeam = new Button { Content = "Share", IsEnabled = allTeams.Count > 0 };
        shareTeam.Click += async (_, _) =>
        {
            if (teamPick.SelectedIndex < 0) return;
            var t = allTeams[teamPick.SelectedIndex];
            await Do(() => svc.Client.ShareWithTeamAsync(project.Id, t.Id, Levels[teamPerm.SelectedIndex]), $"Shared with {t.Name}");
        };

        // ---- create a link ----
        var linkName = new TextBox { PlaceholderText = "Link name (optional)", MinWidth = 220 };
        var linkPerm = PermissionBox(SharePermission.Read, "Permission for the link");
        var linkPass = new PasswordBox { PlaceholderText = "Password (optional)", MinWidth = 220 };
        var create = new Button { Content = "Create link" };
        create.Click += async (_, _) =>
        {
            var pw = linkPass.Password;
            var name = linkName.Text.Trim();
            await Do(async () =>
            {
                var created = await svc.Client.CreateLinkShareAsync(project.Id, name, Levels[linkPerm.SelectedIndex], pw.Length > 0 ? pw : null);
                if (created is not null)
                {
                    var pkg = new DataPackage(); pkg.SetText(DocketService.LinkShareUrl(App.Settings.ServerUrl, created)); Clipboard.SetContent(pkg);
                }
            }, "Link created and copied");
            linkName.Text = ""; linkPass.Password = "";
        };

        StackPanel Line(params FrameworkElement[] items)
        {
            var p = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            foreach (var i in items) p.Children.Add(i);
            return p;
        }

        TextBlock Head(string t) { var h = Ui.Text(t, "BodyStrongTextBlockStyle"); h.Margin = new Thickness(0, 10, 0, 0); return h; }

        var body = new StackPanel { Spacing = 8, Width = 560 };
        body.Children.Add(status);
        body.Children.Add(Head("People"));
        body.Children.Add(Line(who, whoPerm, share));
        body.Children.Add(people);
        body.Children.Add(Head("Teams"));
        body.Children.Add(allTeams.Count > 0 ? Line(teamPick, teamPerm, shareTeam) : Ui.Text("Create teams on the People page.", "CaptionTextBlockStyle", 0.6));
        body.Children.Add(teams);
        body.Children.Add(Head("Public links"));
        body.Children.Add(Line(linkName, linkPerm));
        body.Children.Add(Line(linkPass, create));
        body.Children.Add(links);

        try { await Reload(); }
        catch (Exception ex) when (ex is HttpRequestException or VikunjaException or DocketException) { Fail(ex); }

        var d = new ContentDialog
        {
            XamlRoot = root, Title = $"Share \"{project.Title}\"", Content = new ScrollViewer { Content = body, MaxHeight = 600, Padding = new Thickness(0, 0, 12, 0) },
            CloseButtonText = "Done", DefaultButton = ContentDialogButton.Close,
        };
        d.Resources["ContentDialogMaxWidth"] = 680.0;
        await d.ShowAsync();
        if (anyChange) await changed();
    }
}
