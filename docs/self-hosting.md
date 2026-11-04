# Self-hosting Docket

Docket keeps your tasks in [Vikunja](https://vikunja.io), an open-source task server, and adds the Docket apps and
an AI connector on top. You can run it three ways:

| | Windows PC | Docker server | Your own Vikunja |
|---|---|---|---|
| For | Just you, on one PC (phone via Tailscale) | Always-on: a VPS, NAS or home server | You already run Vikunja |
| Gives you | Vikunja on 127.0.0.1, the app, CLI and local AI connector | Vikunja + the remote AI connector + automatic HTTPS | The Docket apps and connector |
| Remote AIs (claude.ai, ChatGPT) | Only through a tunnel | Yes | Run the connector yourself |
| Guide | [Windows](#windows-pc) | [Docker](#docker-server) | [Existing Vikunja](#your-own-vikunja) |

## Windows PC

1. Download `docket-<version>-windows-x64.zip` (or `arm64`) from
   [Releases](https://github.com/niteshaggarwal/docket/releases) and unzip it.
2. Right-click `install.ps1` > **Run with PowerShell** (or `powershell -ExecutionPolicy Bypass -File install.ps1`).
3. When your browser opens Vikunja, create your account, return to the window and press Enter. Sign in once more
   there; the installer makes the app's token and the AI connector's (no-delete) token, connects Claude Desktop and
   Claude Code if you have them, and offers to close sign-ups.

No admin rights are needed. What it sets up, all for your Windows account only:

| What | Where |
|---|---|
| Docket app, `docket` CLI, `docket-mcp` | `%LOCALAPPDATA%\Programs\Docket` (CLI added to your PATH; Start menu shortcut) |
| Vikunja and your data | `%LOCALAPPDATA%\Docket\server` (outside OneDrive: a live database and file sync don't mix) |
| Backups | `Documents\Docket\backups`, nightly at 11 pm, newest 14 kept |
| Scheduled tasks | Task Scheduler > `Docket`: "Vikunja server" (at sign-in), "Vikunja backup" (nightly) |
| Settings and tokens | `%LOCALAPPDATA%\Docket\settings.json` (tokens encrypted with Windows DPAPI) |

Options: `-Reminders` adds reminder notifications from sign-in; `-Port 3500` changes the port;
`-TimeZone Europe/London` skips the time-zone question; `-NoScheduledTasks -NoPath -NoStartMenu` for a portable setup.
Run it again to upgrade (your data stays). `uninstall.ps1` removes it (add `-RemoveData` to delete your tasks too).

From source instead of a release: clone the repo, install the .NET 10 SDK, then run `deploy\windows\install.ps1`;
it builds the app first.

### Reach it from your phone

Vikunja listens on 127.0.0.1 only. The simplest safe way to reach it from your phone is
[Tailscale](https://tailscale.com) (free for personal use):

1. Install Tailscale on the PC and the phone and sign in to the same account on both.
2. On the PC: `tailscale serve --bg 3456`. That gives Vikunja an HTTPS address like `https://my-pc.tail1234.ts.net`,
   reachable only from your own devices.
3. In Docket for Android, sign in with that address.

To let claude.ai or ChatGPT reach a Windows install, see [No public IP or domain](#no-public-ip-or-domain).

## Docker server

Runs Vikunja, the Docket AI connector, and [Caddy](https://caddyserver.com) for automatic HTTPS certificates.

**You need** a Linux machine (any VPS with 1 GB RAM is plenty, or a NAS/home server) with Docker and the compose
plugin, and a domain name pointing at it with ports 80 and 443 open. No domain? See
[No public IP or domain](#no-public-ip-or-domain).

```sh
# From a release (uses the published connector image):
curl -fsSL -o docket-server.tar.gz https://github.com/niteshaggarwal/docket/releases/latest/download/docket-server-<version>.tar.gz
tar xzf docket-server.tar.gz && cd docket-server
./setup.sh

# Or from a clone (builds the connector image locally if it can't be pulled):
git clone https://github.com/niteshaggarwal/docket && cd docket/deploy/docker
./setup.sh
```

`setup.sh` asks for your domain and time zone, writes `.env` with fresh secrets, starts the stack, waits while you
create your account at `https://your-domain`, then runs `docket-mcp setup` so the connector gets its own no-delete
token, and offers to close sign-ups.

Prefer doing it by hand? `cp .env.example .env`, fill in the four values, `docker compose up -d`, create your account,
then `docker compose exec connector docket-mcp setup`, and set `VIKUNJA_REGISTRATION=false` followed by
`docker compose up -d`.

**Addresses**

| Address | What |
|---|---|
| `https://your-domain/` | Vikunja's web app; sign in here from the Docket apps |
| `https://your-domain/mcp` | MCP for AI assistants ([docs/ai](ai/README.md)) |
| `https://your-domain/openapi.json` | REST description for GPT actions and automations |
| `https://your-domain/connector/health` | Connector status |

**Upgrades:** `docker compose pull && docker compose up -d`. Pin versions with `DOCKET_VERSION` and `VIKUNJA_VERSION`
in `.env`; read the release notes before a Vikunja major-version jump.

**Backups:** your data is in Docker volumes. A consistent Vikunja dump:

```sh
docker compose exec vikunja /app/vikunja/vikunja dump --path /app/vikunja/files/backups
docker compose cp vikunja:/app/vikunja/files/backups ./backups
```

Restore with `vikunja restore <file>` (see [Vikunja's docs](https://vikunja.io/docs/backups/)). The connector's volume
holds only its token and OAuth sign-ins, which can be recreated.

**Security notes**
- Everything is HTTPS; Vikunja and the connector aren't exposed except through Caddy.
- Keep `.env` private (`chmod 600`, as `setup.sh` sets). It holds `VIKUNJA_SECRET` and `DOCKET_MCP_KEY`.
- Close sign-ups after creating your account(s). Turn on two-factor sign-in in Vikunja's settings.
- To revoke all AI access at once: change `DOCKET_MCP_KEY` in `.env`, then `docker compose up -d`. Every OAuth
  sign-in made with the old key stops working.
- OAuth sign-ins can only return to the Claude and ChatGPT sites (and localhost) unless you add hosts with
  `DOCKET_OAUTH_REDIRECT_HOSTS`, so a fake "client" can't collect your key.

### No public IP or domain

claude.ai and ChatGPT connect from the internet, so the connector needs a public HTTPS address. Without opening ports:

- **Tailscale Funnel** (good for the Windows install): run the connector over HTTP on the PC
  ([how](ai/local-ai.md#run-the-connector-over-http-on-your-network)) with
  `DOCKET_PUBLIC_URL=https://<machine>.<tailnet>.ts.net`, then `tailscale funnel --bg 8080`. Funnel provides the
  HTTPS certificate and makes only the connector public; Vikunja stays private. The connector key is what keeps it safe.
- **Cloudflare Tunnel** (good for the Docker server): run `cloudflared` with a hostname on your Cloudflare domain
  pointing at the stack, and set `DOCKET_DOMAIN`/`DOCKET_PUBLIC_URL` to that hostname. Cloudflare handles HTTPS, so
  point the tunnel at the services directly (`/mcp`, `/oauth/*`, `/.well-known/oauth-*`, `/api/tools/*`,
  `/openapi.json` to `connector:8080`, everything else to `vikunja:3456`) and leave Caddy out.

Local AIs and the Docket apps don't need any of this; plain Tailscale (private) is enough for them.

## Your own Vikunja

Docket works with any Vikunja 2.x server.
- **Apps:** sign in with your server's address (Android), or run `deploy\windows\setup-tokens.ps1 -ServerUrl https://your-vikunja`
  (Windows app and CLI).
- **Connector:** run `docket-mcp setup` with `DOCKET_VIKUNJA_URL` set, then `docket-mcp` (local) or
  `docket-mcp --http` (remote), or run the `ghcr.io/niteshaggarwal/docket-connector` image with `DOCKET_VIKUNJA_URL`,
  `DOCKET_MCP_KEY` and `DOCKET_PUBLIC_URL`, and route `/mcp`, `/oauth/*`, `/.well-known/oauth-*`, `/api/tools/*`
  and `/openapi.json` to it from your reverse proxy (see `deploy/docker/Caddyfile`).
