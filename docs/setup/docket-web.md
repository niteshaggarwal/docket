# Docket web

`docket-web` is Vikunja's own web app dressed as Docket: the Fluent look of the Windows app in light and dark, with
Docket's name and icons instead of Vikunja's. Every Vikunja feature stays: list, Gantt, table and Kanban views,
upcoming, teams, labels, sharing and settings. Use it on a Mac, a Linux PC, an iPhone or any computer with a browser.

How to use it day to day: [the web app](../using/web.md). This page is about running it.

## Where it runs

| Setup | Address | Started by |
|---|---|---|
| [Windows PC](windows.md) | `http://localhost:3457` | Scheduled task "Docket web", at sign-in and every 5 minutes |
| [Docker server](docker.md) | `https://your-domain/` | The `web` service in `compose.yaml` |
| [Your own Vikunja](existing-vikunja.md) | Wherever you route it | You |

On Windows, the app's **Open in browser** goes to Docket web when it's running, otherwise to Vikunja.

## Windows

`install.ps1` installs it to `%LOCALAPPDATA%\Programs\Docket\web\` and starts it at sign-in. To run it by hand:

```powershell
& "$env:LOCALAPPDATA\Programs\Docket\web\docket-web.exe"
```

It forwards to Vikunja on port 3456 and listens on `http://127.0.0.1:3457`. To open it on your phone through
Tailscale, run `tailscale serve --bg 3457` ([phone access](phone.md)). The Docket apps can sign in through either
port.

## Docker

Included. The `web` service (the connector image, run as `/app/web/docket-web`) serves the site's root, and Caddy
sends everything except the connector's routes to it. If `web` isn't running, Caddy falls back to Vikunja directly.

It needs a connector image that includes docket-web (0.3.0 or newer). If you pin `DOCKET_VERSION` to an older one,
remove the `web` service or update the pin.

## Your own Vikunja

Run `docket-web` with `DOCKET_VIKUNJA_URL` set and route your site's root to it, keeping the connector's routes
(`/mcp`, `/oauth/*`, `/.well-known/oauth-*`, `/api/tools/*`, `/openapi.json`) going to the connector.

## Settings

| Variable | Default | What |
|---|---|---|
| `DOCKET_VIKUNJA_URL` | `http://127.0.0.1:3456` | The Vikunja server to forward to |
| `DOCKET_WEB_URLS` | `http://127.0.0.1:3457` | Address(es) to listen on, separated by `;` |
| `DOCKET_WEB_SKIN_DIR` | (none) | Serve the skin from this folder instead of the built-in copy (for working on the skin) |

## How it works

It's a small reverse proxy in front of Vikunja. Requests, the WebSocket, uploads and downloads, and public
`/share/...` links pass straight through; only the HTML page and the web manifest are rewritten to add the skin
(`/docket-skin/skin.css` and `skin.js`, built into the program).

Signing in, the API and the Docket apps work the same through it, since it only changes how pages look. The page
reaches the API at whatever address you opened it on, so Vikunja's `service.publicurl` can stay as it is.
