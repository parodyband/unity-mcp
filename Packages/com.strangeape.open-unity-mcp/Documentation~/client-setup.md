# Client Setup

Open the MCP Preferences section in Unity and click **Start**:

```text
Preferences > Open Unity MCP
```

Default endpoint:

```text
http://127.0.0.1:8080/mcp
```

The Scene View toolbar badge also shows server status and provides quick start/stop access.

## Auto Setup

The same Preferences section has **Client Setup** buttons that update common local client config files. You can also use the matching setup commands under `Tools > Open Unity MCP > Setup`.

- **Setup Claude Code**
- **Setup Codex**
- **Setup Claude Desktop Bridge**

These actions merge an `open-unity-mcp` server entry into the target config and keep unrelated servers intact. Restart the client after setup.

| Client | Config Updated | Transport |
| --- | --- | --- |
| Claude Code | `.mcp.json` in the Unity project root | stdio sidecar (`node Server~/open-unity-mcp-sidecar.js`) + named HTTP fallback |
| Codex | `~/.codex/config.toml` | stdio sidecar |
| Claude Desktop | `claude_desktop_config.json` | stdio sidecar |

All three launch the bundled **sidecar** over stdio. The sidecar forwards JSON-RPC to the in-editor HTTP server and **rides out Unity domain reloads** so the client's connection survives recompiles, instead of dropping when Unity reloads the domain. It requires **Node.js 18+** on `PATH`. See `Server~/README.md` for the sidecar's arguments and reload behavior.

Clients that speak Streamable HTTP directly can still point at the endpoint below, but they will drop on every recompile (Claude Code auto-reconnects for only ~31s, then marks the server failed). The Claude Code setup keeps a named `open-unity-mcp-http` entry for that case.

## Generic Streamable HTTP Client

Configure the client with:

```text
http://127.0.0.1:8080/mcp
```

The server accepts JSON-RPC over HTTP POST and returns JSON responses. GET returns `405 Method Not Allowed` because the package does not implement server-sent event streaming yet.

## Claude Code

Use the Unity auto setup action (recommended — it fills in the absolute sidecar path for you), or add this `.mcp.json` to the Unity project root. Replace `<abs>` with the absolute path to the package's `Server~/open-unity-mcp-sidecar.js` and `<project>` with the Unity project root:

```json
{
  "mcpServers": {
    "open-unity-mcp": {
      "command": "node",
      "args": ["<abs>/Server~/open-unity-mcp-sidecar.js", "--port", "8080", "--project", "<project>"]
    }
  }
}
```

The auto setup also writes a named `open-unity-mcp-http` fallback entry (`type: http`, direct to the endpoint) for anyone who wants to bypass Node. Direct HTTP drops on every recompile; the sidecar does not.

Run `/mcp` in Claude Code to confirm the connection.

Claude Code may ask for permission to read `.claude/settings.local.json` when it loads project MCP settings. That prompt is for Claude Code's local project settings, not Claude Desktop.

## Codex

Use the Unity auto setup action, or add this to `~/.codex/config.toml` (replace `<abs>` and `<project>` as above):

```toml
[mcp_servers.open-unity-mcp]
command = "node"
args = ["<abs>/Server~/open-unity-mcp-sidecar.js", "--port", "8080", "--project", "<project>"]
```

Run `codex mcp list` to confirm the connection.

## Claude Desktop

Use the Unity auto setup action, or add this to `claude_desktop_config.json` (replace `<abs>` and `<project>` as above):

```json
{
  "mcpServers": {
    "open-unity-mcp": {
      "command": "node",
      "args": ["<abs>/Server~/open-unity-mcp-sidecar.js", "--port", "8080", "--project", "<project>"]
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

Restart Claude Desktop after editing config. Keep Unity open with the MCP server running, or enable **Auto Start** in `Preferences > Open Unity MCP`.

## Security

Only connect local clients you trust. The server can read and write project files under `Assets` and `Packages`, mutate scenes, open/save/close scene assets, execute editor menu items, request script compilation, and build players when a client calls those tools. Scene lifecycle tools protect dirty scenes by default, and player build output is restricted to `Builds/`.

## Companion workflow skill

The Unity setup buttons now install the connection and a project-local workflow skill from the package's `Skills~/open-unity-mcp` folder:

- **Codex:** `.agents/skills/open-unity-mcp/`, following [Codex's repository skill discovery](https://developers.openai.com/codex/skills/).
- **Claude Code:** `.claude/skills/open-unity-mcp/`, following [Claude Code's project skill discovery](https://code.claude.com/docs/en/skills).
- **Claude Desktop/custom clients:** essential SDK guidance is supplied through MCP initialization and tool descriptions. The installer does not assume these clients load local filesystem skills.

Rerun the setup button after updating the package to refresh the managed skill. Customized files are preserved: setup reports the conflict and leaves all skill files unchanged rather than partially updating them. Existing MCP config setup behavior is retained. The config may be installed successfully even if a customized skill cannot be updated; the result dialog reports both outcomes. Restart/reconnect the client to load the updated tools and skill discovery paths.

The same skill and reference files are bundled for both supported coding agents. They are installed in the current Unity project, not globally, so unrelated projects do not inherit Unity-specific guidance. For custom clients, the skill can be copied manually if that client supports Agent Skills.

The stdio sidecar now offers persistent JavaScript sessions. Direct HTTP remains supported but does not offer session execution. Add `--no-code` to the sidecar's argument list to omit the session tools. Session execution must be authorized as trusted local code, and wrapper approval rules must cover nested operations. Server-side disabled-tool checks remain enforced by SDK calls.
