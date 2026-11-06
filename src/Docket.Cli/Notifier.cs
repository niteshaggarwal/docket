using System.Diagnostics;
using Docket.Core;
using Docket.Shared;
using CommunityToolkit.WinUI.Notifications;

namespace Docket.Cli;

/// <summary>
/// Shows Windows notifications for task reminders. "docket notify --once" checks once;
/// "docket notify --watch" keeps checking every 30 seconds and handles the Complete and
/// Snooze buttons while it runs. It is not started automatically.
/// </summary>
internal static class Notifier
{
    /// <summary>
    /// The Docket app, opened when a notification is clicked: DOCKET_APP_EXE, else next to the CLI
    /// (installed layout: Docket\app and Docket\cli), else a build in the source tree (bin\cli in a clone).
    /// </summary>
    public static string? AppExe { get; } = FindApp();

    private static string? FindApp()
    {
        var dir = AppContext.BaseDirectory;
        string[] candidates =
        [
            Environment.GetEnvironmentVariable("DOCKET_APP_EXE") ?? "",
            Path.Combine(dir, "Docket.exe"),
            Path.Combine(dir, "..", "app", "Docket.exe"),
            .. new[] { "Release", "Debug" }.Select(c => Path.Combine(dir, "..", "..", "src", "Docket.App", "bin", "x64", c, "net10.0-windows10.0.26100.0", "win-x64", "Docket.exe")),
        ];
        return candidates.Where(p => p.Length > 0).Select(Path.GetFullPath).FirstOrDefault(File.Exists);
    }
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(30);

    public static async Task<int> RunAsync(bool watch, CancellationToken ct = default)
    {
        if (watch)
        {
            ToastNotificationManagerCompat.OnActivated += e => _ = HandleActivationAsync(e.Argument);
            Console.WriteLine("Watching reminders. Press Ctrl+C to stop.");
        }
        while (true)
        {
            int shown;
            try { shown = await CheckOnceAsync(ct); }
            catch (Exception ex) when (watch && ex is HttpRequestException or VikunjaException or TaskCanceledException && !ct.IsCancellationRequested)
            {
                // Keep watching through server hiccups. If the server on this PC has stopped, start it again:
                // the watcher runs all day, so it doubles as the server's failsafe.
                if (ex is HttpRequestException h && LocalServer.IsConnectionRefused(h))
                {
                    var s = AppSettings.Load();
                    Console.Error.WriteLine(await LocalServer.EnsureRunningAsync(s.ServerUrl, s.VikunjaExe, ct: ct) ? "Restarted the task server." : "The task server is down; trying again shortly.");
                }
                shown = 0;
            }
            if (!watch) { Console.WriteLine(shown == 0 ? "No reminders due." : $"Showed {shown} reminder(s)."); return 0; }
            try { await Task.Delay(Interval, ct); } catch (TaskCanceledException) { return 0; }
        }
    }

    /// <summary>Shows reminders that fell due since the last check and moves the bookmark forward.</summary>
    public static async Task<int> CheckOnceAsync(CancellationToken ct = default)
    {
        var settings = AppSettings.Load();
        using var client = settings.CreateClient() ?? throw new DocketException("Not connected yet. Run: docket login");
        var svc = new DocketService(client);
        var now = DateTimeOffset.Now;
        var since = settings.NotifiedUntil is { } n && n > now.AddDays(-1) ? n : now.AddMinutes(-1);
        var due = await svc.DueRemindersAsync(since, now, ct);
        foreach (var (task, at) in due) Show(task, at, now);

        // Reload before saving so a long-running watcher never overwrites changes made by the app or CLI meanwhile.
        var fresh = AppSettings.Load();
        fresh.NotifiedUntil = now;
        fresh.Save();
        return due.Count;
    }

    public static void Show(TaskInfo info, DateTimeOffset at, DateTimeOffset now)
    {
        var t = info.Task;
        var due = TaskFormat.Due(t, now);
        var line = string.Join(" · ", new[] { info.Project, due.Length > 0 ? "due " + due : "" }.Where(s => s.Length > 0));
        new ToastContentBuilder()
            .AddArgument("action", "open").AddArgument("id", t.Id)
            .AddText(t.Title)
            .AddText(line.Length > 0 ? line : "Reminder")
            .AddAttributionText("Docket reminder")
            .AddButton(new ToastButton().SetContent("Complete").AddArgument("action", "complete").AddArgument("id", t.Id).SetBackgroundActivation())
            .AddButton(new ToastButton().SetContent("Snooze 10 min").AddArgument("action", "snooze").AddArgument("id", t.Id).SetBackgroundActivation())
            .Show(toast => toast.Tag = $"task-{t.Id}");
    }

    /// <summary>Handles a toast click: open the app, complete the task, or snooze it 10 minutes.</summary>
    public static async Task HandleActivationAsync(string argument)
    {
        try
        {
            var args = ToastArguments.Parse(argument);
            var action = args.TryGetValue("action", out var a) ? a : "open";
            long.TryParse(args.TryGetValue("id", out var s) ? s : "0", out var id);
            if (action == "open" || id == 0)
            {
                if (AppExe is { } exe) Process.Start(new ProcessStartInfo(exe) { UseShellExecute = true });
                return;
            }
            var settings = AppSettings.Load();
            using var client = settings.CreateClient();
            if (client is null) return;
            var svc = new DocketService(client) { State = settings };
            if (action == "complete") await svc.SetDoneAsync(id, true);
            else if (action == "snooze")
            {
                var t = await svc.GetAsync(id);
                var times = (t.Reminders ?? []).Select(r => VDates.OrNull(r.Reminder)).OfType<DateTimeOffset>().Where(x => x > DateTimeOffset.Now).ToList();
                times.Add(DateTimeOffset.Now.AddMinutes(10));
                await svc.SetRemindersAsync(id, times);
            }
        }
        catch (Exception ex) when (ex is DocketException or VikunjaException or HttpRequestException or TaskCanceledException)
        {
            Console.Error.WriteLine($"Reminder action failed: {ex.Message}");
        }
    }
}
