# Docket with Claude

Three ways in, depending on where you use Claude. You can use more than one; they all work on the same tasks.

| Where | Setup | Needs |
|---|---|---|
| [Claude Desktop](#claude-desktop) | Local connector | Docket set up on that computer |
| [Claude Code](#claude-code) | Local or remote connector | Docket on that computer, or a Docket server |
| [claude.ai (web, desktop, phone)](#claudeai-web-desktop-and-phone) | Remote connector | A [Docket server](../self-hosting.md) reachable from the internet |

## Claude Desktop

**Windows, with the installer:** nothing to do. `install.ps1` (through `setup-tokens.ps1`) adds a `docket` entry to
Claude Desktop's config. Quit Claude Desktop fully (tray icon > Quit) and reopen it; you'll see Docket's tools under
the tools icon in the message box.

**By hand** (any OS), edit `claude_desktop_config.json`
(Claude Desktop > **Settings > Developer > Edit Config**):

```json
{
  "mcpServers": {
    "docket": {
      "command": "C:\\Users\\you\\AppData\\Local\\Programs\\Docket\\mcp\\docket-mcp.exe"
    }
  }
}
```

On macOS or Linux, point `command` at the downloaded `docket-mcp` and pass your server and token:

```json
{
  "mcpServers": {
    "docket": {
      "command": "/usr/local/bin/docket-mcp",
      "env": {
        "DOCKET_VIKUNJA_URL": "https://tasks.example.com",
        "DOCKET_VIKUNJA_TOKEN_FILE": "/Users/you/.config/docket/vikunja-token"
      }
    }
  }
}
```

Make the token file once with `DOCKET_VIKUNJA_URL=https://tasks.example.com DOCKET_DATA_DIR=~/.config/docket docket-mcp setup`.
Save the config without a byte-order mark (plain UTF-8); Claude Desktop rejects files that start with one.

If you run a Docket server, Claude Desktop can also use the [remote connector](#claudeai-web-desktop-and-phone):
connectors you add on claude.ai show up in the desktop app too.

## Claude Code

**Local** (same computer as Docket):

```sh
claude mcp add --scope user docket -- "C:\Users\you\AppData\Local\Programs\Docket\mcp\docket-mcp.exe"
```

`--scope user` makes it available in every project. On macOS/Linux add `-e DOCKET_VIKUNJA_URL=... -e DOCKET_VIKUNJA_TOKEN_FILE=...`
before `--`.

**Remote** (a Docket server), with the connector key as a header:

```sh
claude mcp add --scope user --transport http docket https://tasks.example.com/mcp \
  --header "Authorization: Bearer YOUR_CONNECTOR_KEY"
```

Or leave the header off and run `/mcp` in Claude Code to sign in with OAuth instead.

Check it with `claude mcp get docket`, or `/mcp` inside a session.

## claude.ai (web, desktop and phone)

This uses a **custom connector**, which works on every claude.ai plan (Free allows one custom connector). Claude
connects to your server from Anthropic's cloud, so the server needs a public HTTPS address. The
[Docker server](../self-hosting.md) gives you one; [Tailscale Funnel or a Cloudflare Tunnel](../self-hosting.md#no-public-ip-or-domain)
work if you don't want to open ports.

1. In Claude, go to **Customize > Connectors** and choose **Add custom connector**.
   (On Team and Enterprise plans an Owner adds it under **Organization settings > Connectors**, then each member connects.)
2. Name: `Docket`. URL: `https://tasks.example.com/mcp`.
3. Authentication: **Sign in now** (or **Sign in when needed**). OAuth client: **Register automatically**.
   Leave client ID and secret empty.
4. Click **Add**, then **Connect**. Docket's sign-in page opens: type your connector key (`DOCKET_MCP_KEY` from the
   server's `.env`) and choose **Allow access**.
5. In a chat, open **+ > Connectors** and make sure Docket is on.

The connection then works in the Claude apps on your phone and desktop as well. Claude asks before using a tool that
changes something; you can allow Docket's tools permanently, or block single tools, under **Customize > Connectors > Docket**.

If your organization has request-header authentication, you can choose **No sign-in** and add a request header
`authorization` with the value `Bearer YOUR_CONNECTOR_KEY` instead of OAuth.

**To disconnect:** remove the connector in Claude. To cut off every OAuth client at once, change `DOCKET_MCP_KEY`
and restart the connector; every sign-in made with the old key stops working.

## Troubleshooting

| Symptom | Fix |
|---|---|
| Tools don't appear in Claude Desktop | Quit it from the tray icon, not just the window. Check the path in the config, and that the file has no BOM. |
| "Docket isn't set up yet" | The connector can't find a token: sign in with the Docket app or installer (Windows), or set `DOCKET_VIKUNJA_URL` and a token. |
| claude.ai: "couldn't connect" | Open `https://your-domain/connector/health` in a browser; it should say `"status":"ok"`. The server must be reachable from the internet, not just your network. |
| Sign-in page says the link isn't valid | Remove the connector in Claude and add it again (its registration was lost, e.g. `oauth.json` was deleted). |
| "Too many wrong keys" | Ten wrong keys from one address in 15 minutes locks sign-in for 15 minutes. |
| "isn't an allowed return address" | The client sends you back somewhere Docket doesn't know; add its host to `DOCKET_OAUTH_REDIRECT_HOSTS`. |
| Times are off by hours | Set `TZ` on the server (the Docker `.env` has it). |
