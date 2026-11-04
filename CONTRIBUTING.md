# Contributing to Docket

Thanks for helping. Bug reports, docs fixes and focused pull requests are all welcome.

## Before you start

- For anything bigger than a small fix, open an issue or discussion first so we can agree on the approach.
- Docket follows Microsoft To Do's behaviour where it can. If a feature works differently there, say why in the PR.
- Behaviour belongs in `Docket.Core`, so the Windows app, Android app, CLI and connector stay identical. The UI
  projects should only present it.

## Set up

1. Install the [.NET 10 SDK](https://dotnet.microsoft.com/download). Windows is needed for `Docket.App` and `Docket.Cli`.
2. Run a throwaway Vikunja to test against (never your real account): download it from
   [Vikunja's releases](https://github.com/go-vikunja/vikunja/releases), start it with a config that sets
   `enableregistration: true`, and register a test user. `.github/workflows/ci.yml` shows the exact steps.
3. Point the tools at the test account with `DOCKET_SETTINGS` (Windows apps and CLI) or `DOCKET_VIKUNJA_URL` and
   `DOCKET_DATA_DIR` (connector), so your own settings are never touched.

## Checks

```sh
dotnet test tests/Docket.Core.Tests                    # unit tests
dotnet build src/Docket.Mcp -c Release                 # warnings are errors
node tools/http-smoke.mjs http://127.0.0.1:8080 KEY    # connector end-to-end (read-only)
node tools/mcp-smoke.mjs                               # connector over stdio (changes tasks: test account only)
```

CI runs the unit tests, a Linux end-to-end connector test against a fresh Vikunja, the Windows release build, and the
Docker image build on every pull request.

## Style

- C#: nullable on, warnings as errors, file-scoped namespaces, `var` where the type is obvious. Keep methods short.
- User-facing text: plain, short sentences, sentence case, no jargon. Errors say what happened and what to do.
- Connector tools: results are readable text with task ids as `#123`; never add a tool that deletes.
- PowerShell: must run on Windows PowerShell 5.1. Read and write files with `[IO.File]` and UTF-8 without BOM
  (`Get-Content`/`Set-Content` in 5.1 corrupt non-ASCII text).
- Shell scripts: POSIX `sh`, LF line endings.

## Pull requests

- One topic per PR, with a short description of what changed and how you tested it.
- Add or update tests for behaviour changes in `Docket.Core`.
- Update the docs when you change setup steps, tools or settings.
- By contributing you agree your contribution is licensed under the [AGPL-3.0](LICENSE).
