# Install on a Windows PC

The Windows installer sets up everything on one PC: the Vikunja task server, the Docket app, the `docket` command,
Docket web and the AI connector. It takes about five minutes and needs no admin rights. Everything is installed for
your Windows account only.

**You need** Windows 10 or 11 (x64 or Arm64) and an internet connection for the download.

## Step by step

1. **Download.** Get `docket-<version>-windows-x64.zip` (or `-arm64.zip` for Arm PCs such as Surface Pro X or
   Snapdragon laptops) from the [latest release](https://github.com/niteshaggarwal/docket/releases/latest).
2. **Unzip it** into any folder, for example your Downloads folder. (Don't run it from inside the zip.)
3. **Run the installer.** Right-click `install.ps1` and choose **Run with PowerShell**. If PowerShell asks about
   the execution policy, or the window closes straight away, open PowerShell in that folder (in File Explorer:
   **File > Open Windows PowerShell**, or type `powershell` in the address bar) and run:

   ```powershell
   powershell -ExecutionPolicy Bypass -File install.ps1
   ```

4. **Answer the questions** (explained below). Your browser opens Vikunja: create your account there.
5. **Open Docket** from the Start menu.

## What each question means

The installer prints what it's doing in sections (`== Vikunja`, `== Scheduled tasks`, `== Your account`…). It asks
only a few things:

| The installer asks | What it means | What to answer |
|---|---|---|
| `Your time zone, e.g. Europe/London, America/New_York, Asia/Kolkata` | Used for "today", due dates and reminders | Your [time zone name](https://en.wikipedia.org/wiki/List_of_tz_database_time_zones), like `Europe/London` |
| `Press Enter when your account exists` | Your browser has opened Vikunja's sign-up page at `http://localhost:3456/register` | Create an account (username, email, password), then press <kbd>Enter</kbd> |
| `Vikunja username` and `Vikunja password` | One sign-in, so the installer can make keys (API tokens) for the app and for AI | The account you just created. The password isn't saved |
| `Two-factor code (press Enter if you have not turned it on)` | Only if you've turned on two-factor sign-in in Vikunja | Press <kbd>Enter</kbd> on a fresh install |
| `Close sign-ups so nobody else can make an account on this server? [Y/n]` | Stops new accounts being created on your server | **Y** (you can still add people from the Docket app) |
| `Docket is already connected to an account. Connect again (new tokens)? [y/N]` | Only when you run the installer again | **N** to keep things as they are; **Y** to make fresh tokens |

After signing in, the installer:

- makes a token for the Docket app and CLI, and a separate **no-delete** token for the AI connector;
- adds Docket to **Claude Desktop** and **Claude Code** if you have them (quit and reopen Claude Desktop to load it);
- prints where everything is, and how to uninstall.

> [!NOTE]
> Windows may show "Windows protected your PC" for the downloaded script or app. Choose **More info > Run anyway**.
> The release builds aren't code-signed yet.

## What gets installed where

| What | Where |
|---|---|
| Docket app, `docket` CLI, `docket-mcp`, Docket web | `%LOCALAPPDATA%\Programs\Docket` (the CLI is added to your PATH; Start menu shortcut **Docket**) |
| Vikunja and your data | `%LOCALAPPDATA%\Docket\server` (kept outside OneDrive: a live database and file sync don't mix) |
| Backups | `Documents\Docket\backups`, every night at 11 pm, newest 14 kept |
| Settings and tokens | `%LOCALAPPDATA%\Docket\settings.json` (tokens encrypted with Windows DPAPI for your account) |
| Server log | `%LOCALAPPDATA%\Docket\server\logs` |

**Scheduled tasks** (Task Scheduler > Task Scheduler Library > **Docket**):

| Task | What it does |
|---|---|
| Vikunja server | Starts the task server when you sign in, then checks every 5 minutes and restarts it if it stopped |
| Docket web | Runs [Docket web](docket-web.md) at `http://localhost:3457`, with the same 5-minute check |
| Vikunja backup | Nightly backup at 11 pm |
| Docket reminders | Only with `-Reminders`: shows reminder notifications from sign-in |

The server listens on `127.0.0.1` only, so other devices on your network can't reach it. To use your phone, see
[phone access](phone.md).

## Options

Add these after `install.ps1` when you run it from PowerShell:

| Option | What it does |
|---|---|
| `-Reminders` | Shows reminder notifications from sign-in (adds the "Docket reminders" task). Recommended |
| `-TimeZone Europe/London` | Skips the time-zone question |
| `-Port 3500` | Runs Vikunja on another port (default 3456) |
| `-NoScheduledTasks -NoPath -NoStartMenu` | A portable setup: no scheduled tasks, PATH entry or shortcut |
| `-SkipAccountSetup` | Installs the programs but doesn't ask you to sign in |

For example:

```powershell
powershell -ExecutionPolicy Bypass -File install.ps1 -Reminders -TimeZone Asia/Kolkata
```

Run the installer again at any time to upgrade or repair; your tasks stay. See [updating](updating.md).

## If the server stops

The task server keeps itself running, with three layers of protection:

1. **The apps restart it.** When the Docket app, the `docket` CLI or the AI connector finds the server on this PC
   stopped, it starts it again (through its scheduled task, or the server program directly), waits for it, and then
   carries on with what you asked. You see "Starting your task server…" for a few seconds instead of an error.
2. **The scheduled tasks check every 5 minutes.** "Vikunja server", "Docket web" and "Docket reminders" run at sign-in
   and again every 5 minutes. A running copy is left alone; a stopped one starts again.
3. **The reminders watcher heals it too.** `docket notify --watch` runs all day; if the server stops, it restarts it
   and keeps watching.

If the server still can't start, the app says so and offers **Start server and retry**. If that fails too, restart
the PC, or run `install.ps1` again (it keeps your data). The server's log is in `%LOCALAPPDATA%\Docket\server\logs`.

A server on another machine (Docker, a VPS) can't be started from your PC: there the app tells you it can't reach it.

## Connect it to your account again

If tokens stop working (they last a year), or the app says a token can't do something, make new ones:

```powershell
& "$env:LOCALAPPDATA\Programs\Docket\setup-tokens.ps1"
```

It asks for your Vikunja username and password once, replaces the app's and the AI connector's tokens, and
reconnects Claude Desktop and Claude Code. For a server elsewhere, add `-ServerUrl https://your-server`.

## From source

To build from the repository instead of a release: install the [.NET 10 SDK](https://dotnet.microsoft.com/download),
clone the repo, and run `deploy\windows\install.ps1`. It builds the app first (a few minutes).
