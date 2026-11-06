# Use your own Vikunja

Already run [Vikunja](https://vikunja.io)? Docket works with any Vikunja 2.x server. Your existing projects become
Docket lists, and nothing about your server has to change.

## Android app

Sign in with your server's address (for example `https://tasks.example.com`) and your username and password.
Docket makes a token for the phone; the password isn't stored.

## Windows app and CLI

Install Docket with the [Windows installer](windows.md), then connect it to your server instead of a local one:

```powershell
& "$env:LOCALAPPDATA\Programs\Docket\setup-tokens.ps1" -ServerUrl https://your-vikunja
```

It signs in once and makes two tokens: one for the app and CLI, and a no-delete one for the AI connector.
You can also enter the address and an API token by hand in **Settings > Connection**, or with
`docket login --server https://your-vikunja`.

If you don't want a local server at all, run the installer with `-NoScheduledTasks -SkipAccountSetup`, then run
`setup-tokens.ps1` as above.

## AI connector

The connector needs your server's address and a token. `docket-mcp setup` makes a no-delete token for you:

```sh
export DOCKET_VIKUNJA_URL=https://your-vikunja
export DOCKET_DATA_DIR=~/.config/docket
docket-mcp setup        # asks for your username and password once, saves the token
docket-mcp              # local (stdio), for Claude Desktop, Claude Code, LM Studio…
docket-mcp --http       # remote (HTTP), for claude.ai and ChatGPT behind your HTTPS proxy
```

For remote use, set `DOCKET_MCP_KEY` (24+ characters) and `DOCKET_PUBLIC_URL` (your public HTTPS address) too, or
run the container image instead:

```sh
docker run -d --name docket-connector -p 127.0.0.1:8080:8080 \
  -e DOCKET_VIKUNJA_URL=https://your-vikunja -e DOCKET_MCP_KEY=... -e DOCKET_PUBLIC_URL=https://your-vikunja \
  -v docket-connector:/data ghcr.io/niteshaggarwal/docket-connector
docker exec -it docket-connector docket-mcp setup
```

Then route these paths from your reverse proxy to the connector, and everything else to Vikunja (or Docket web):
`/mcp`, `/oauth/*`, `/.well-known/oauth-protected-resource*`, `/.well-known/oauth-authorization-server*`,
`/api/tools/*` and `/openapi.json`. [`deploy/docker/Caddyfile`](../../deploy/docker/Caddyfile) is a working example.

All connector settings are listed in [how AI works with Docket](../ai/README.md#settings-environment-variables).

## Docket web

To give your Vikunja the Docket look, run `docket-web` in front of it: see [Docket web](docket-web.md#your-own-vikunja).

## Good to know

- Docket stores its features in plain Vikunja: My Day is a label, Important is a favourite, steps are a checklist in
  the description. See [how Docket maps onto Vikunja](../api.md#vikunja-api).
- Avoid renaming the "My Day" label or the labels that start with "↻"; Docket uses them.
