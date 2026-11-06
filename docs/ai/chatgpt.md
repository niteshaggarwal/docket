# Docket with ChatGPT

ChatGPT connects from OpenAI's cloud, so you need a [Docket server](../self-hosting.md) with a public HTTPS address.
There are two ways to hook it up; pick by what your plan offers.

| | MCP connector (developer mode) | Custom GPT with actions |
|---|---|---|
| Uses | `https://your-domain/mcp` | `https://your-domain/openapi.json` |
| Sign-in | OAuth (you type the connector key once) | Bearer key stored in the GPT |
| Works in | Any chat where you turn the connector on | Only inside that GPT |
| Availability | Depends on your plan; OpenAI has offered full (write) MCP access in developer mode on Business, Enterprise and Edu, and more limited access on other plans. Check OpenAI's help for your plan. | Plans that can create GPTs |

OpenAI changes these menus often; if a name below doesn't match, search OpenAI's help for "developer mode" or "GPT actions".

## Option 1: MCP connector (developer mode)

1. In ChatGPT, open **Settings > Apps & Connectors > Advanced settings** and turn on **Developer mode**.
   (On workspace plans an admin may need to allow it.)
2. Back in **Apps & Connectors**, choose **Create** (or **Add custom connector**).
3. Name: `Docket`. MCP server URL: `https://tasks.example.com/mcp`. Authentication: **OAuth**.
   ChatGPT registers itself with Docket automatically (dynamic client registration); leave client ID/secret empty.
4. Docket's sign-in page opens. Type your connector key (`DOCKET_MCP_KEY`) and choose **Allow access**.
5. In a chat, open the **+** menu, pick **Developer mode** (or **More > Docket**) and ask about your tasks.

ChatGPT asks you to confirm before it uses a tool that changes something. Docket marks its read tools as read-only,
so listing and reading need no confirmation.

ChatGPT can't send custom headers to MCP servers, so the bearer-key method doesn't apply here; OAuth is required.

## Option 2: Custom GPT with actions

1. Go to **Explore GPTs > Create**, then the **Configure** tab.
2. Name it (e.g. "My tasks"). Instructions, for example:
   > You manage my to-do list through the Docket actions. Task ids appear as #123; use them in follow-up calls.
   > Use list_tasks with view=today to see what's due, my_day for My Day. Never invent ids; look tasks up first.
3. Under **Actions**, choose **Create new action**, then **Import from URL** and enter
   `https://tasks.example.com/openapi.json`. ChatGPT reads all 26 operations ([the tools](tools.md)).
4. **Authentication**: **API Key**, Auth Type **Bearer**, and paste your connector key.
5. **Privacy policy**: required only if you publish the GPT. Keep it **Only me** so nobody else can use your key.
6. Save, then test in the preview pane: "What's due today?"

Operations that change data are marked as consequential, so ChatGPT asks before running them.

## Other online assistants

The OpenAPI description at `/openapi.json` works with any assistant or automation tool that imports OpenAPI 3.1
(for example Gemini Gems and Microsoft Copilot Studio, Zapier, n8n or Make), with Bearer authentication. For
assistants that speak MCP, see [other-clients.md](other-clients.md).

## Troubleshooting

| Symptom | Fix |
|---|---|
| "Error fetching OAuth configuration" | `https://your-domain/.well-known/oauth-authorization-server` must load from the internet. If you changed the domain, set `DOCKET_PUBLIC_URL` to the new address. |
| Action import fails | Open `https://your-domain/openapi.json` in a browser; check the `servers` URL is your public HTTPS address (set `DOCKET_PUBLIC_URL`). |
| 401 from an action | The Auth Type must be **Bearer** and the key must match `DOCKET_MCP_KEY` exactly. |
| New tools don't show up | ChatGPT keeps a connector's tool list from when it was created; delete and re-add the connector after upgrading Docket. |
