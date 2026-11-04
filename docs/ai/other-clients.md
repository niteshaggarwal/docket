# Docket with other MCP clients

Any app that supports MCP can use Docket. Use the **local** form when the app runs on the computer where Docket is
set up, and the **remote** form for a [Docket server](../self-hosting.md). Replace the paths, address and key with
yours. Config formats change between versions; if one below is rejected, check the app's own MCP docs.

Local command, by platform:
- Windows (installer): `C:\Users\you\AppData\Local\Programs\Docket\mcp\docket-mcp.exe` (reads your Docket settings)
- macOS / Linux: the `docket-mcp` you downloaded, with `DOCKET_VIKUNJA_URL` and `DOCKET_VIKUNJA_TOKEN_FILE` set
  ([how](README.md#settings-environment-variables))

## Gemini CLI

`~/.gemini/settings.json`:

```json
{
  "mcpServers": {
    "docket": { "command": "C:\\Users\\you\\AppData\\Local\\Programs\\Docket\\mcp\\docket-mcp.exe" }
  }
}
```

Remote:

```json
{
  "mcpServers": {
    "docket": {
      "httpUrl": "https://tasks.example.com/mcp",
      "headers": { "Authorization": "Bearer YOUR_CONNECTOR_KEY" }
    }
  }
}
```

Run `/mcp` in Gemini CLI to see Docket's tools.

## VS Code (GitHub Copilot agent mode)

`.vscode/mcp.json` in a workspace, or **MCP: Open User Configuration** for all workspaces:

```json
{
  "servers": {
    "docket": {
      "type": "http",
      "url": "https://tasks.example.com/mcp",
      "headers": { "Authorization": "Bearer ${input:docketKey}" }
    }
  },
  "inputs": [
    { "id": "docketKey", "type": "promptString", "description": "Docket connector key", "password": true }
  ]
}
```

Local: `"docket": { "type": "stdio", "command": "C:\\...\\docket-mcp.exe" }`.

## Cursor, Windsurf and other `mcp.json` clients

`~/.cursor/mcp.json` (Cursor), and the same shape in most clients that copied it:

```json
{
  "mcpServers": {
    "docket": { "url": "https://tasks.example.com/mcp", "headers": { "Authorization": "Bearer YOUR_CONNECTOR_KEY" } }
  }
}
```

Local: `"docket": { "command": "C:\\...\\docket-mcp.exe", "env": {} }`.

## Clients that only do OAuth

Point them at `https://tasks.example.com/mcp`. Docket advertises its OAuth server through the standard MCP discovery
(`/.well-known/oauth-protected-resource`), supports dynamic client registration and PKCE, and its sign-in page asks
for your connector key once. Return addresses must be `http(s)://localhost` (desktop apps) or https on a host in
`DOCKET_OAUTH_REDIRECT_HOSTS` (by default the Claude and ChatGPT sites); add your client's host there if it's refused.

## Checking a connection

```sh
# Is the connector up, and can it reach Vikunja?
curl https://tasks.example.com/connector/health
# Does the key work? (should list 20 tools)
curl -s https://tasks.example.com/mcp -H "Authorization: Bearer YOUR_CONNECTOR_KEY" \
  -H "Content-Type: application/json" -H "Accept: application/json, text/event-stream" \
  -d '{"jsonrpc":"2.0","id":1,"method":"tools/list"}'
```

The [MCP Inspector](https://github.com/modelcontextprotocol/inspector) (`npx @modelcontextprotocol/inspector`) is handy
for trying tools by hand, locally or remotely.
