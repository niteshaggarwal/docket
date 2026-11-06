# Troubleshooting

Find your problem below. Most fixes take a minute. If yours isn't here, ask in
[Discussions](https://github.com/niteshaggarwal/docket/discussions) or [report a bug](https://github.com/niteshaggarwal/docket/issues).

## The server stopped, or "Starting your task server…"

On a Windows install, the task server runs on your PC. If it stops, Docket fixes it for you:

- The **app, CLI and AI connector** start it again and carry on. "Starting your task server…" for a few seconds is
  normal.
- **Task Scheduler checks every 5 minutes** and restarts it if needed.

If the app says it couldn't start the server:

1. Choose **Start server and retry**.
2. Still stuck? Open Task Scheduler > Task Scheduler Library > **Docket**, right-click **Vikunja server** > **Run**,
   and check it isn't **Disabled**.
3. Look at the log in `%LOCALAPPDATA%\Docket\server\logs` for the reason (for example "address already in use" if
   another program took port 3456).
4. Restart the PC, or run `install.ps1` again. It repairs the install and keeps your tasks.

On a Docker server or someone else's Vikunja, Docket can't start it from your PC; the app says it can't reach the
server. Check the server is up: `docker compose ps`, and `docker compose logs vikunja`.

## "This token can't delete" (or can't share)

The app is using a token without delete or sharing rights, usually the AI connector's no-delete token, or a token
made by an older installer. Make fresh tokens:

```powershell
& "$env:LOCALAPPDATA\Programs\Docket\setup-tokens.ps1"
```

Sign in when asked. It gives the Docket app a full token and the AI connector a fresh no-delete one. For a server
elsewhere, add `-ServerUrl https://your-server`. (From a clone of the repository the script is
`deploy\windows\setup-tokens.ps1`.)

If an **AI assistant** says it can't delete or share: that's on purpose. Do it in the Docket app.

## Signed out, or "401 unauthorized" after a year

Tokens made by the installer last a year. When they expire the apps can't sign in. Run `setup-tokens.ps1` as above
(Windows), or sign in again on Android.

## The phone can't connect

- **Is Tailscale on** on both the phone and the PC, signed in to the same account? Both should show as connected
  in the Tailscale app.
- **Is the address right?** Run `tailscale serve status` on the PC and use exactly the `https://….ts.net` address
  it shows. Don't add a port.
- **Is the PC awake and signed in?** The server runs while you're signed in to Windows.
- **Open the address in the phone's browser.** If Docket web or Vikunja loads there, the network is fine and it's a
  sign-in problem: check the username and password.
- **Docker server:** use `https://your-domain` and check the certificate works in a browser.

More: [phone access](setup/phone.md).

## Reminders don't pop up

**On Windows**, pop-ups come from the notifier `docket notify --watch`, not from the app window.

1. Check it runs: Task Scheduler > **Docket** > **Docket reminders**. No such task? Run the installer again with
   `-Reminders`, or start it yourself with `docket notify --watch`.
2. Check Windows lets it notify: Settings > System > Notifications, find **Docket**, and turn it on. Turn off
   **Do not disturb** / Focus.
3. Run `docket notify --once` to show anything due now straight away.

**On Android**, allow notifications for Docket, allow **alarms and reminders** if asked, and turn off battery
optimisation for Docket. Open the app after adding reminders on another device, so the phone schedules them.

## AI planning doesn't respond

For the `/ai` command and the sparkle button:

- **"Turn on an AI in Settings first"**: choose a provider in **Settings > AI**.
- **Local (Ollama):**
  - Settings > AI should say **Local AI is running**. If not, choose **Start Ollama** (or **Install Ollama**).
  - You need a chat model: download one there (e.g. `qwen2.5:7b`) and pick it in **Model**.
  - The first answer after starting can take up to a minute while the model loads. Later ones are faster.
  - Very slow? The model may be too big for your PC. Try a smaller one, close other heavy apps.
  - Check from PowerShell: `ollama list` shows installed models; `ollama run qwen2.5:7b "hi"` should answer.
- **On Android with local AI:** Ollama must listen on the network (`OLLAMA_HOST=0.0.0.0`) and the phone must reach the
  PC. See [AI planning on Android](using/ai-planning.md#on-android).
- **Claude, ChatGPT or Gemini:** check the API key (paste it again and **Save key**), that the account has API credit,
  and the model name. **Test** in Settings shows the provider's error message.

## An AI assistant doesn't see Docket

| Where | Check |
|---|---|
| Claude Desktop | Quit it from the tray icon (not just the window) and reopen. Check the path in its config, and that the file has no byte-order mark |
| Claude Code | `claude mcp get docket`, or `/mcp` in a session |
| claude.ai | Open `https://your-domain/connector/health` from a phone on mobile data. It must say `"status":"ok"` and be reachable from the internet |
| ChatGPT | After a Docket upgrade, delete and re-add the connector so it sees new tools |
| Any | "Docket isn't set up yet": the connector has no token. Sign in with the installer, or run `docket-mcp setup` |

More in [Claude](ai/claude.md#troubleshooting), [ChatGPT](ai/chatgpt.md#troubleshooting) and
[other clients](ai/other-clients.md#checking-a-connection).

## Times are off by a few hours

The server's time zone is wrong. Windows install: run `install.ps1` again with `-TimeZone Your/Zone`. Docker: set
`TZ` in `.env` and run `docker compose up -d`.

## A repeating task didn't move to its next date

Weekdays, yearly and every-N-months or -years repeats (with a "↻" category) only move forward when completed in a
Docket app, the CLI or through the AI connector, not in Vikunja's web app or Docket web. Reopen it and complete it in
Docket.

## Where are the logs?

| What | Where |
|---|---|
| Task server (Windows) | `%LOCALAPPDATA%\Docket\server\logs` |
| Docker services | `docker compose logs -f vikunja` (or `connector`, `web`, `caddy`) |
| Scheduled tasks | Task Scheduler > Docket > the task > **History** tab |

## Start fresh

Uninstall with `-RemoveData` and install again; see [uninstalling](setup/uninstalling.md). Make a
[backup](setup/backups.md) first if there's anything you want to keep.
