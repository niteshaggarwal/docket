# Developer API

Four ways to work with Docket from code, from highest to lowest level:

| Interface | Best for | Auth |
|---|---|---|
| [REST tools + OpenAPI](#rest-tools-and-openapi) | GPT actions, automations (n8n, Zapier, Make), quick scripts | Connector key (Bearer) |
| [MCP](#mcp) | AI agents and MCP clients | Connector key or OAuth |
| [`docket` CLI with `--json`](#cli) | Shell scripts on Windows | Docket settings |
| [Vikunja REST API](#vikunja-api) | Anything the tools don't cover | Vikunja API token |

The first two are served by `docket-mcp --http` (the [Docker server](self-hosting.md) runs it). They share one
implementation, so they always offer the same 20 tools with the same behaviour as the Docket apps: My Day, steps,
repeats and quick-add words work the same way everywhere. None of them delete.

## REST tools and OpenAPI

```
POST /api/tools/{tool}
Authorization: Bearer <connector key>
Content-Type: application/json

{ ...arguments }
```

Replies:

| Status | Body | Meaning |
|---|---|---|
| 200 | `{"result": "text"}` | Done. The text is human-readable, with task ids as `#123` |
| 400 | `{"error": "..."}` | Missing or wrongly typed argument |
| 401 | `{"error": "unauthorized"}` | Missing or wrong key |
| 404 | `{"error": "..."}` | No such tool |
| 422 | `{"error": "..."}` | Docket couldn't do it, e.g. "No task #99" |

`GET /openapi.json` (no auth) describes every tool, its arguments, types, defaults and which ones change data
(`x-openai-isConsequential`). It's generated from the code, so it's always current.

Examples:

```sh
KEY=your-connector-key; BASE=https://tasks.example.com
curl -s $BASE/api/tools/list_tasks -H "Authorization: Bearer $KEY" -H 'Content-Type: application/json' -d '{"view":"today"}'
curl -s $BASE/api/tools/add_task -H "Authorization: Bearer $KEY" -H 'Content-Type: application/json' \
  -d '{"title":"Renew passport friday !3","project":"Home","labels":["Admin"]}'
curl -s $BASE/api/tools/complete_task -H "Authorization: Bearer $KEY" -H 'Content-Type: application/json' -d '{"ids":[42]}'
curl -s $BASE/api/tools/set_reminder -H "Authorization: Bearer $KEY" -H 'Content-Type: application/json' \
  -d '{"id":42,"times":["tomorrow 9:00"]}'
```

```python
import requests
s = requests.Session()
s.headers["Authorization"] = "Bearer your-connector-key"
r = s.post("https://tasks.example.com/api/tools/my_day", json={"suggestions": True})
print(r.json()["result"])
```

### Tool reference

Arguments in **bold** are required. Dates accept `2026-11-20`, `2026-11-20T15:00`, `today`, `tomorrow`, weekdays
(`fri`), `in 3 days`, `20/11` (day/month) or `none` to clear. Lists (projects) can be given by name or id.

| Tool | Arguments | Changes data |
|---|---|---|
| `list_tasks` | `view` (today, upcoming, overdue, open, done, my_day, important, planned, all, completed), `project`, `search`, `limit` (1-200) | no |
| `get_task` | **`id`** | no |
| `add_task` | **`title`** (quick-add words allowed), `project`, `due`, `priority` (0-5), `description`, `labels[]` | yes |
| `update_task` | **`id`**, `title`, `description`, `due`, `priority`, `project` | yes |
| `complete_task` | **`ids[]`**, `done` (default true; false reopens) | yes |
| `move_task` | **`id`**, **`column`** | yes |
| `add_comment` | **`id`**, **`text`** | yes |
| `add_label` | **`id`**, **`label`** | yes |
| `set_categories` | **`id`**, **`categories[]`** | yes |
| `list_projects` | none | no |
| `get_board` | **`project`** | no |
| `create_project` | **`title`**, `parent` | yes |
| `create_list` | **`title`**, `group` | yes |
| `my_day` | `suggestions` | no |
| `set_my_day` | **`ids[]`**, `in_my_day` | yes |
| `set_important` | **`ids[]`**, `important` | yes |
| `steps` | **`id`**, **`action`** (add, complete, uncomplete, remove), `text`, `index` (1-based) | yes |
| `set_repeat` | **`id`**, **`repeat`** (daily, weekdays, weekly, monthly, yearly, "every N days/weeks/months/years", none) | yes |
| `set_reminder` | **`id`**, **`times[]`** (empty clears) | yes |
| `set_notes` | **`id`**, **`notes`** | yes |

Quick-add words in titles: `today`, `tomorrow`, a weekday, `in 3 days`, `20/11` set the due date; `!1` to `!5` set the
priority. They're removed from the title.

## MCP

Endpoint: `POST /mcp`, [Streamable HTTP](https://modelcontextprotocol.io/specification/2025-06-18/basic/transports),
stateless (no session id needed). Or run `docket-mcp` with no arguments for stdio.

```sh
curl -s https://tasks.example.com/mcp -H "Authorization: Bearer $KEY" \
  -H 'Content-Type: application/json' -H 'Accept: application/json, text/event-stream' \
  -d '{"jsonrpc":"2.0","id":1,"method":"tools/call","params":{"name":"list_tasks","arguments":{"view":"overdue"}}}'
```

Tool annotations follow the MCP spec: read tools are `readOnlyHint: true`; nothing is `destructiveHint: true`.
Errors come back as tool results with `isError: true` and a readable message.

### OAuth

For clients that need it, the connector is its own OAuth 2.1 authorization server:

| Endpoint | |
|---|---|
| `GET /.well-known/oauth-protected-resource` | Resource metadata (RFC 9728); also linked from the 401 `WWW-Authenticate` header |
| `GET /.well-known/oauth-authorization-server` | Server metadata (RFC 8414) |
| `POST /oauth/register` | Dynamic client registration (RFC 7591). Redirects: localhost, or https on a `DOCKET_OAUTH_REDIRECT_HOSTS` host; up to 5 per client; 200 clients kept |
| `GET/POST /oauth/authorize` | Sign-in page: the user types the connector key once. PKCE S256 required |
| `POST /oauth/token` | `authorization_code` and `refresh_token` grants. Access tokens last 12 hours, refresh tokens 90 days and rotate on use |

Only SHA-256 hashes of tokens are stored (`oauth.json` in the data folder), and a new connector key revokes them all.
Ten wrong keys from one address (or 100 overall) in 15 minutes pause sign-in. Token responses are `Cache-Control: no-store`,
and the sign-in page can't be framed.

## CLI

The Windows `docket` command prints JSON with `--json` on any listing:

```powershell
docket today --json | ConvertFrom-Json | Where-Object priority -ge 3
docket planned --json        # [{ group: "Today", task: {...} }, ...]
docket show 42 --json
```

Task objects look like:

```json
{
  "id": 42, "title": "Send invoice", "done": false, "project": "Work", "projectId": 3,
  "due": "2026-11-20T17:00:00+00:00", "dueText": "Fri 20 Nov", "priority": 3, "column": "To-Do",
  "important": true, "myDay": true,
  "steps": [{ "Title": "Price it", "Done": true }], "repeat": "every week",
  "nextReminder": "2026-11-20T09:00:00+00:00", "categories": ["Admin"]
}
```

Run `docket --help` for every command. Exit code is 0 on success, 1 on errors, 2 on wrong usage.

## Vikunja API

Docket stores everything in plain Vikunja, so the [Vikunja API](https://vikunja.io/docs/api) (`/api/v1`, Swagger at
`/api/v1/docs` on your server) sees the same data. Make a token under **Settings > API Tokens** with only the
permissions you need. How Docket maps its features onto Vikunja:

| Docket | In Vikunja |
|---|---|
| Lists and groups | Projects; a group is a parent project |
| Important | `is_favorite` |
| My Day | The label "My Day"; its description holds the day it was last reset (`Docket My Day yyyy-MM-dd`) |
| Categories | Other labels |
| Steps | A task list (`<ul data-type="taskList">`) at the start of the description |
| Daily, weekly, every N days/weeks, monthly repeats | Vikunja's own `repeat_after` / `repeat_mode` |
| Weekdays, yearly, every N months/years repeats | A label starting with "↻" (handled when completed through Docket) |
| Reminders | `reminders` |
| Board columns | Kanban buckets of the project's Kanban view |

Changing tasks from another tool is fine; avoid renaming the "My Day" label or the "↻" labels.

## Building on Docket.Core

`src/Docket.Core` (.NET 10, no UI or platform code) holds the Vikunja client and all of Docket's behaviour
(`DocketService`, `QuickAdd`, `RepeatRule`, `TaskNotes`, `TaskFormat`). The apps, CLI and connector are thin layers on
it; reference it from your own .NET tool to get identical behaviour.
