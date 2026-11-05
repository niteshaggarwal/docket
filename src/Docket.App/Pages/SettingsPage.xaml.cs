using System.Diagnostics;
using Docket.App.Services;
using Docket.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace Docket.App.Pages;

public sealed partial class SettingsPage : Page
{
    private static readonly string[] Providers = ["ollama", "anthropic", "openai", "gemini", "off"];
    private bool _loading;
    private CancellationTokenSource? _testCts;
    private List<string>? _installed;

    public SettingsPage() => InitializeComponent();

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        _loading = true;
        var s = App.Settings;
        ServerBox.Text = s.ServerUrl;
        TokenBox.PlaceholderText = s.Token is null ? "tk_..." : "Saved. Paste a new token to replace it.";
        var projects = App.Window?.Projects ?? [];
        DefaultProject.ItemsSource = projects;
        DefaultProject.SelectedItem = projects.FirstOrDefault(p => p.Id == (App.Window?.DefaultProject?.Id ?? s.DefaultProjectId));
        ConfirmDeleteSwitch.IsOn = s.ConfirmDelete;
        ShowImportantSwitch.IsOn = s.ShowImportant;
        ShowPlannedSwitch.IsOn = s.ShowPlanned;
        ShowAllSwitch.IsOn = s.ShowAll;
        ShowCompletedSwitch.IsOn = s.ShowCompletedList;
        HideEmptySwitch.IsOn = s.HideEmptySmartLists;
        SoundSwitch.IsOn = s.ReminderSound;
        ThemeChoice.SelectedIndex = s.Theme switch { "light" => 1, "dark" => 2, _ => 0 };
        ProviderChoice.SelectedIndex = Math.Max(0, Array.IndexOf(Providers, s.AiProvider));
        _loading = false;
        ShowProvider();
        await CheckOllamaAsync();
    }

    private async void General_Changed(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        var s = App.Settings;
        s.ConfirmDelete = ConfirmDeleteSwitch.IsOn;
        s.ShowImportant = ShowImportantSwitch.IsOn;
        s.ShowPlanned = ShowPlannedSwitch.IsOn;
        s.ShowAll = ShowAllSwitch.IsOn;
        s.ShowCompletedList = ShowCompletedSwitch.IsOn;
        s.HideEmptySmartLists = HideEmptySwitch.IsOn;
        s.ReminderSound = SoundSwitch.IsOn;
        s.Save();
        if (App.Window is not null) await App.Window.RefreshCountsAsync();
    }

    private void Theme_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_loading) return;
        App.Settings.Theme = ThemeChoice.SelectedIndex switch { 1 => "light", 2 => "dark", _ => "system" };
        App.Settings.Save();
        App.Window?.ApplyTheme();
    }

    // ---------- AI ----------
    private string Provider => Providers[Math.Max(0, ProviderChoice.SelectedIndex)];

    private async void Provider_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_loading) return;
        App.Settings.AiProvider = Provider;
        App.Settings.Save();
        Controls.AddBar.ResetAiLabel();
        ShowProvider();
        if (Provider == "ollama") await CheckOllamaAsync();
    }

    private void ShowProvider()
    {
        var p = Provider;
        LocalPanel.Visibility = p == "ollama" ? Visibility.Visible : Visibility.Collapsed;
        CloudPanel.Visibility = p is "anthropic" or "openai" or "gemini" ? Visibility.Visible : Visibility.Collapsed;
        TestButton.IsEnabled = p != "off";
        TestResult.IsOpen = false;
        if (CloudPanel.Visibility == Visibility.Visible)
        {
            CloudModelBox.Text = App.Settings.AiModels.GetValueOrDefault(p) is { Length: > 0 } m ? m : AiConfig.DefaultModel(p);
            var has = App.Settings.GetAiKey(p) is not null;
            KeyBox.Password = "";
            KeyBox.PlaceholderText = has ? "Saved. Paste a new key to replace it." : "Paste your API key";
            var where = p switch { "anthropic" => "console.anthropic.com", "openai" => "platform.openai.com", _ => "aistudio.google.com" };
            KeyNote.Text = has ? "A key is saved, encrypted for your Windows account." : $"Get a key at {where}. It's encrypted for your Windows account and stays on this PC.";
        }
    }

    private void CloudModel_LostFocus(object sender, RoutedEventArgs e)
    {
        var m = CloudModelBox.Text.Trim();
        if (m.Length == 0) { App.Settings.AiModels.Remove(Provider); CloudModelBox.Text = AiConfig.DefaultModel(Provider); }
        else App.Settings.AiModels[Provider] = m;
        App.Settings.Save();
    }

    private void SaveKey_Click(object sender, RoutedEventArgs e)
    {
        App.Settings.SetAiKey(Provider, KeyBox.Password);
        App.Settings.Save();
        ShowProvider();
        App.Window?.SetStatus("API key saved");
    }

    private static string OllamaApp => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Ollama", "ollama app.exe");

    private async Task CheckOllamaAsync()
    {
        OllamaTitle.Text = "Checking local AI…";
        OllamaSub.Text = "";
        _installed = await AiPlanner.OllamaModelsAsync(App.Settings.OllamaUrl);
        var chat = _installed?.Where(m => !m.Contains("embed", StringComparison.OrdinalIgnoreCase)).ToList() ?? [];
        if (_installed is null)
        {
            var onDisk = File.Exists(OllamaApp);
            OllamaIcon.Glyph = "";
            OllamaIcon.Foreground = Ui.Brush("SystemFillColorCautionBrush");
            OllamaTitle.Text = onDisk ? "Ollama is installed but not running" : "Ollama isn't installed";
            OllamaSub.Text = onDisk ? "Start it to use local AI. It runs in the background from the system tray." : "Ollama runs AI models on this PC. Install it to use local AI without sending your text anywhere.";
            InstallButton.Content = onDisk ? "Start Ollama" : "Install Ollama";
            InstallButton.Visibility = Visibility.Visible;
        }
        else
        {
            OllamaIcon.Glyph = "";
            OllamaIcon.Foreground = Ui.Brush("SystemFillColorSuccessBrush");
            OllamaTitle.Text = "Local AI is running";
            OllamaSub.Text = chat.Count == 0 ? "No chat models installed yet." : $"{chat.Count} {(chat.Count == 1 ? "model" : "models")} installed: {string.Join(", ", chat)}";
            InstallButton.Visibility = Visibility.Collapsed;
        }
        _loading = true;
        LocalModelBox.Items.Clear();
        foreach (var m in chat) LocalModelBox.Items.Add(m);
        var configured = App.Settings.AiModels.GetValueOrDefault("ollama");
        var pick = configured is { Length: > 0 } && chat.Contains(configured) ? configured : AiPlanner.PickLocalModel(chat) ?? configured ?? AiConfig.DefaultModel("ollama");
        LocalModelBox.SelectedItem = chat.Contains(pick) ? pick : null;
        LocalModelBox.Text = pick;
        _loading = false;
        LocalModelBox.IsEnabled = _installed is not null;
        DownloadPanel.Visibility = _installed is not null && chat.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private async void Check_Click(object sender, RoutedEventArgs e) => await CheckOllamaAsync();

    private void LocalModel_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || LocalModelBox.SelectedItem is not string m) return;
        App.Settings.AiModels["ollama"] = m;
        App.Settings.Save();
        Controls.AddBar.ResetAiLabel();
    }

    private void LocalModel_TextSubmitted(ComboBox sender, ComboBoxTextSubmittedEventArgs args)
    {
        var m = args.Text.Trim();
        if (m.Length == 0) return;
        App.Settings.AiModels["ollama"] = m;
        App.Settings.Save();
        if (_installed is not null && !_installed.Contains(m)) { PullBox.Text = m; DownloadPanel.Visibility = Visibility.Visible; }
    }

    private async void Install_Click(object sender, RoutedEventArgs e)
    {
        if (File.Exists(OllamaApp))
        {
            Process.Start(new ProcessStartInfo(OllamaApp) { UseShellExecute = true });
            await Task.Delay(4000);
            await CheckOllamaAsync();
            return;
        }
        var ok = await Dialogs.ConfirmAsync(XamlRoot, "Install Ollama?",
            "Docket will use Windows Package Manager (winget) to download Ollama from the internet and install it for your account. " +
            "A window shows the progress. After that, download a model here (several GB) to use local AI.", "Install");
        if (!ok) return;
        try
        {
            var p = Process.Start(new ProcessStartInfo("winget", "install --id Ollama.Ollama -e --accept-package-agreements --accept-source-agreements") { UseShellExecute = true });
            InstallButton.IsEnabled = false;
            OllamaTitle.Text = "Installing Ollama…";
            OllamaSub.Text = "Follow the installer window. This page checks again when it finishes.";
            if (p is not null) await p.WaitForExitAsync();
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            App.Window?.ShowError("Couldn't start the installer", $"{ex.Message}. You can install Ollama from ollama.com instead.");
        }
        finally { InstallButton.IsEnabled = true; }
        await Task.Delay(3000);
        await CheckOllamaAsync();
    }

    private async void Pull_Click(object sender, RoutedEventArgs e)
    {
        var model = PullBox.Text.Trim();
        if (model.Length == 0) return;
        PullButton.IsEnabled = false;
        PullProgress.Visibility = Visibility.Visible;
        PullProgress.Value = 0;
        PullText.Text = $"Downloading {model}…";
        try
        {
            var progress = new Progress<double>(v => { PullProgress.Value = v; PullText.Text = $"Downloading {model}… {v:P0}"; });
            await AiPlanner.PullModelAsync(App.Settings.OllamaUrl, model, progress);
            App.Settings.AiModels["ollama"] = model;
            App.Settings.Save();
            PullText.Text = $"{model} is ready.";
            await CheckOllamaAsync();
        }
        catch (Exception ex) when (ex is HttpRequestException or DocketException or TaskCanceledException or System.Text.Json.JsonException)
        {
            PullText.Text = $"Download failed: {ex.Message}";
        }
        finally { PullButton.IsEnabled = true; PullProgress.Visibility = Visibility.Collapsed; }
    }

    private async void Test_Click(object sender, RoutedEventArgs e)
    {
        if (Provider is "anthropic" or "openai" or "gemini" && KeyBox.Password.Length > 0) SaveKey_Click(sender, e);
        var config = App.Settings.Ai;
        _testCts?.Cancel();
        var cts = _testCts = new CancellationTokenSource();
        TestButton.IsEnabled = false;
        CancelTestButton.Visibility = Visibility.Visible;
        TestBusy.IsActive = true;
        TestResult.Severity = InfoBarSeverity.Informational;
        TestResult.Title = $"Thinking with {config.Label}…";
        TestResult.Message = config.Provider == "ollama" ? "The first answer after start-up can take up to a minute while the model loads." : "";
        TestResult.IsOpen = true;
        try
        {
            var watch = Stopwatch.StartNew();
            var plan = await AiPlanner.PlanAsync(config, "Buy milk tomorrow", DateTimeOffset.Now, (App.Window?.Projects ?? []).Select(p => p.Title), ct: cts.Token);
            var lines = plan.Select(p => p.Title + (string.IsNullOrWhiteSpace(p.Due) ? "" : $" (due {p.Due})"));
            TestResult.Severity = InfoBarSeverity.Success;
            TestResult.Title = $"It works ({watch.Elapsed.TotalSeconds:0} s)";
            TestResult.Message = string.Join("\n", lines);
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested) { TestResult.IsOpen = false; }
        catch (Exception ex) when (ex is DocketException or HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
        {
            TestResult.Severity = InfoBarSeverity.Error;
            TestResult.Title = "The AI didn't answer";
            TestResult.Message = ex.Message;
        }
        finally
        {
            TestButton.IsEnabled = Provider != "off";
            CancelTestButton.Visibility = Visibility.Collapsed;
            TestBusy.IsActive = false;
        }
    }

    private void CancelTest_Click(object sender, RoutedEventArgs e) => _testCts?.Cancel();

    // ---------- connection ----------
    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        var url = ServerBox.Text.Trim();
        var token = TokenBox.Password.Trim();
        if (token.Length == 0) token = App.Settings.Token ?? "";
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || token.Length == 0)
        {
            Show(InfoBarSeverity.Warning, "Check the details", "Enter a full server address and an API token.");
            return;
        }

        SaveButton.IsEnabled = false;
        Busy.IsActive = true;
        try
        {
            using var probe = new VikunjaClient(uri, token);
            var version = await probe.GetServerVersionAsync();
            var projects = await probe.GetProjectsAsync();
            App.Settings.ServerUrl = url.TrimEnd('/');
            App.Settings.Token = token;
            App.Settings.Save();
            App.ReloadClient();
            TokenBox.Password = "";
            Show(InfoBarSeverity.Success, "Connected", $"Docket server {version}, {projects.Count} lists.");
            if (App.Window is not null) await App.Window.OnConnectedAsync();
        }
        catch (Exception ex) when (ex is HttpRequestException or VikunjaException or TaskCanceledException)
        {
            Show(InfoBarSeverity.Error, "Couldn't connect", ex.Message.Replace("Vikunja: ", "", StringComparison.Ordinal));
        }
        finally
        {
            SaveButton.IsEnabled = true;
            Busy.IsActive = false;
        }
    }

    private async void DefaultProject_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || DefaultProject.SelectedItem is not VProject p) return;
        App.Settings.DefaultProjectId = p.Id;
        App.Settings.Save();
        if (App.Window is not null) await App.Window.LoadListsAsync();
    }

    private void Show(InfoBarSeverity severity, string title, string message)
    {
        Result.Severity = severity;
        Result.Title = title;
        Result.Message = message;
        Result.IsOpen = true;
    }
}
