using System.Diagnostics;
using Docket.Core;
using Docket.Mobile.Services;

namespace Docket.Mobile.Views;

/// <summary>Which AI plans tasks from the add bar: local (Ollama on the PC), Claude, ChatGPT, Gemini or off.</summary>
public sealed class AiSettingsPage : ContentPage
{
    private readonly Session _session;
    private readonly VerticalStackLayout _stack = new() { Padding = new Thickness(12, 4, 12, 24), Spacing = 8 };
    private List<string>? _models;
    private string? _status;
    private bool _statusOk;
    private CancellationTokenSource? _test;

    public AiSettingsPage(Session session)
    {
        _session = session;
        Content = new ScrollView { Content = _stack };
    }

    private bool _appeared;

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (_appeared && SheetState.JustClosed) return;
        _appeared = true;
        await RenderAsync();
        if (AiSettings.Provider == "ollama") await FindModelsAsync();
    }

    private async Task RenderAsync()
    {
        _stack.Clear();
        _stack.Add(U.Title("AI planning"));
        _stack.Add(U.Caption("Type “/ai” or tap the sparkle in the add bar, describe what you need, and the AI drafts the tasks (with subtasks, dates and lists). You see a preview before anything is added.", 13));

        _stack.Add(U.Section("Provider"));
        var list = new VerticalStackLayout { Spacing = 0 };
        var current = AiSettings.Provider;
        foreach (var (id, name, blurb) in AiSettings.Providers)
        {
            var g = new Grid { ColumnDefinitions = [new(44), new(GridLength.Star), new(36)], MinimumHeightRequest = 56 };
            g.Add(U.Glyph(id switch { "ollama" => IconChars.Bot, "off" => IconChars.Dismiss, _ => IconChars.Sparkle }, 20, id == current ? Th.Accent : Th.Text2), 0);
            var col = new VerticalStackLayout { VerticalOptions = LayoutOptions.Center, Padding = new Thickness(0, 6) };
            col.Add(U.Text(name, 15, id == current));
            col.Add(U.Caption(blurb));
            g.Add(col, 1);
            if (id == current) g.Add(U.Glyph(IconChars.Check, 18, Th.Accent), 2);
            var pid = id;
            list.Add(g.Tap(async () => { AiSettings.Provider = pid; _status = null; _models = null; await RenderAsync(); if (pid == "ollama") await FindModelsAsync(); }));
        }
        _stack.Add(U.Card(list, new Thickness(0, 2)));
        if (current == "off") return;

        _stack.Add(U.Section(current == "ollama" ? "Local AI on your PC" : AiSettings.NameOf(current)));
        var form = new VerticalStackLayout { Spacing = 8 };
        if (current == "ollama")
        {
            var hint = AiSettings.DefaultOllamaUrl(_session.ServerUrl);
            form.Add(U.Text("Address of Ollama on your PC", 14, true));
            var url = U.Entry(hint, AiSettings.OllamaUrl(_session.ServerUrl), Keyboard.Url);
            url.Unfocused += async (_, _) => { AiSettings.SetOllamaUrl(string.IsNullOrWhiteSpace(url.Text) ? hint : url.Text); await FindModelsAsync(); };
            form.Add(U.Field(url));
            form.Add(U.Caption($"Usually your PC's address on port 11434, like {hint}. Ollama on the PC must accept network connections (set OLLAMA_HOST=0.0.0.0 there) and Tailscale must be on if you use it.", 12));
            form.Add(U.Text("Model", 14, true));
            if (_models is { Count: > 0 } models)
            {
                var picker = new Picker { ItemsSource = models, Title = "Model", FontFamily = "Selawik" }.Themed(Picker.TextColorProperty, Th.Text);
                var chosen = AiSettings.ModelFor("ollama");
                picker.SelectedItem = models.Contains(chosen) ? chosen : AiPlanner.PickLocalModel(models);
                if (picker.SelectedItem is string sel && sel != chosen) AiSettings.SetModel("ollama", sel);
                picker.SelectedIndexChanged += (_, _) => { if (picker.SelectedItem is string m) AiSettings.SetModel("ollama", m); };
                form.Add(U.Field(picker));
            }
            else form.Add(U.Caption(_models is null ? "Looking for installed models…" : "No models installed. Install one in Ollama on your PC.", 13));
            form.Add(U.Plain("Find models", FindModelsAsync));
        }
        else
        {
            form.Add(U.Text("API key", 14, true));
            var key = U.Entry("Paste your API key", await AiSettings.KeyAsync(current) ?? "", password: true);
            key.Unfocused += async (_, _) => await AiSettings.SetKeyAsync(current, key.Text);
            form.Add(U.Field(key));
            form.Add(U.Caption("Kept in Android's secure storage on this phone only. Requests go straight from the phone to the provider.", 12));
            form.Add(U.Text("Model", 14, true));
            var model = U.Entry(AiConfig.DefaultModel(current), AiSettings.ModelFor(current));
            model.Unfocused += (_, _) => AiSettings.SetModel(current, string.IsNullOrWhiteSpace(model.Text) ? AiConfig.DefaultModel(current) : model.Text);
            form.Add(U.Field(model));
        }
        _stack.Add(U.Card(form));

        var testRow = new HorizontalStackLayout { Spacing = 8 };
        testRow.Add(U.Accent("Test", TestAsync));
        if (_test is not null) testRow.Add(U.Plain("Cancel", () => { _test?.Cancel(); return Task.CompletedTask; }));
        _stack.Add(testRow);
        if (_status is not null) _stack.Add(U.Text(_status, 14, false, _statusOk ? Duo.Of("#107C10", "#6CCB5F") : Th.Danger));
    }

    private async Task FindModelsAsync()
    {
        var url = AiSettings.OllamaUrl(_session.ServerUrl);
        _models = await AiPlanner.OllamaModelsAsync(url);
        if (_models is null) { _models = []; _status = $"Can't reach Ollama at {url}."; _statusOk = false; }
        else { _status = $"Connected · {_models.Count} model{(_models.Count == 1 ? "" : "s")} installed."; _statusOk = true; }
        await RenderAsync();
    }

    private async Task TestAsync()
    {
        if (_test is not null) return;
        // Save what's typed before testing.
        foreach (var e in _stack.GetVisualTreeDescendants().OfType<Entry>()) e.Unfocus();
        await Task.Delay(100);
        var config = await AiSettings.ConfigAsync(_session.ServerUrl);
        if (!config.IsReady) { _status = "Add an API key and a model first."; _statusOk = false; await RenderAsync(); return; }
        _test = new CancellationTokenSource();
        _status = config.Provider == "ollama" ? "Asking the local AI… (a local model can take up to a minute)" : "Asking…";
        _statusOk = true;
        await RenderAsync();
        var sw = Stopwatch.StartNew();
        try
        {
            var plan = await AiPlanner.PlanAsync(config, "Water the plants tomorrow morning", DateTimeOffset.Now, ["Tasks"], ct: _test.Token);
            _status = plan.Count > 0 ? $"Works · planned “{plan[0].Title}” in {sw.Elapsed.TotalSeconds:0} s." : "The AI answered but suggested no tasks.";
            _statusOk = plan.Count > 0;
        }
        catch (OperationCanceledException) { _status = "Test cancelled."; _statusOk = false; }
        catch (Exception ex) when (U.IsExpected(ex) || ex is System.Text.Json.JsonException or KeyNotFoundException) { _status = U.Friendly(ex); _statusOk = false; }
        finally { _test = null; }
        await RenderAsync();
    }
}
