# Uninstalling

## Windows

Run the uninstaller from PowerShell:

```powershell
powershell -ExecutionPolicy Bypass -File "$env:LOCALAPPDATA\Programs\Docket\uninstall.ps1"
```

It removes the scheduled tasks, the programs, the PATH entry, the Start menu shortcut and the Claude Code connector.
**Your tasks are kept** in `%LOCALAPPDATA%\Docket\server`, so installing again brings everything back.

To delete your tasks too, add `-RemoveData`:

```powershell
powershell -ExecutionPolicy Bypass -File "$env:LOCALAPPDATA\Programs\Docket\uninstall.ps1" -RemoveData
```

That deletes the server data and Docket's settings. Backups in `Documents\Docket\backups` are always kept; delete
that folder yourself if you want them gone.

Afterwards:

- If you added Docket to **Claude Desktop**, LM Studio or another AI app, remove the `docket` entry from its MCP
  settings (Claude Desktop: Settings > Developer > Edit Config).
- If you used Tailscale for your phone, run `tailscale serve reset` on the PC.

## Docker server

In the folder with `compose.yaml`:

```sh
docker compose down          # stops the stack, keeps your data in Docker volumes
docker compose down -v       # stops it and deletes all data (tasks, files, certificates)
```

Make a [backup](backups.md#docker-server) first if you might want your tasks later.

## Android

Uninstall the app as usual. Its token stays valid on the server until you delete it in Vikunja (Settings >
API Tokens), or uninstall the server.

## AI connections

- **claude.ai / ChatGPT:** remove the Docket connector in their settings.
- **All AI at once (server):** change `DOCKET_MCP_KEY` in `.env` and run `docker compose up -d`.
- **The connector's token:** delete "Docket AI connector" in Vikunja under Settings > API Tokens.
