# Run a Docker server

An always-on Docket server, for a VPS, a NAS or a home server. One script sets up:

- **Vikunja**, the task server;
- **Docket web**, Vikunja's web app with the Docket look, at `https://your-domain/`;
- the **AI connector**, so claude.ai, ChatGPT and other assistants can reach your tasks, at `https://your-domain/mcp`;
- **[Caddy](https://caddyserver.com)**, which gets and renews HTTPS certificates automatically.

## What you need

- A Linux machine with [Docker](https://docs.docker.com/engine/install/) and the compose plugin. Any VPS with 1 GB of
  memory is plenty.
- A domain or subdomain (for example `tasks.example.com`) whose DNS points at the machine.
- Ports **80** and **443** open to the internet (Caddy needs them for certificates).

No domain, or behind a home router you can't open? See [public address for AI](public-address.md), or use
[Tailscale](phone.md) if you only need your own devices.

## Set it up

From a release (uses the published connector image):

```sh
curl -fsSL -o docket-server.tar.gz https://github.com/niteshaggarwal/docket/releases/latest/download/docket-server-<version>.tar.gz
tar xzf docket-server.tar.gz && cd docket-server
./setup.sh
```

Or from a clone of the repository (builds the connector image locally if it can't be pulled):

```sh
git clone https://github.com/niteshaggarwal/docket && cd docket/deploy/docker
./setup.sh
```

### What setup.sh asks

| Question | What to answer |
|---|---|
| `Domain for this server (DNS must point here, ports 80+443 open)` | Your domain, e.g. `tasks.example.com` |
| `Time zone [Europe/London]` | Press <kbd>Enter</kbd> to accept, or type yours (e.g. `Asia/Kolkata`) |
| `1. Open https://your-domain and create your account… Press Enter` | Open the address, create your account, then press <kbd>Enter</kbd>. The first certificate can take a minute |
| `2. Sign in once so the AI connector gets its own no-delete token` | Your Vikunja username and password. The connector saves a token without delete rights; the password isn't stored |
| `3. Close sign-ups so nobody else can make an account? [Y/n]` | **Y**. You can still add people from the Docket app |

`setup.sh` writes the settings to `.env` with fresh random secrets (readable only by you) and starts the stack.
At the end it prints your addresses and where the **connector key** is: `DOCKET_MCP_KEY` in `.env`. You'll type that
key once when you connect claude.ai or ChatGPT.

### By hand instead

```sh
cp .env.example .env            # fill in DOCKET_DOMAIN, TZ, VIKUNJA_SECRET and DOCKET_MCP_KEY
chmod 600 .env
docker compose up -d            # then create your account at https://your-domain
docker compose exec connector docket-mcp setup
# close sign-ups: set VIKUNJA_REGISTRATION=false in .env, then
docker compose up -d
```

Make the two secrets with `openssl rand -hex 32`.

## Addresses

| Address | What |
|---|---|
| `https://your-domain/` | [Docket web](docket-web.md). Sign in here from the Docket apps too |
| `https://your-domain/mcp` | MCP for AI assistants ([AI guides](../ai/README.md)) |
| `https://your-domain/openapi.json` | REST description for GPT actions and automations ([API](../api.md)) |
| `https://your-domain/connector/health` | Connector status; should say `"status":"ok"` |

Then:

- **Windows app:** in Docket's Settings > Connection, enter `https://your-domain` and a token, or run
  `setup-tokens.ps1 -ServerUrl https://your-domain` ([how](existing-vikunja.md#windows-app-and-cli)).
- **Android:** sign in with `https://your-domain`.
- **AI:** [Claude](../ai/claude.md), [ChatGPT](../ai/chatgpt.md), [others](../ai/other-clients.md).

## Settings in .env

| Setting | Meaning |
|---|---|
| `DOCKET_DOMAIN` | The public address of this server |
| `TZ` | Your time zone, used for due dates and reminders |
| `VIKUNJA_SECRET` | Secret for Vikunja's sign-in tokens |
| `DOCKET_MCP_KEY` | The connector key (24+ characters). Changing it signs out every AI |
| `VIKUNJA_REGISTRATION` | `true` while you create accounts, then `false` |
| `DOCKET_DEFAULT_LIST` | Optional: list id that new AI tasks go to when no list is named |
| `DOCKET_VERSION`, `VIKUNJA_VERSION` | Optional: pin image versions |
| `HTTP_PORT`, `HTTPS_PORT` | Optional: other ports for Caddy |

After changing `.env`, run `docker compose up -d`.

## Looking after it

- **Update:** `docker compose pull && docker compose up -d`. See [updating](updating.md).
- **Back up:** see [backups and restore](backups.md).
- **Logs:** `docker compose logs -f connector` (or `vikunja`, `web`, `caddy`).
- **Security:** see [security](security.md). Keep `.env` private; it holds your secrets.

## How it fits together

Caddy is the only service open to the internet. It sends the connector's routes (`/mcp`, `/oauth/*`,
`/.well-known/oauth-*`, `/api/tools/*`, `/openapi.json`, and `/connector/health` as `/health`) to the connector, and everything else
to Docket web, which passes requests on to Vikunja. If `web` isn't running, Caddy falls back to Vikunja directly. The
data lives in Docker volumes: `vikunja-db` (the database), `vikunja-files` (attachments and backups) and
`connector-data` (the connector's token and OAuth sign-ins).
