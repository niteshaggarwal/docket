using Docket.Core;
using Docket.Mobile.Services;
using Docket.Mobile.ViewModels;
using Microsoft.Maui.Controls.Shapes;
using Icons = Docket.Mobile.Services.Icons;

namespace Docket.Mobile.Views;

/// <summary>A list's icon, colour and background (none, colour, gradient, accent or a photo). Every Docket app shows the same look.</summary>
public sealed class ListStylePage : ContentPage, IQueryAttributable
{
    private readonly Session _session;
    private readonly VerticalStackLayout _stack = new() { Padding = new Thickness(12, 4, 12, 24), Spacing = 8 };
    private long _id;
    private VProject? _project;
    private ListStyle _style = new();
    private byte[]? _image;
    private bool _busy;

    public ListStylePage(Session session)
    {
        _session = session;
        Content = new ScrollView { Content = _stack };
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

    private async Task LoadAsync()
    {
        if (_session.Service is not { } svc) return;
        try
        {
            _project = (await svc.ProjectsAsync(true)).FirstOrDefault(p => p.Id == _id);
            _style = ListStyle.Parse(_project?.Description);
            _image = _style.Background == "image" ? await svc.Client.GetBackgroundAsync(_id) : null;
            Render();
        }
        catch (Exception ex) when (U.IsExpected(ex)) { _stack.Clear(); _stack.Add(U.Text(U.Friendly(ex), 15, false, Th.Danger)); }
    }

    private void Render()
    {
        _stack.Clear();
        _stack.Add(U.Title("Icon and background"));
        _stack.Add(Preview());

        var tint = Th.Hex(_project?.HexColor, Th.Accent.Now);
        _stack.Add(U.Section("Icon"));
        var iconCard = new VerticalStackLayout { Spacing = 0 };
        var iconRow = new Grid { ColumnDefinitions = [new(44), new(GridLength.Star), new(GridLength.Auto)], MinimumHeightRequest = 52, Padding = new Thickness(0, 0, 12, 0) };
        iconRow.Add(U.ListIcon(_style.Icon, 22, new Duo(tint, tint)), 0);
        var name = _style.Icon is null ? "Default list icon" : Icons.IsEmoji(_style.Icon) ? "Emoji" : _style.Icon;
        var l = U.Text(name);
        l.VerticalTextAlignment = TextAlignment.Center;
        iconRow.Add(l, 1);
        var change1 = U.Text("Change", 14, true, Th.Accent);
        change1.VerticalOptions = LayoutOptions.Center;
        iconRow.Add(change1, 2);
        iconCard.Add(iconRow.Tap(PickIconAsync));
        iconCard.Add(U.Divider(44));
        var colorRow = new Grid { ColumnDefinitions = [new(44), new(GridLength.Star), new(GridLength.Auto)], MinimumHeightRequest = 52, Padding = new Thickness(0, 0, 12, 0) };
        colorRow.Add(new Ellipse { WidthRequest = 18, HeightRequest = 18, Fill = new SolidColorBrush(tint), HorizontalOptions = LayoutOptions.Center, VerticalOptions = LayoutOptions.Center }, 0);
        var cl = U.Text(_project?.HexColor is { Length: 6 } ? "List colour" : "List colour (accent)");
        cl.VerticalTextAlignment = TextAlignment.Center;
        colorRow.Add(cl, 1);
        var change2 = U.Text("Change", 14, true, Th.Accent);
        change2.VerticalOptions = LayoutOptions.Center;
        colorRow.Add(change2, 2);
        iconCard.Add(colorRow.Tap(PickColorAsync));
        _stack.Add(U.Card(iconCard, new Thickness(0, 0)));

        _stack.Add(U.Section("Background"));
        var tiles = new FlexLayout { Wrap = Microsoft.Maui.Layouts.FlexWrap.Wrap };
        tiles.Add(Tile("None", null, _style.Background == "none", () => SetBackgroundAsync("none", null), IconChars.Dismiss));
        tiles.Add(Tile("Accent", Icons.BackgroundBrush(new ListStyle { Background = "accent" }), _style.Background == "accent", () => SetBackgroundAsync("accent", "blue")));
        tiles.Add(Tile("Photo", _image is null ? null : null, _style.Background == "image", PickPhotoAsync, IconChars.Image, _image));
        foreach (var (gname, value) in Icons.Gradients)
            tiles.Add(Tile(gname, Icons.BackgroundBrush(new ListStyle { Background = "gradient", BackgroundValue = value }), _style.Background == "gradient" && _style.BackgroundValue == value, () => SetBackgroundAsync("gradient", value)));
        foreach (var (cname, hex) in Icons.Colors)
            tiles.Add(Tile(cname, new SolidColorBrush(Color.FromArgb(hex)), _style.Background == "color" && string.Equals(_style.BackgroundValue, hex, StringComparison.OrdinalIgnoreCase), () => SetBackgroundAsync("color", hex)));
        _stack.Add(U.Card(tiles, new Thickness(6)));
        _stack.Add(U.Caption("Photos go to your Docket server, so the list looks the same on your PC and on anyone's phone it's shared with. Text on a background gets a soft shade so it stays readable.", 12));
    }

    private View Preview()
    {
        var grid = new Grid { HeightRequest = 132 };
        var brush = Icons.BackgroundBrush(_style);
        var hasBg = brush is not null || _image is not null;
        if (brush is not null) grid.Add(new BoxView { Background = brush });
        if (_image is { } bytes) grid.Add(new Image { Source = ImageSource.FromStream(() => new MemoryStream(bytes)), Aspect = Aspect.AspectFill });
        if (hasBg) grid.Add(new BoxView { Background = new LinearGradientBrush([new GradientStop(Color.FromArgb("#8C000000"), 0), new GradientStop(Color.FromArgb("#26000000"), 1)], new Point(0, 0), new Point(0, 1)) });
        else grid.Themed(VisualElement.BackgroundColorProperty, Th.Bg);
        var tint = Th.Hex(_project?.HexColor, Th.Accent.Now);
        var row = new HorizontalStackLayout { Spacing = 10, Padding = new Thickness(16, 18), VerticalOptions = LayoutOptions.Start };
        var iconColor = hasBg ? Colors.White : tint;
        row.Add(U.ListIcon(_style.Icon, 26, new Duo(iconColor, iconColor)));
        var title = new Label { Text = _project?.Title ?? "List", FontFamily = "SelawikSemibold", FontSize = 26, VerticalTextAlignment = TextAlignment.Center };
        if (hasBg) title.TextColor = Colors.White; else title.Themed(Label.TextColorProperty, Th.Text);
        row.Add(title);
        grid.Add(row);
        var sample = U.Card(new HorizontalStackLayout { Spacing = 10, Children = { new FluentCheck(), new Label { Text = "A task in this list", VerticalTextAlignment = TextAlignment.Center }.Themed(Label.TextColorProperty, Th.Text) } }, new Thickness(4, 2));
        sample.Margin = new Thickness(12, 0, 12, 12);
        sample.VerticalOptions = LayoutOptions.End;
        grid.Add(sample);
        return new Border { Content = grid, StrokeThickness = 1, StrokeShape = new RoundRectangle { CornerRadius = 10 }, Padding = 0 }.Themed(Border.StrokeProperty, Th.Stroke);
    }

    private static View Tile(string name, Brush? brush, bool selected, Func<Task> tap, string? glyph = null, byte[]? image = null)
    {
        var g = new Grid { WidthRequest = 72, HeightRequest = 72 };
        if (brush is not null) g.Add(new BoxView { Background = brush });
        else g.Themed(VisualElement.BackgroundColorProperty, Th.Subtle);
        if (image is { } bytes) g.Add(new Image { Source = ImageSource.FromStream(() => new MemoryStream(bytes)), Aspect = Aspect.AspectFill });
        if (glyph is not null && image is null) g.Add(U.Glyph(glyph, 22, Th.Text2));
        var cap = new Label { Text = name, FontSize = 10, FontFamily = "SelawikSemibold", HorizontalTextAlignment = TextAlignment.Center, VerticalOptions = LayoutOptions.End, Margin = new Thickness(0, 0, 0, 4) };
        if (brush is not null || image is not null) { cap.TextColor = Colors.White; cap.Shadow = new Shadow { Brush = Colors.Black, Opacity = 0.55f, Radius = 3, Offset = new Point(0, 1) }; }
        else cap.Themed(Label.TextColorProperty, Th.Text2);
        g.Add(cap);
        if (selected)
            g.Add(new Label { Text = IconChars.Check, FontFamily = U.Icons, FontSize = 18, TextColor = Colors.White, HorizontalOptions = LayoutOptions.End, VerticalOptions = LayoutOptions.Start, Margin = new Thickness(0, 4, 6, 0), Shadow = new Shadow { Brush = Colors.Black, Opacity = 0.6f, Radius = 4 } });
        var b = new Border { Content = g, Margin = 4, Padding = 0, StrokeThickness = selected ? 3 : 0, StrokeShape = new RoundRectangle { CornerRadius = 8 } }.Themed(Border.StrokeProperty, Th.Accent);
        SemanticProperties.SetDescription(b, name);
        return b.Tap(tap);
    }

    private async Task SaveAsync(Func<ListStyle, ListStyle> change)
    {
        if (_session.Service is not { } svc || _busy) return;
        _busy = true;
        try { _style = await svc.SetListStyleAsync(_id, change); await LoadAsync(); }
        finally { _busy = false; }
    }

    private async Task PickIconAsync()
    {
        var icon = await Sheet<string>.ShowAsync(new IconSheet("List icon", _style.Icon));
        if (icon is not null) await SaveAsync(s => s with { Icon = icon.Length == 0 ? null : icon });
    }

    private async Task PickColorAsync()
    {
        var hex = await Sheet<string>.ShowAsync(new ColorSheet("List colour", _project?.HexColor));
        if (hex is null || _session.Service is not { } svc) return;
        await svc.RenameListAsync(_id, hexColor: hex);
        await LoadAsync();
    }

    private async Task SetBackgroundAsync(string kind, string? value)
    {
        if (_session.Service is { } svc && _style.Background == "image" && kind != "image")
        {
            try { await svc.Client.RemoveBackgroundAsync(_id); } catch (Exception ex) when (U.IsExpected(ex)) { }
            ListViewModel.ForgetBackground(_id);
        }
        await SaveAsync(s => s with { Background = kind, BackgroundValue = value });
    }

    private async Task PickPhotoAsync()
    {
        if (_session.Service is not { } svc) return;
        var file = await FilePicker.Default.PickAsync(new PickOptions { PickerTitle = "Pick a background photo", FileTypes = FilePickerFileType.Images });
        if (file is null) return;
        var path = file.FullPath;
        if (string.IsNullOrEmpty(path) || !File.Exists(path))
        {
            path = System.IO.Path.Combine(FileSystem.CacheDirectory, "bg_" + System.IO.Path.GetFileName(file.FileName));
            await using var src = await file.OpenReadAsync();
            await using var dst = File.Create(path);
            await src.CopyToAsync(dst);
        }
        _busy = true;
        try { await svc.Client.UploadBackgroundAsync(_id, path); }
        finally { _busy = false; }
        ListViewModel.ForgetBackground(_id);
        await SaveAsync(s => s with { Background = "image", BackgroundValue = "uploaded" });
    }
}
