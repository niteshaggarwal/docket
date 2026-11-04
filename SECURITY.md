# Security

## Reporting a problem

Please don't open a public issue for security problems. Use GitHub's
[private vulnerability reporting](https://github.com/quartifex/docket/security/advisories/new) for this repository.
Include what you found, how to reproduce it, and the version. We aim to reply within 5 working days and to fix
confirmed issues in the next release, crediting you unless you'd rather we didn't.

Problems in Vikunja itself should go to the [Vikunja project](https://vikunja.io/contact/).

## Supported versions

Security fixes go into the latest release. Please upgrade before reporting.

## How Docket protects your data

- **Your server, your data.** Docket talks only to the Vikunja server you sign in to. It sends nothing to the
  developers: no analytics, no telemetry.
- **Tokens, not passwords.** Setup signs in once and creates API tokens; passwords are never stored. On Windows,
  tokens are encrypted with DPAPI for your user account. On Android they're in the system's secure storage.
- **Least privilege for AI.** The AI connector gets its own token with no delete, sharing, team, webhook, migration
  or CalDAV rights, and none of its tools delete. Revoke it in Vikunja under Settings > API Tokens.
- **Remote connector.** Every request to `/mcp` and `/api/tools/*` needs the connector key or an OAuth token.
  The key is compared in constant time and stored only as a hash in memory; OAuth tokens are stored only as SHA-256
  hashes and are all revoked when the key changes; authorization codes are single-use, expire in 2 minutes and
  require PKCE (S256). OAuth only returns to allow-listed hosts (the Claude and ChatGPT sites by default) or
  localhost, and the sign-in page shows where access will go and can't be framed. Ten wrong keys from one address
  in 15 minutes pause sign-in. Registration is size- and count-limited.
- **Proxies.** Forwarded headers are trusted only from loopback and private networks; the connector listens on
  127.0.0.1 unless told otherwise.
- **Network.** The Windows install listens on 127.0.0.1 only. The Docker stack exposes only Caddy (HTTPS with
  automatic certificates and HSTS).

## Hardening checklist for servers

- Use a long random `DOCKET_MCP_KEY` (`openssl rand -hex 32`) and keep `.env` readable only by you.
- Close Vikunja sign-ups after creating your accounts, and turn on two-factor sign-in.
- Keep Docker images up to date (`docker compose pull && docker compose up -d`).
- Prefer private access (Tailscale) when you don't need web assistants like claude.ai or ChatGPT.
- Back up regularly ([how](docs/self-hosting.md)).
