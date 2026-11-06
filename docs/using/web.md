# Docket web

Docket web is Vikunja's web app with the Docket look, for any computer or phone with a browser: a Mac, a Linux PC,
an iPhone, or someone else's computer.

| Your setup | Open |
|---|---|
| Windows PC | `http://localhost:3457` (the app's **Open in browser** goes there) |
| Windows PC, from your phone | Your Tailscale address ([phone access](../setup/phone.md)) |
| Docker server | `https://your-domain` |

Sign in with your Vikunja username and password.

## What's different from the apps

Docket web is Vikunja's own interface, reskinned, so it works a little differently from the Docket apps:

- It has Vikunja's views: list, Gantt, table and Kanban, plus **Upcoming**, teams, labels, sharing and settings.
- **My Day** shows as a label called "My Day", and **Important** as a favourite (star).
- Docket's quick-add words and `/` commands are a Docket app feature; Vikunja has its own quick-add magic
  (`*label`, `+list`, `!priority`, dates).
- Repeats that only Docket understands (weekdays, yearly, every N months or years; they carry a "↻" label) don't move
  forward when completed here. Complete those in a Docket app.

Everything else, including tasks, subtasks, files and sharing, is the same data the apps see.

## Settings and API tokens

Vikunja's settings live here too: your profile, password, **two-factor sign-in**, and **Settings > API Tokens**,
where you can see and revoke the tokens Docket made for the app, the AI connector and each phone.

Running and configuring Docket web: [setup guide](../setup/docket-web.md).
