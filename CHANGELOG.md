# Changelog

## 0.3.0 (unreleased)

First public release.

- Windows app (WinUI 3) with the Microsoft To Do feature set: My Day with suggestions, Important, Planned, All,
  Completed, lists and groups, steps, reminders, repeats, categories, files, notes, boards, undo.
- Android app (.NET MAUI) with the same features and Fluent design.
- `docket` CLI with `--json` output and Windows reminder notifications.
- AI connector `docket-mcp`: 20 tools over MCP (stdio and Streamable HTTP), REST with generated OpenAPI 3.1,
  built-in OAuth 2.1 (dynamic client registration, PKCE) for claude.ai and ChatGPT, `docket-mcp setup` for a
  no-delete token. Runs on Windows, Linux and macOS.
- Self-hosting: Windows one-click installer (no admin), Docker Compose stack with Caddy HTTPS.
- Releases built by GitHub Actions: Windows x64/arm64 zip, Android APK/AAB, connector binaries, container image.
