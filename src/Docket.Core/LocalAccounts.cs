using System.Diagnostics;
using System.Security.Cryptography;

namespace Docket.Core;

/// <summary>A new account and the password to hand to its owner.</summary>
public sealed record NewAccount(string Username, string Email, string Password);

/// <summary>
/// Creates Docket accounts for people when the server runs on this computer. Vikunja's API can't create
/// accounts with a token, so this calls the server program's own "user create" command.
/// A person added by email can sign in with that email's account; a person added locally gets a placeholder address.
/// </summary>
public static class LocalAccounts
{
    public static string? FindVikunja(string serverUrl, string? configuredExe)
    {
        if (!Uri.TryCreate(serverUrl, UriKind.Absolute, out var u) || !(u.IsLoopback || u.Host == "localhost")) return null;
        string?[] candidates =
        [
            configuredExe,
            Environment.GetEnvironmentVariable("DOCKET_VIKUNJA_EXE"),
            @"C:\dev\vikunja\bin\vikunja.exe",
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Docket", "server", "vikunja.exe"),
        ];
        return candidates.FirstOrDefault(p => !string.IsNullOrEmpty(p) && File.Exists(p));
    }

    /// <summary>A readable one-off password: four words of letters and digits.</summary>
    public static string NewPassword()
    {
        const string chars = "abcdefghjkmnpqrstuvwxyz23456789";
        var parts = Enumerable.Range(0, 4).Select(_ => new string(Enumerable.Range(0, 4).Select(_ => chars[RandomNumberGenerator.GetInt32(chars.Length)]).ToArray()));
        return string.Join("-", parts);
    }

    public static string UsernameFrom(string nameOrEmail)
    {
        var basis = nameOrEmail.Contains('@') ? nameOrEmail[..nameOrEmail.IndexOf('@')] : nameOrEmail;
        var clean = new string(basis.ToLowerInvariant().Select(c => char.IsLetterOrDigit(c) ? c : '.').ToArray()).Trim('.');
        while (clean.Contains("..")) clean = clean.Replace("..", ".");
        return clean.Length >= 3 ? clean : (clean + "user")[..Math.Max(3, clean.Length)];
    }

    public static async Task<NewAccount> CreateAsync(string vikunjaExe, string username, string? email, string? password = null, CancellationToken ct = default)
    {
        var pw = string.IsNullOrEmpty(password) ? NewPassword() : password;
        var mail = string.IsNullOrWhiteSpace(email) ? $"{username}@people.docket.local" : email.Trim();
        var dir = Path.GetDirectoryName(vikunjaExe)!;
        var config = new[] { Path.Combine(dir, "config.yml"), Path.Combine(Path.GetDirectoryName(dir)!, "config.yml") }.FirstOrDefault(File.Exists);
        var psi = new ProcessStartInfo(vikunjaExe)
        {
            RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true,
            WorkingDirectory = config is null ? dir : Path.GetDirectoryName(config)!,
        };
        if (config is not null) { psi.ArgumentList.Add("--config"); psi.ArgumentList.Add(config); }
        foreach (var a in new[] { "user", "create", "-u", username, "-e", mail, "-p", pw }) psi.ArgumentList.Add(a);
        using var p = Process.Start(psi) ?? throw new DocketException("Couldn't start the server program.");
        var output = await p.StandardOutput.ReadToEndAsync(ct) + await p.StandardError.ReadToEndAsync(ct);
        await p.WaitForExitAsync(ct);
        if (p.ExitCode != 0 || output.Contains("error", StringComparison.OrdinalIgnoreCase) && !output.Contains("created", StringComparison.OrdinalIgnoreCase))
            throw new DocketException($"Couldn't create the account: {LastLine(output)}");
        return new NewAccount(username, mail, pw);
    }

    // The server prints nothing when the username or email is already in use, so say that rather than "unknown error".
    private static string LastLine(string s) => s.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).LastOrDefault()
        ?? "that username or email is probably already taken. Try another one.";
}
