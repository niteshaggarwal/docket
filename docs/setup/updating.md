# Updating

New Docket versions are listed on [Releases](https://github.com/niteshaggarwal/docket/releases), with what changed
in the [changelog](../../CHANGELOG.md). Your tasks are kept when you update.

## Windows

1. Download the new `docket-<version>-windows-x64.zip` and unzip it.
2. Close the Docket app.
3. Run the new `install.ps1` the same way as the first time (right-click > **Run with PowerShell**).
4. When it asks `Docket is already connected to an account. Connect again (new tokens)?`, answer **N**.

The installer replaces the programs, keeps your data and settings, and updates Vikunja to the latest release. To pin
Vikunja to a version, add `-VikunjaVersion v2.7.1` (any tag from
[Vikunja's releases](https://github.com/go-vikunja/vikunja/releases)).

> [!TIP]
> A backup is made every night, but before a big update you can make one straight away: Task Scheduler > Docket >
> **Vikunja backup** > Run. See [backups](backups.md).

## Docker server

In the folder with `compose.yaml`:

```sh
docker compose pull && docker compose up -d
```

To stay on known versions, set `DOCKET_VERSION` (the connector and Docket web) and `VIKUNJA_VERSION` in `.env`,
then run the same command after changing them. Read Vikunja's release notes before a major-version jump, and make a
[backup](backups.md#docker-server) first.

After upgrading the connector, ChatGPT may not see new tools until you remove and add the connector again
([why](../ai/chatgpt.md#troubleshooting)).

## Android

Install the new APK from [Releases](https://github.com/niteshaggarwal/docket/releases) over the old one (or update from
the Play Store once Docket is listed there). You stay signed in.

## AI connector on macOS or Linux

Download the new `docket-mcp` for your platform from Releases and replace the old file. Restart the AI app that uses it.
