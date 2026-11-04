# Docket with local AI (LM Studio, Ollama, Jan, Open WebUI)

You can run your task assistant entirely on your own computer: a local model, a local MCP client, the local Docket
connector and your own Vikunja. Nothing leaves your machine or network.

**What you need**
1. Docket set up (the Windows installer, or a [Docket server](../self-hosting.md) on your network).
2. A model that's good at tool calling. Recent instruction-tuned models with tool support work best, for example
   Qwen 3 (8B or larger), Llama 3.1/3.3 (8B or larger), Mistral Small or gpt-oss. Very small models (under ~4B)
   often call tools with wrong arguments.
3. An app that connects models to MCP servers. Ollama itself doesn't load MCP servers; pair it with one of the
   clients below.

## LM Studio

LM Studio is both the model runner and an MCP client.

1. Load a model with tool use (the model list shows a hammer icon for tool-capable models).
2. In the right sidebar open **Program > Install > Edit mcp.json** and add Docket:

```json
{
  "mcpServers": {
    "docket": {
      "command": "C:\\Users\\you\\AppData\\Local\\Programs\\Docket\\mcp\\docket-mcp.exe"
    }
  }
}
```

   On macOS/Linux add `"env": { "DOCKET_VIKUNJA_URL": "http://localhost:3456", "DOCKET_VIKUNJA_TOKEN_FILE": "/home/you/.config/docket/vikunja-token" }`.
   For a Docket server instead: `"docket": { "url": "https://tasks.example.com/mcp", "headers": { "Authorization": "Bearer YOUR_CONNECTOR_KEY" } }`.
3. Save. In a chat, LM Studio asks before each tool call; approve it, or allow Docket's read-only tools permanently.

## Ollama

Use Ollama for the model and one of these as the MCP client:

- **Open WebUI** (browser UI for Ollama): in **Admin Settings > External Tools**, add a server of type **MCP
  (Streamable HTTP)** with URL `http://your-docket-server:8080/mcp` (or `https://tasks.example.com/mcp`) and a
  Bearer key. Then turn the tool on in a chat. Open WebUI calls HTTP servers, so run the connector with `--http`
  ([below](#run-the-connector-over-http-on-your-network)).
- **Jan, Msty, Cherry Studio, 5ire** and other desktop chat apps with MCP support: add a local (stdio) server with
  the `docket-mcp` command, like the LM Studio example, and pick an Ollama model.
- **Continue (VS Code / JetBrains)**: add Docket under `mcpServers` in Continue's config and choose an Ollama model.
- **Terminal**: [ollmcp](https://github.com/jonigl/mcp-client-for-ollama) connects Ollama models to MCP servers;
  give it the `docket-mcp` command as a server (see its README for the current flags).

## Run the connector over HTTP on your network

Some local clients only speak HTTP. Run the connector on the machine with Docket:

```powershell
# Windows, with your Docket settings
$env:DOCKET_MCP_KEY = "a-long-random-key-of-24-or-more-characters"
$env:DOCKET_MCP_URLS = "http://127.0.0.1:8080"     # use 0.0.0.0:8080 to allow other devices on your network
& "$env:LOCALAPPDATA\Programs\Docket\mcp\docket-mcp.exe" --http
```

Then use `http://127.0.0.1:8080/mcp` with `Authorization: Bearer <that key>`. Plain HTTP is fine on one computer;
across a network, put it behind HTTPS (Tailscale, Caddy) or use the [Docker server](../self-hosting.md).

## Tips for small models

- Name the tool in your request: "Use list_tasks with view today." Small models follow explicit tool names well.
- Keep the conversation short; start a new chat for a new job so the tool list and results fit the context window.
- Give the model a system prompt such as:
  > You manage the user's to-do list with the Docket tools. Task ids look like #123. Look tasks up before changing
  > them, never guess ids, and confirm what you changed in one short sentence.
- Raise the context length to at least 8k tokens; Docket's 20 tool descriptions take about 4k.
