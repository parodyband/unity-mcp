# Client Setup

Start the server in Unity:

```text
Tools > Open Unity MCP > Start Server
```

Default endpoint:

```text
http://127.0.0.1:8080/mcp
```

## Auto Setup

Open Unity MCP can update common local client config files from the Unity editor:

```text
Tools > Open Unity MCP > Setup > Claude Code Project
Tools > Open Unity MCP > Setup > Codex User Config
Tools > Open Unity MCP > Setup > Claude Desktop Bridge
```

The same actions are available in `Tools > Open Unity MCP > Status`.

These actions merge a `unity` MCP server entry into the target config and keep unrelated servers intact. Restart the client after setup.

| Client | Config Updated | Transport |
| --- | --- | --- |
| Claude Code | `.mcp.json` in the Unity project root | Direct Streamable HTTP |
| Codex | `~/.codex/config.toml` | Direct Streamable HTTP |
| Claude Desktop | `claude_desktop_config.json` | Local stdio bridge with `npx -y mcp-remote@latest` |

Claude Desktop requires Node.js/npm for the bridge. The bridge is needed because Claude Desktop's local MCP config starts local processes, while Open Unity MCP exposes a local HTTP endpoint.

## Generic Streamable HTTP Client

Configure the client with:

```text
http://127.0.0.1:8080/mcp
```

The server accepts JSON-RPC over HTTP POST and returns JSON responses. GET returns `405 Method Not Allowed` because the package does not implement server-sent event streaming yet.

## Claude Code

Use the Unity auto setup action, or add this `.mcp.json` file to the Unity project root:

```json
{
  "mcpServers": {
    "unity": {
      "type": "http",
      "url": "http://127.0.0.1:8080/mcp"
    }
  }
}
```

You can also add it with the Claude Code CLI:

```powershell
claude mcp add --transport http unity http://127.0.0.1:8080/mcp
```

Run `/mcp` in Claude Code to confirm the connection.

## Codex

Use the Unity auto setup action, or add this to `~/.codex/config.toml`:

```toml
[mcp_servers.unity]
url = "http://127.0.0.1:8080/mcp"
```

You can also add it with the Codex CLI:

```powershell
codex mcp add unity --url http://127.0.0.1:8080/mcp
```

Run `codex mcp list` to confirm the connection.

## Claude Desktop

Use the Unity auto setup action, or add this to `claude_desktop_config.json`:

```json
{
  "mcpServers": {
    "unity": {
      "command": "npx",
      "args": [
        "-y",
        "mcp-remote@latest",
        "--http",
        "http://127.0.0.1:8080/mcp",
        "--allow-http"
      ]
    }
  }
}
```

Common config locations:

- Windows: `%APPDATA%\Claude\claude_desktop_config.json`
- macOS: `~/Library/Application Support/Claude/claude_desktop_config.json`
- Linux: `~/.config/Claude/claude_desktop_config.json`

Restart Claude Desktop after editing config. Keep Unity open with the MCP server running, or enable `Tools > Open Unity MCP > Auto Start`.

## Security

Only connect local clients you trust. The server can read and write project files under `Assets` and `Packages`, mutate scenes, open/save/close scene assets, execute editor menu items, request script compilation, and build players when a client calls those tools. Scene lifecycle tools protect dirty scenes by default, and player build output is restricted to `Builds/`.
