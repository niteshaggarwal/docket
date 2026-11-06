# Choose a setup

Docket keeps your tasks in [Vikunja](https://vikunja.io), an open-source task server, and adds the Docket apps and
an AI connector on top. You can run it three ways. All three give you the same apps and the same features.

| | Windows PC | Docker server | Your own Vikunja |
|---|---|---|---|
| Best for | Just you, on one PC (phone through Tailscale) | Always on: a VPS, NAS or home server | You already run Vikunja |
| You get | Vikunja on 127.0.0.1, the app, CLI, Docket web and the local AI connector | Vikunja, Docket web, the remote AI connector and automatic HTTPS | The Docket apps and connector |
| claude.ai and ChatGPT | Only through a [tunnel](setup/public-address.md) | Yes | Run the connector yourself |
| Skill needed | Run one script | Basic Linux and Docker | Reverse proxy setup |
| Guide | [Windows PC](setup/windows.md) | [Docker server](setup/docker.md) | [Existing Vikunja](setup/existing-vikunja.md) |

Claude Desktop, Claude Code, local models and the Docket apps work with every setup.

## Windows PC

Download the Windows zip from [Releases](https://github.com/niteshaggarwal/docket/releases), right-click
`install.ps1` and choose **Run with PowerShell**. It installs everything for your Windows account, without admin
rights. Full guide, including what each question means: [Windows PC](setup/windows.md).

### If the server stops

The apps, the CLI and the AI connector restart a stopped server on your PC by themselves, and Windows checks it
every 5 minutes as a failsafe. Details: [if the server stops](setup/windows.md#if-the-server-stops).

### Reach it from your phone

The Windows install listens only on your PC. Use [Tailscale](setup/phone.md) to reach it privately from your phone:
`tailscale serve --bg 3457` on the PC, then sign in to Docket for Android with the address it gives you.

## Docker server

One script on any Linux machine with Docker sets up Vikunja, Docket web, the AI connector and HTTPS certificates.
Full guide: [Docker server](setup/docker.md).

### No public IP or domain

claude.ai and ChatGPT need a public HTTPS address for the connector. Without opening ports, use Tailscale Funnel or
a Cloudflare Tunnel: [public address for AI](setup/public-address.md).

## Your own Vikunja

Docket works with any Vikunja 2.x server: [existing Vikunja](setup/existing-vikunja.md).

## Docket web skin

`docket-web` serves Vikunja's web app with the Docket look. It's included in the Windows and Docker setups:
[Docket web](setup/docket-web.md).

## Looking after it

- [Updating](setup/updating.md) Docket and Vikunja
- [Backups and restore](setup/backups.md)
- [Uninstalling](setup/uninstalling.md)
- [Security](setup/security.md)
