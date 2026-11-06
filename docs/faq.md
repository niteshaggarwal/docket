# Frequently asked questions

Short answers to the questions people ask most. For step-by-step fixes, see [troubleshooting](troubleshooting.md).

## Is Docket free?

Yes. Docket is open source under the [AGPL-3.0](../LICENSE) licence, and so is Vikunja. If you use a cloud AI
(Claude, ChatGPT or Gemini) for planning, that provider may charge for its API; local AI with Ollama is free.

## Where are my tasks stored?

On the Vikunja server you set up. With the Windows installer that's a database in
`%LOCALAPPDATA%\Docket\server` on your PC; with Docker it's in Docker volumes on your server. Nothing is stored
by Docket's developers, and the apps send no analytics or telemetry.

## Do I need a server or a domain name?

No. The Windows installer runs the server on your own PC, reachable only from that PC. You need a domain (or a
tunnel) only if you want claude.ai or ChatGPT to reach your tasks from the internet. See
[public address for AI](setup/public-address.md).

## Can I use it on my phone?

Yes, with the Android app. If your server is on your Windows PC, the easy and private way is
[Tailscale](setup/phone.md). There's no iPhone app yet; on iPhone you can use [Docket web](using/web.md) in the
browser.

## Is there a Mac or Linux app?

Not a native one. On macOS and Linux you can use [Docket web](using/web.md) in a browser, the AI connector
(`docket-mcp`) and the Android app against a [Docker server](setup/docker.md).

## Can the AI delete my tasks?

No. None of the connector's tools delete anything, and the key (token) it uses has no delete, sharing or team
rights either. Deleting stays in the Docket apps, where you can undo it. See [how AI works with Docket](ai/README.md).

## Which AI should I use?

- **Claude** or **ChatGPT** when you want the most capable help, in their apps.
- **A local model** (LM Studio, Ollama) when you want everything to stay on your computer.

You can connect several at once; they all see the same tasks. The [AI guide](ai/README.md) has a table.

## What's the difference between steps and subtasks?

Steps are a simple checklist inside one task (like Microsoft To Do). Subtasks are full tasks of their own, with
their own dates, star, reminders and subtasks, nested as deep as you like; a task can even sit under several
parents. Turn old steps into subtasks with one click, or `docket convert <id>`. See
[the Windows app](using/windows.md#subtasks-and-steps).

## Can other people use my Docket?

Yes. Add people on the **People** page, share a list with them or with a team, and assign tasks. See
[people, teams and sharing](using/people-and-sharing.md).

## Can I still use Vikunja's own apps?

Yes. Docket stores everything in plain Vikunja, so Vikunja's web app (or [Docket web](using/web.md), its reskin)
and API see the same tasks. A few Docket repeats (weekdays, yearly, every N months) only advance when you complete
the task in a Docket app. Details in the [developer API](api.md#vikunja-api).

## How do I update Docket?

Run the new release's `install.ps1` on Windows, or `docker compose pull && docker compose up -d` on a server.
See [updating](setup/updating.md).

## How do I move from my PC to a server?

Make a backup on the PC, set up the [Docker server](setup/docker.md), then restore the backup there. See
[backups and restore](setup/backups.md).

## Is Docket made by Microsoft or Vikunja?

No. Docket is an independent project by Quartifex. It isn't affiliated with Vikunja, Microsoft, Anthropic or OpenAI.
