# Changelog

## 0.3.0 (unreleased)

First public release.

### People, sharing and teams
- People are accounts on your Docket server, added by email or as local people (the Windows app creates them on a
  local server). Assign tasks to them; assigning shares the list automatically when needed.
- Share a list with a person or a team (can view / can edit / can edit and share) or through a public link, with an
  optional password. Teams and their members are managed on the People page.

### Subtasks, history and icons
- Subtasks are real tasks, nested to any depth, and a task can sit under several parents. Every subtask has its own
  star, dates, reminders and subtasks. Old checklist steps turn into subtasks with one click (`docket convert`).
- Task history (created by and when, last changed, completed) and row icons for notes, files, reminders, repeats,
  subtask progress, priority and assignees.

### Add bar, commands and AI
- The add bar sits at the bottom. `/s` searches, `/x` marks the matches done, `/m` adds them to My Day, `/i` stars
  them, after a confirmation.
- AI planning (`/ai`, or the sparkle button): describe what you need and get a nested plan to confirm. Uses Ollama on
  your PC (it picks an installed model) or a Claude, ChatGPT or Gemini API key, stored encrypted.

### Views, boards and list style
- Overview dashboard, Upcoming, Open tasks, priorities everywhere, start and end dates.
- Every list switches between List, Table, Gantt (timeline on Android) and Board. Board columns get their own colour
  and icon; add as many as you like. To-Do and Done are permanent but can be renamed.
- Each list can have an icon (Fluent glyph or emoji) and a background: colour, gradient, accent or image.

### Web
- `docket-web`: Vikunja's own web app with the Docket look in light and dark and no Vikunja branding, nothing removed.
  Runs at http://localhost:3457 on Windows and as the `web` service in Docker.

### Reliability
- If the task server on your PC has stopped, the Windows app, the CLI and the AI connector start it again and retry
  before showing an error. If it still won't start, the app offers "Start server and retry".
- The server, Docket web and reminder tasks re-run every 5 minutes as a failsafe (a running copy is left alone), and
  the reminders watcher keeps going through outages and restarts the server.

### Also in this release

- Windows app (WinUI 3) with the Microsoft To Do feature set: My Day with suggestions, Important, Planned, All,
  Completed, lists and groups, steps, reminders, repeats, categories, files, notes, boards, undo.
- Android app (.NET MAUI) with the same features and Fluent design.
- `docket` CLI with `--json` output and Windows reminder notifications.
- AI connector `docket-mcp`: 20 tools over MCP (stdio and Streamable HTTP), REST with generated OpenAPI 3.1,
  built-in OAuth 2.1 (dynamic client registration, PKCE) for claude.ai and ChatGPT, `docket-mcp setup` for a
  no-delete token. Runs on Windows, Linux and macOS.
- Self-hosting: Windows one-click installer (no admin), Docker Compose stack with Caddy HTTPS.
- Releases built by GitHub Actions: Windows x64/arm64 zip, Android APK/AAB, connector binaries, container image.
