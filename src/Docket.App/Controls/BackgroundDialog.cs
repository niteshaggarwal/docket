using System.Globalization;
using Docket.App.Services;
using Docket.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Storage.Pickers;

namespace Docket.App.Controls;

/// <summary>Picks a list background: none, a colour, a two-colour gradient, the Windows accent, or an image.</summary>
public static class BackgroundDialog
{
    public static async Task ShowAsync(XamlRoot root, VProject project, Func<Task> changed)
    {
        if (App.Service is not { } svc || App.Window is not { } win) return;
        var style = ListStyle.Parse(project.Description);
        var kind = style.Background;
        var color = kind == "color" ? style.BackgroundValue : "2564cf";
        var parts = (kind == "gradient" ? style.BackgroundValue ?? "" : "2564cf,7a4fd6,135").Split(',', StringSplitOptions.TrimEntries);
        var from = parts.ElementAtOrDefault(0) ?? "2564cf";
        var to = parts.ElementAtOrDefault(1) ?? "7a4fd6";
        var angle = double.TryParse(parts.ElementAtOrDefault(2), NumberStyles.Float, CultureInfo.InvariantCulture, out var a) ? a : 135;
        string? imagePath = null;
        BitmapImage? image = kind == "image" ? await Backgrounds.ImageAsync(project.Id) : null;

        var preview = new Border { Height = 120, CornerRadius = new CornerRadius(8), BorderBrush = Ui.Brush("CardStrokeColorDefaultBrush"), BorderThickness = new Thickness(1) };
        var previewLabel = new TextBlock { Text = project.Title, Style = Ui.TextStyle("SubtitleTextBlockStyle"), Margin = new Thickness(16), VerticalAlignment = VerticalAlignment.Bottom };
        preview.Child = previewLabel;

        var choice = new RadioButtons { Header = "Background", MaxColumns = 5 };
        foreach (var n in new[] { "None", "Colour", "Gradient", "Accent", "Image" }) choice.Items.Add(n);
        var keys = new[] { "none", "color", "gradient", "accent", "image" };
        choice.SelectedIndex = Math.Max(0, Array.IndexOf(keys, kind));

        var colorPanel = new StackPanel { Spacing = 8 };
        var gradientPanel = new StackPanel { Spacing = 8 };
        var imagePanel = new StackPanel { Spacing = 8 };
        var accentNote = Ui.Text("Uses your Windows accent colour, so it follows your theme.", "CaptionTextBlockStyle", 0.7, TextWrapping.Wrap);
        colorPanel.Children.Add(Pickers.Swatches(color, false, h => { color = h; Update(); }));

        gradientPanel.Children.Add(Ui.Text("From", "CaptionTextBlockStyle", 0.75));
        gradientPanel.Children.Add(Pickers.Swatches(from, false, h => { from = h ?? from; Update(); }));
        gradientPanel.Children.Add(Ui.Text("To", "CaptionTextBlockStyle", 0.75));
        gradientPanel.Children.Add(Pickers.Swatches(to, false, h => { to = h ?? to; Update(); }));
        var slider = new Slider { Header = "Angle", Minimum = 0, Maximum = 360, StepFrequency = 15, Value = angle, Width = 300, HorizontalAlignment = HorizontalAlignment.Left };
        slider.ValueChanged += (_, e) => { angle = e.NewValue; Update(); };
        gradientPanel.Children.Add(slider);

        var chooseImage = new Button { Content = "Choose image…" };
        var imageName = Ui.Text(image is null ? "No image chosen" : "Current image", "CaptionTextBlockStyle", 0.7);
        chooseImage.Click += async (_, _) =>
        {
            var picker = new FileOpenPicker { ViewMode = PickerViewMode.Thumbnail, SuggestedStartLocation = PickerLocationId.PicturesLibrary };
            WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(win));
            foreach (var ext in new[] { ".jpg", ".jpeg", ".png", ".webp" }) picker.FileTypeFilter.Add(ext);
            if (await picker.PickSingleFileAsync() is not { } file) return;
            imagePath = file.Path;
            imageName.Text = file.Name;
            image = new BitmapImage(new Uri(file.Path)) { DecodePixelWidth = 1200 };
            Update();
        };
        imagePanel.Children.Add(chooseImage);
        imagePanel.Children.Add(imageName);
        imagePanel.Children.Add(Ui.Text("The image is stored on your Docket server, so everyone the list is shared with sees it.", "CaptionTextBlockStyle", 0.6, TextWrapping.Wrap));


        void Update()
        {
            var k = keys[Math.Max(0, choice.SelectedIndex)];
            colorPanel.Visibility = k == "color" ? Visibility.Visible : Visibility.Collapsed;
            gradientPanel.Visibility = k == "gradient" ? Visibility.Visible : Visibility.Collapsed;
            imagePanel.Visibility = k == "image" ? Visibility.Visible : Visibility.Collapsed;
            accentNote.Visibility = k == "accent" ? Visibility.Visible : Visibility.Collapsed;
            var s = Make(k);
            preview.Background = Icons.Background(s, image) ?? Ui.Brush("LayerFillColorDefaultBrush");
            previewLabel.Foreground = k == "none" ? Ui.Brush("TextFillColorPrimaryBrush") : new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.White);
        }

        ListStyle Make(string k) => k switch
        {
            "color" => style with { Background = "color", BackgroundValue = color },
            "gradient" => style with { Background = "gradient", BackgroundValue = $"{from},{to},{angle.ToString("0", CultureInfo.InvariantCulture)}" },
            "accent" => style with { Background = "accent", BackgroundValue = "system" },
            "image" => style with { Background = "image", BackgroundValue = "uploaded" },
            _ => style with { Background = "none", BackgroundValue = null },
        };

        choice.SelectionChanged += (_, _) => Update();
        Update();

        var panel = new StackPanel { Spacing = 14, Width = 420 };
        panel.Children.Add(preview);
        panel.Children.Add(choice);
        panel.Children.Add(colorPanel);
        panel.Children.Add(gradientPanel);
        panel.Children.Add(accentNote);
        panel.Children.Add(imagePanel);
        var d = new ContentDialog
        {
            XamlRoot = root, Title = "List background", Content = new ScrollViewer { Content = panel, MaxHeight = 560 },
            PrimaryButtonText = "Save", CloseButtonText = "Cancel", DefaultButton = ContentDialogButton.Primary,
        };
        if (await d.ShowAsync() != ContentDialogResult.Primary) return;
        var k = keys[Math.Max(0, choice.SelectedIndex)];
        if (k == "image" && imagePath is null && image is null) { win.ShowError("Choose an image", "Pick a picture for the background, or choose another kind."); return; }
        await win.RunAsync("Couldn't change the background", async () =>
        {
            if (k == "image" && imagePath is not null) await svc.Client.UploadBackgroundAsync(project.Id, imagePath);
            if (kind == "image" && k != "image")
            {
                try { await svc.Client.RemoveBackgroundAsync(project.Id); } catch (VikunjaException) { /* keep the old file; the style no longer shows it */ }
            }
            Backgrounds.Forget(project.Id);
            await svc.SetListStyleAsync(project.Id, _ => Make(k));
            await changed();
        });
    }
}
