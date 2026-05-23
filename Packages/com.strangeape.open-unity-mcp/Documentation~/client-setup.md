# Client Setup

Open the MCP window in Unity and click **Start**:

```text
Tools > Open Unity MCP > Status
```

Default endpoint:

```text
http://127.0.0.1:8080/mcp
```

The Scene View toolbar badge also shows server status and provides quick start/stop access.

## Auto Setup

The same window has **Client Setup** buttons that update common local client config files. You can also use the matching setup commands under `Tools > Open Unity MCP > Setup`.

- **Setup Claude Code**
- **Setup Codex**
- **Setup Claude Desktop Bridge**

These actions merge an `open-unity-mcp` server entry into the target config and keep unrelated servers intact. Restart the client after setup.

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
    "open-unity-mcp": {
      "type": "http",
      "url": "http://127.0.0.1:8080/mcp"
    }
  }
}
```

You can also add it with the Claude Code CLI:

```powershell
claude mcp add --transport http open-unity-mcp http://127.0.0.1:8080/mcp
```

Run `/mcp` in Claude Code to confirm the connection.

Claude Code may ask for permission to read `.claude/settings.local.json` when it loads project MCP settings. That prompt is for Claude Code's local project settings, not Claude Desktop.

## Codex

Use the Unity auto setup action, or add this to `~/.codex/config.toml`:

```toml
[mcp_servers.open-unity-mcp]
url = "http://127.0.0.1:8080/mcp"
```

You can also add it with the Codex CLI:

```powershell
codex mcp add open-unity-mcp --url http://127.0.0.1:8080/mcp
```

Run `codex mcp list` to confirm the connection.

## Claude Desktop

Use the Unity auto setup action, or add this to `claude_desktop_config.json`:

```json
{
  "mcpServers": {
    "open-unity-mcp": {
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

In Claude Desktop, `Settings > Developer > Edit Config` should open `claude_desktop_config.json`. A prompt for `.claude/settings.local.json` is from Claude Code.

On Windows, auto setup also updates a detected MSIX package config path if Claude Desktop is using one.

Restart Claude Desktop after editing config. Keep Unity open with the MCP server running, or enable `Tools > Open Unity MCP > Auto Start`.

## Security

Only connect local clients you trust. The server can read and write project files under `Assets` and `Packages`, mutate scenes, open/save/close scene assets, execute editor menu items, request script compilation, and build players when a client calls those tools. Scene lifecycle tools protect dirty scenes by default, and player build output is restricted to `Builds/`.
