using System.Diagnostics;
using System.Net.Sockets;

namespace Docket.Core;

/// <summary>
/// Self-healing for a server that runs on this PC. When a Docket tool finds the server stopped, it starts it again
/// (through its scheduled task, or the server program directly) and waits for it, before showing any error.
/// Servers on other machines are left alone: they can't be started from here.
/// </summary>
public static class LocalServer
{
    /// <summary>Scheduled tasks that run the server: the Windows installer's, and older manual installs'.</summary>
    public static readonly string[] TaskNames = [@"\Docket\Vikunja server", @"\Tasks\Vikunja", @"\Vikunja"];

    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static DateTimeOffset _lastAttempt = DateTimeOffset.MinValue;

    /// <summary>True when the error means nothing is listening at the address (the server is stopped), not a slow or remote failure.</summary>
    public static bool IsConnectionRefused(Exception ex)
    {
        for (var e = ex; e is not null; e = e.InnerException)
            if (e is SocketException { SocketErrorCode: SocketError.ConnectionRefused }) return true;
        return false;
    }

    public static bool IsLocal(string serverUrl) =>
        Uri.TryCreate(serverUrl, UriKind.Absolute, out var u) && (u.IsLoopback || u.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase));

    public static async Task<bool> IsUpAsync(string serverUrl, CancellationToken ct = default)
    {
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
            using var res = await http.GetAsync(serverUrl.TrimEnd('/') + "/api/v1/info", ct);
            return res.IsSuccessStatusCode;
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException) { return false; }
    }

    /// <summary>
    /// Makes sure a local server is running: if it's down, starts it and waits up to <paramref name="wait"/>.
    /// Returns true when the server answers. Several callers at once share one attempt.
    /// </summary>
    public static async Task<bool> EnsureRunningAsync(string serverUrl, string? vikunjaExe = null, TimeSpan? wait = null, CancellationToken ct = default)
    {
        if (!OperatingSystem.IsWindows() || !IsLocal(serverUrl)) return false;
        if (await IsUpAsync(serverUrl, ct)) return true;
        await Gate.WaitAsync(ct);
        try
        {
            if (await IsUpAsync(serverUrl, ct)) return true;
            // Don't hammer: one start attempt per 20 seconds, however many screens ask.
            if (DateTimeOffset.UtcNow - _lastAttempt > TimeSpan.FromSeconds(20))
            {
                _lastAttempt = DateTimeOffset.UtcNow;
                if (!StartTask()) StartDirectly(serverUrl, vikunjaExe);
            }
            var until = DateTimeOffset.UtcNow + (wait ?? TimeSpan.FromSeconds(25));
            while (DateTimeOffset.UtcNow < until)
            {
                await Task.Delay(750, ct);
                if (await IsUpAsync(serverUrl, ct)) return true;
            }
            return false;
        }
        finally { Gate.Release(); }
    }

    private static bool StartTask()
    {
        foreach (var name in TaskNames)
            if (Run("schtasks.exe", "/Run", "/TN", name) == 0) return true;
        return false;
    }

    private static void StartDirectly(string serverUrl, string? vikunjaExe)
    {
        var exe = LocalAccounts.FindVikunja(serverUrl, vikunjaExe);
        if (exe is null) return;
        var dir = Path.GetDirectoryName(exe)!;
        var config = new[] { Path.Combine(dir, "config.yml"), Path.Combine(Path.GetDirectoryName(dir)!, "config.yml") }.FirstOrDefault(File.Exists);
        var psi = new ProcessStartInfo(exe) { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = config is null ? dir : Path.GetDirectoryName(config)! };
        if (config is not null) { psi.ArgumentList.Add("--config"); psi.ArgumentList.Add(config); }
        try { Process.Start(psi)?.Dispose(); } catch (System.ComponentModel.Win32Exception) { }
    }

    private static int Run(string file, params string[] args)
    {
        try
        {
            var psi = new ProcessStartInfo(file) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
            foreach (var a in args) psi.ArgumentList.Add(a);
            using var p = Process.Start(psi);
            if (p is null) return -1;
            p.WaitForExit(10_000);
            return p.HasExited ? p.ExitCode : -1;
        }
        catch (System.ComponentModel.Win32Exception) { return -1; }
    }

    /// <summary>What to tell the person when the server is still down after a restart attempt.</summary>
    public static string StillDownMessage(string serverUrl) => IsLocal(serverUrl)
        ? "Docket couldn't start your task server on this PC. Try again in a minute; if it keeps happening, restart the PC or run the installer again."
        : $"Can't reach the server at {serverUrl}. Check your internet connection, or that the server is on.";
}
