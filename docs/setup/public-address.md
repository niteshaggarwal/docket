# Public address for AI

claude.ai and ChatGPT run in the cloud, so they connect to your tasks from the internet. That means the **AI
connector** needs a public HTTPS address. The Docket apps, Claude Desktop, Claude Code and local models don't need
this; they work privately.

You have three options:

| Option | Good for | Opens ports? |
|---|---|---|
| A [Docker server](docker.md) with a domain | Always-on servers | Yes, 80 and 443 |
| [Tailscale Funnel](#tailscale-funnel) | The Windows install | No |
| [Cloudflare Tunnel](#cloudflare-tunnel) | A Docker server at home | No |

Whichever you pick, only the connector becomes public, and it's protected by your connector key. Pick a long random
key and keep it private.

## Tailscale Funnel

Funnel publishes one port of your PC on a public `ts.net` address with an HTTPS certificate. Use it to let claude.ai
or ChatGPT reach a Windows install.

1. Set up [Tailscale](phone.md) on the PC, and turn on **Funnel** for your tailnet in the Tailscale admin console
   (Access controls; Tailscale's [Funnel guide](https://tailscale.com/kb/1223/funnel) has the current steps).
2. Run the connector over HTTP on the PC, with a key and your public address:

   ```powershell
   $env:DOCKET_MCP_KEY = "a-long-random-key-of-24-or-more-characters"
   $env:DOCKET_PUBLIC_URL = "https://my-pc.tail1234.ts.net"
   & "$env:LOCALAPPDATA\Programs\Docket\mcp\docket-mcp.exe" --http
   ```

   It listens on `http://127.0.0.1:8080` and reads your Docket settings for the server and token.
3. In another PowerShell window, publish it:

   ```powershell
   tailscale funnel --bg 8080
   ```

4. Connect [Claude](../ai/claude.md#claudeai-web-desktop-and-phone) or [ChatGPT](../ai/chatgpt.md) to
   `https://my-pc.tail1234.ts.net/mcp`, and type the key when asked.

Vikunja itself stays private. The connector needs to keep running: to start it at sign-in, add it to Task
Scheduler or a startup script with the same environment variables.

> [!WARNING]
> Funnel and `serve` share ports on the PC. If you use `tailscale serve` for your phone on 3457, keep Funnel on a
> different port (8080 above).

## Cloudflare Tunnel

Good for a Docker server at home. Run `cloudflared` with a hostname on your Cloudflare domain, and set
`DOCKET_DOMAIN` and `DOCKET_PUBLIC_URL` to that hostname. Cloudflare handles HTTPS, so leave Caddy out and point the
tunnel at the services directly:

| Path | Send to |
|---|---|
| `/mcp`, `/oauth/*`, `/.well-known/oauth-*`, `/api/tools/*`, `/openapi.json` | `connector:8080` |
| everything else | `web:8080` ([Docket web](docket-web.md)) |

See Cloudflare's [tunnel documentation](https://developers.cloudflare.com/cloudflare-one/connections/connect-networks/)
for creating the tunnel.

## Check it from outside

From a phone on mobile data (not your Wi-Fi), open the connector's health page: `https://your-address/health` when
the connector is published directly (Funnel), or `https://your-domain/connector/health` on the Docker server. It
should say `"status":"ok"`. If it doesn't load there, claude.ai and ChatGPT can't reach it either.
