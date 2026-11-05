using System.Globalization;
using Docket.App.Services;
using Docket.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.ApplicationModel.DataTransfer;

namespace Docket.App.Controls;

/// <summary>
/// The "List options" menu shared by every view of a list (list, table, Gantt, board):
/// rename, icon, background, colour, group, share, show completed, copy, delete.
/// </summary>
public sealed class ListOptionsMenu
{
    private readonly FrameworkElement _owner;
    private readonly Func<VProject?> _project;
    private readonly Func<string> _settingsKey;
    private readonly Func<string> _copyText;
    private readonly Func<Task> _changed;

    public ListOptionsMenu(FrameworkElement owner, Func<VProject?> project, Func<string> settingsKey, Func<string> copyText, Func<Task> changed)
    {
        _owner = owner; _project = project; _settingsKey = settingsKey; _copyText = copyText; _changed = changed;
    }

    public bool ShowCompletedOption { get; set; } = true;

    private static DocketService? Svc => App.Service;
    private static MainWindow? Win => App.Window;

    /// <summary>Fills the menu each time it opens, so it reflects the current list.</summary>
    public void Fill(MenuFlyout menu)
    {
        menu.Items.Clear();
        var p = _project();
        var isDefault = p is not null && p.Id == Win?.DefaultProject?.Id;
        if (p is not null)
        {
            menu.Items.Add(Item("Rename list", "", RenameAsync));
            menu.Items.Add(Item("Icon…", "", IconAsync));
            menu.Items.Add(Item("Background…", "", () => BackgroundDialog.ShowAsync(_owner.XamlRoot, p, Changed)));
            var color = new MenuFlyoutSubItem { Text = "Colour", Icon = new FontIcon { Glyph = "" } };
            var def = new MenuFlyoutItem { Text = "Default", Icon = new FontIcon { Glyph = "", Foreground = Ui.Hex("8a8886") } };
            def.Click += async (_, _) => await SetColorAsync("");
            color.Items.Add(def);
            foreach (var (name, hex) in Ui.Palette)
            {
                var item = new MenuFlyoutItem { Text = name, Icon = new FontIcon { Glyph = "", Foreground = Ui.Hex(hex) } };
                item.Click += async (_, _) => await SetColorAsync(hex);
                color.Items.Add(item);
            }
            menu.Items.Add(color);
            if (!isDefault)
            {
                var group = new MenuFlyoutSubItem { Text = "Move to group", Icon = new FontIcon { Glyph = "" } };
                var none = new MenuFlyoutItem { Text = "No group" };
                none.Click += async (_, _) => await MoveToGroupAsync(0);
                group.Items.Add(none);
                foreach (var g in (Win?.Projects ?? []).Where(x => x.ParentProjectId == 0 && x.Id != p.Id && x.Id != Win?.DefaultProject?.Id))
                {
                    var item = new MenuFlyoutItem { Text = g.Title };
                    item.Click += async (_, _) => await MoveToGroupAsync(g.Id);
                    group.Items.Add(item);
                }
                menu.Items.Add(group);
            }
            menu.Items.Add(new MenuFlyoutSeparator());
            menu.Items.Add(Item("Share list…", "", () => ShareDialog.ShowAsync(_owner.XamlRoot, p, Changed)));
        }
        if (ShowCompletedOption)
        {
            var show = new ToggleMenuFlyoutItem { Text = "Show completed tasks", IsChecked = !App.Settings.HideCompleted.Contains(_settingsKey()) };
            show.Click += async (_, _) =>
            {
                if (show.IsChecked) App.Settings.HideCompleted.Remove(_settingsKey()); else App.Settings.HideCompleted.Add(_settingsKey());
                App.Settings.Save();
                await _changed();
            };
            menu.Items.Add(show);
        }
        menu.Items.Add(Item("Copy list", "", () =>
        {
            var pkg = new DataPackage();
            pkg.SetText(_copyText());
            Clipboard.SetContent(pkg);
            Win?.SetStatus("List copied");
            return Task.CompletedTask;
        }));
        if (p is not null && !isDefault)
        {
            menu.Items.Add(new MenuFlyoutSeparator());
            menu.Items.Add(Item("Delete list", "", DeleteAsync));
        }
    }

    private static MenuFlyoutItem Item(string text, string glyph, Func<Task> action)
    {
        var i = new MenuFlyoutItem { Text = text, Icon = new FontIcon { Glyph = glyph } };
        i.Click += async (_, _) => await action();
        return i;
    }

    private async Task Changed()
    {
        if (Win is not null) await Win.LoadListsAsync();
        await _changed();
    }

    public async Task RenameAsync()
    {
        if (_project() is not { } p || Svc is not { } svc || Win is null) return;
        var name = await Dialogs.PromptAsync(_owner.XamlRoot, "Rename list", "List name", p.Title, "Rename");
        if (string.IsNullOrWhiteSpace(name)) return;
        await Win.RunAsync("Couldn't rename the list", async () => { await svc.RenameListAsync(p.Id, title: name); await Changed(); });
    }

    private async Task IconAsync()
    {
        if (_project() is not { } p || Svc is not { } svc || Win is null) return;
        var current = ListStyle.Parse(p.Description).Icon;
        var pick = await Pickers.IconAsync(_owner.XamlRoot, "List icon", current, Ui.IsHex(p.HexColor) ? p.HexColor : null);
        if (!pick.Ok) return;
        await Win.RunAsync("Couldn't change the icon", async () => { await svc.SetListStyleAsync(p.Id, s => s with { Icon = pick.Value }); await Changed(); });
    }

    private async Task SetColorAsync(string hex)
    {
        if (_project() is not { } p || Svc is not { } svc || Win is null) return;
        await Win.RunAsync("Couldn't change the colour", async () => { await svc.RenameListAsync(p.Id, hexColor: hex); await Changed(); });
    }

    private async Task MoveToGroupAsync(long parentId)
    {
        if (_project() is not { } p || Svc is not { } svc || Win is null) return;
        await Win.RunAsync("Couldn't move the list", async () => { await svc.RenameListAsync(p.Id, parentId: parentId); await Changed(); });
    }

    private async Task DeleteAsync()
    {
        if (_project() is not { } p || Svc is not { } svc || Win is null) return;
        if (!await Dialogs.ConfirmAsync(_owner.XamlRoot, "Delete list?", $"\"{p.Title}\" and all its tasks will be permanently deleted.")) return;
        await Win.RunAsync("Couldn't delete the list", async () =>
        {
            await svc.DeleteListAsync(p.Id);
            await Win.LoadListsAsync();
            Win.SelectList(Win.DefaultProject?.Id ?? 0);
        });
    }

    public static string Describe(long id) => id.ToString(CultureInfo.InvariantCulture);
}
