# Backups and restore

All your tasks, lists, files and people live in Vikunja, so backing up Vikunja backs up Docket. Vikunja's `dump`
command makes one zip with the database, your attachments and its settings.

## Windows

The installer already backs up for you:

- **When:** every night at 11 pm (scheduled task **Vikunja backup**). If the PC is off, it runs at the next chance.
- **Where:** `Documents\Docket\backups`, files named `vikunja-dump_<date>.zip`. The newest 14 are kept.
- **Make one now:** Task Scheduler > Task Scheduler Library > **Docket** > **Vikunja backup** > **Run**.

`Documents` is often backed up by OneDrive, which gives you an off-PC copy. The live database itself is kept outside
OneDrive on purpose.

### Restore on Windows

1. Quit the Docket app.
2. Stop the server: Task Scheduler > **Docket** > right-click **Vikunja server** > **End**, and also **Disable** it for
   now so the 5-minute check doesn't start it again while you restore.
3. In PowerShell, run (with the backup you want):

   ```powershell
   $server = "$env:LOCALAPPDATA\Docket\server"
   & "$server\bin\vikunja.exe" --config "$server\config.yml" restore "$env:USERPROFILE\Documents\Docket\backups\vikunja-dump_XXXX.zip"
   ```

4. **Enable** the **Vikunja server** task again and **Run** it, then open Docket.

> [!WARNING]
> Restoring replaces everything on the server with the backup. Make a fresh backup first if you might want today's
> tasks back.

## Docker server

Make a backup inside the container, then copy it out:

```sh
docker compose exec vikunja /app/vikunja/vikunja dump --path /app/vikunja/files/backups
docker compose cp vikunja:/app/vikunja/files/backups ./backups
```

Run this from cron (for example nightly) and copy `./backups` somewhere off the server.

### Restore on Docker

Copy the zip into the container and run `restore`, then restart Vikunja:

```sh
docker compose cp ./backups/vikunja-dump_XXXX.zip vikunja:/app/vikunja/files/restore.zip
docker compose exec vikunja /app/vikunja/vikunja restore /app/vikunja/files/restore.zip
docker compose restart vikunja
```

The connector's volume holds only its token and OAuth sign-ins. You don't need to back it up: run
`docker compose exec connector docket-mcp setup` again and reconnect your AI apps.

## Moving from your PC to a server

1. On the PC, make a backup now (see above).
2. Set up the [Docker server](docker.md) and create an account.
3. Restore the PC's backup on the server (above). Your account from the PC comes along with it.
4. Point the apps at the server: Android, sign in with `https://your-domain`; Windows, run
   `setup-tokens.ps1 -ServerUrl https://your-domain`.

Vikunja's [backup documentation](https://vikunja.io/docs/backups/) has more detail, including database-specific options.
