# Docket with AI assistants

Docket ships one connector, `docket-mcp`, that lets AI assistants read and change your tasks. It speaks the
[Model Context Protocol](https://modelcontextprotocol.io) (MCP), so any MCP-capable assistant can use it, and it also
offers the same tools as a plain REST API with an OpenAPI description for assistants that use "actions" instead.

**Nothing the connector does can delete.** None of its 26 tools delete, and the Vikunja token the setup scripts give it
has no delete, sharing or team rights either. Deleting and sharing stay in the Docket apps, where you can undo them.

## Pick your setup

| You use | How it connects | Guide |
|---|---|---|
| Claude Desktop, Claude Code | Local (stdio), on the same computer as your Docket settings | [Claude](claude.md#claude-desktop) |
| claude.ai on the web or phone | Remote MCP over HTTPS, OAuth sign-in | [Claude](claude.md#claudeai-web-desktop-and-phone) |
| ChatGPT | Remote MCP connector (developer mode) or a custom GPT with actions | [ChatGPT](chatgpt.md) |
| Gemini CLI, Copilot, Cursor, VS Code, other MCP clients | Local (stdio) or remote MCP | [Other MCP clients](other-clients.md) |
| LM Studio, Ollama, Jan, Open WebUI (local models) | Local (stdio), fully offline | [Local models](local-ai.md) |
| Your own scripts or agents | REST, OpenAPI, MCP or the `docket` CLI | [Developer API](../api.md) |

Using Claude? Add the [Docket skill](claude-skill.md) too: it teaches Claude how to use these tools well.

> [!NOTE]
> This page is about assistants working on your tasks from **their** apps. To plan tasks with AI from **inside** the
> Docket apps (the `/ai` command), see [AI planning in the app](../using/ai-planning.md).

## The two ways to run the connector

**Local (stdio).** The assistant starts `docket-mcp` itself and talks to it over standard input/output. Nothing listens
on the network. Use this for desktop apps on the computer where Docket is set up.

- Windows: the installer (or `deploy\windows\setup-tokens.ps1`) puts it at
  `%LOCALAPPDATA%\Programs\Docket\mcp\docket-mcp.exe` and connects it for you. It reads your Docket settings.
- macOS and Linux: download `docket-mcp` for your platform from the
  [releases](https://github.com/niteshaggarwal/docket/releases), then give it your Vikunja address and a token with
  environment variables (below).

**Remote (HTTPS).** `docket-mcp --http` serves MCP at `/mcp`, REST at `/api/tools/{name}`, an OpenAPI description at
`/openapi.json`, and an OAuth sign-in at `/oauth/*`. Web and phone assistants (claude.ai, ChatGPT) need this, because
they connect from the internet. The [Docker server](../self-hosting.md) runs it behind HTTPS for you.

Remote clients prove who they are in one of two ways, both tied to your **connector key** (`DOCKET_MCP_KEY`):
- **Bearer key:** send `Authorization: Bearer <connector key>`. For clients that let you set headers.
- **OAuth:** for clients that only do OAuth (claude.ai, ChatGPT). They register themselves, open Docket's sign-in page,
  you type the connector key once, and they get tokens that refresh on their own. The sign-in page shows which
  address access will go to; only approve a connection you started yourself. Changing the key revokes every OAuth sign-in.

## Settings (environment variables)

| Variable | Used by | Meaning |
|---|---|---|
| `DOCKET_VIKUNJA_URL` | both | Your Vikunja address, e.g. `http://localhost:3456` or `https://tasks.example.com` |
| `DOCKET_VIKUNJA_TOKEN` | both | A Vikunja API token. Prefer one without delete rights (`docket-mcp setup` makes one) |
| `DOCKET_VIKUNJA_TOKEN_FILE` | both | Read the token from this file instead (default: `DOCKET_DATA_DIR/vikunja-token`) |
| `DOCKET_DEFAULT_LIST` | both | List id new tasks go to when none is named |
| `DOCKET_DATA_DIR` | both | Where the connector keeps `state.json`, `oauth.json` and the token file (default `./data`) |
| `DOCKET_MCP_KEY` | remote | The connector key, 24+ characters (`openssl rand -hex 32`) |
| `DOCKET_PUBLIC_URL` | remote | The public HTTPS address, e.g. `https://tasks.example.com`, used in OAuth metadata |
| `DOCKET_MCP_URLS` | remote | Listen address (default `http://127.0.0.1:8080`; the Docker image uses `0.0.0.0:8080`) |
| `DOCKET_OAUTH_REDIRECT_HOSTS` | remote | Hosts OAuth may send you back to. Default: claude.ai, claude.com, chatgpt.com, chat.openai.com, platform.openai.com (localhost is always allowed). Comma-separated; `*` allows any https host |
| `TZ` | both | Time zone for "today", due dates and reminders |

On Windows, when the Vikunja variables aren't set, the connector uses the Docket settings file instead
(`%LOCALAPPDATA%\Docket\settings.json`, protected with Windows DPAPI).

To make a token without the Docket app: `docket-mcp setup` asks for your Vikunja username and password once, creates a
token called "Docket AI connector" with no delete rights, and saves it to the token file. Revoke it any time in Vikunja
under **Settings > API Tokens**.

## Tools

| Tool | What it does |
|---|---|
| `list_tasks` | Tasks by view: today, upcoming, overdue, open, done, my_day, important, planned, all, completed; optional list and search |
| `get_task` | One task in full: steps, notes, reminders, repeat, categories, files, board column, comments |
| `add_task` | Add a task; the title can carry quick-add words (`tomorrow`, `fri`, `in 3 days`, `20/11`, `!1`–`!5`) |
| `update_task` | Change title, due date, priority or list |
| `complete_task` | Complete or reopen tasks; repeating tasks move to their next date |
| `move_task` | Move to a Kanban column (Done completes it) |
| `add_comment` | Comment on a task |
| `add_label` / `set_categories` | Add one category, or replace them all |
| `list_projects` / `get_board` | Lists with counts and columns; one list's board |
| `create_project` / `create_list` | New list, optionally inside a group |
| `my_day` / `set_my_day` | My Day (with suggestions); add or remove tasks |
| `set_important` | Star or unstar |
| `steps` | Add, complete, uncomplete or remove checklist steps |
| `set_repeat` | daily, weekdays, weekly, monthly, yearly, "every N days/weeks/months/years", none |
| `set_reminder` | Replace reminders (`tomorrow 9:00`, `in 2 hours`, `fri 5pm`, ISO times) |
| `set_notes` | Replace the notes, keeping steps |
| `add_subtask` / `link_subtask` | Add a subtask, or put an existing task under another (several parents allowed) |
| `task_tree` | A list nested, with subtasks under their parents |
| `assign` / `people` | Assign a task to someone who can see its list; list the people you work with |
| `overview` | Dashboard numbers: open, due today, overdue, next 7 days, done this week, by priority and list |

Every result is readable text with task ids written as `#123`, which keeps small local models on track too. Every
argument of every tool is in the [tool reference](tools.md); the full schema is also in `/openapi.json` on a running
connector, or `tools/list` over MCP.

## Prompts that work well

- "What's on my plate today? Put the three most urgent in My Day."
- "Add 'Send the Q3 invoice' to Work, due Friday, high priority, remind me Thursday at 4pm."
- "Break #42 into steps and estimate each."
- "Everything overdue in Home: move it to next week."
- "Make 'Water the plants' repeat every 3 days."
