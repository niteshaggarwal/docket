# Security

Docket is built so your tasks stay yours, and so an AI can help without being able to do damage. This page explains
what protects your data and how to keep a server safe. To report a security problem, see
[SECURITY.md](../../SECURITY.md) (please report privately, not as a public issue).

## How Docket protects your data

- **Your server, your data.** Docket talks only to the Vikunja server you sign in to. It sends nothing to the
  developers: no analytics, no telemetry.
- **Tokens, not passwords.** Setup signs in once and creates API tokens; passwords are never stored. On Windows,
  tokens are encrypted with DPAPI for your Windows account. On Android they're in the system's secure storage.
- **Least privilege for AI.** The AI connector gets its own token with no delete, sharing, team, webhook, migration
  or CalDAV rights, and none of its tools delete. You can revoke it in Vikunja under Settings > API Tokens.
- **Private by default on Windows.** The Windows install listens on `127.0.0.1` only. Nothing on your network can
  reach it until you choose to share it (for example with Tailscale).
- **HTTPS on servers.** The Docker stack exposes only Caddy, with automatic certificates and HSTS.

## The remote connector

When the connector is reachable from the internet (for claude.ai and ChatGPT):

- Every request to `/mcp` and `/api/tools/*` needs the **connector key** or an OAuth token made with it.
- OAuth tokens are stored only as SHA-256 hashes, and changing the key revokes them all.
- OAuth sign-ins only return to the Claude and ChatGPT sites (and localhost) unless you add hosts with
  `DOCKET_OAUTH_REDIRECT_HOSTS`, so a fake "client" can't collect your key.
- The sign-in page shows where access will go and can't be framed. Only approve a connection you started yourself.
- Ten wrong keys from one address in 15 minutes pause sign-in.

## Checklist for servers

- Use a long random `DOCKET_MCP_KEY` (`openssl rand -hex 32`; `setup.sh` does this) and keep `.env` readable only by
  you (`chmod 600 .env`).
- Close Vikunja sign-ups after creating your accounts (`VIKUNJA_REGISTRATION=false`).
- Turn on two-factor sign-in in Vikunja's settings.
- Keep images up to date: `docker compose pull && docker compose up -d`.
- Prefer private access (Tailscale) when you don't need web assistants like claude.ai or ChatGPT.
- Back up regularly ([how](backups.md)).

## Revoking access

| To cut off | Do this |
|---|---|
| Every AI connected over the internet | Change `DOCKET_MCP_KEY` and restart the connector (`docker compose up -d`) |
| One AI app | Remove the Docket connector in that app |
| The AI connector's token | Delete "Docket AI connector" in Vikunja, Settings > API Tokens |
| A lost phone | Delete that phone's token in Vikunja, Settings > API Tokens, and change your password |

## Sharing and public links

A public link lets anyone with the link see the list, so add a password if it holds anything private. Remove links you
no longer need from the list's **Share** options. See [people, teams and sharing](../using/people-and-sharing.md).
