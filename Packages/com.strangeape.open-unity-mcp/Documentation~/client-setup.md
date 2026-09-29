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
| Claude Code | `.mcp.json` in the Unity project root | stdio sidecar + named HTTP fallback |
| Codex | `~/.codex/config.toml` | stdio sidecar |
| Claude Desktop | `claude_desktop_config.json` | stdio sidecar |

All three launch the **sidecar** over stdio. The sidecar forwards JSON-RPC to the in-editor HTTP server and **rides out Unity domain reloads** so the client's connection survives recompiles, instead of dropping when Unity reloads the domain. It requires **Node.js 18+** on `PATH`. See `Server~/README.md` for the sidecar's arguments and reload behavior.

Clients that speak Streamable HTTP directly can still point at the endpoint below, but they will drop on every recompile (Claude Code auto-reconnects for only ~31s, then marks the server failed). The Claude Code setup keeps a named `open-unity-mcp-http` entry for that case.

### Stable sidecar path

Configs launch the sidecar from a fixed per-user folder, never from the package itself:

- Windows: `%USERPROFILE%\.open-unity-mcp\sidecar\open-unity-mcp-sidecar.js`
- macOS/Linux: `~/.open-unity-mcp/sidecar/open-unity-mcp-sidecar.js`

Git and registry packages resolve to `Library/PackageCache/com.strangeape.open-unity-mcp@<hash>`, and that hash changes on every package update. Configs written by 0.16.x and earlier pointed there, so each update broke them with `Cannot find module ...open-unity-mcp-sidecar.js`, which clients report as the server disconnecting.

Every time the editor loads, the package refreshes the stable copy. A newer copy installed by another project is kept rather than downgraded. It then repoints any existing `open-unity-mcp` sidecar entry that still references `Library/PackageCache` or a missing script. That covers Claude Desktop, this project's `.mcp.json`, and Codex. Each repair is logged in the Console. Restart the client afterward. Entries using the HTTP URL or a custom script path that still exists are left alone.

The sidecar also:

- **Answers the handshake while Unity is closed.** It serves the last tool catalog it saw, then notifies the client with `list_changed` once Unity is reachable. A client started before Unity therefore stays connected instead of failing its 30-second handshake timeout.
- **Follows the project that owns the port.** Claude Desktop has one global config, so `--project` may name a different project than the one open in Unity. `/health` reports the live project, and the sidecar uses that project's status file and access token.

## Generic Streamable HTTP Client

Configure the client with:

```text
http://127.0.0.1:8080/mcp
```

The server accepts JSON-RPC over HTTP POST and returns JSON responses. GET returns `405 Method Not Allowed` because the package does not implement server-sent event streaming yet.

## Claude Code

Use the Unity auto setup action (recommended: it fills in the absolute sidecar path for you), or add this `.mcp.json` to the Unity project root. Replace `<home>` with your home directory (the stable path above) and `<project>` with the Unity project root:

```json
{
  "mcpServers": {
    "open-unity-mcp": {
      "command": "node",
      "args": ["<home>/.open-unity-mcp/sidecar/open-unity-mcp-sidecar.js", "--port", "8080", "--project", "<project>"]
    }
  }
}
```

The auto setup also writes a named `open-unity-mcp-http` fallback entry (`type: http`, direct to the endpoint) for anyone who wants to bypass Node. Direct HTTP drops on every recompile; the sidecar does not.

Run `/mcp` in Claude Code to confirm the connection.

Claude Code may ask for permission to read `.claude/settings.local.json` when it loads project MCP settings. That prompt is for Claude Code's local project settings, not Claude Desktop.

## Codex

Use the Unity auto setup action, or add this to `~/.codex/config.toml` (replace `<home>` and `<project>` as above):

```toml
[mcp_servers.open-unity-mcp]
command = "node"
args = ["<home>/.open-unity-mcp/sidecar/open-unity-mcp-sidecar.js", "--port", "8080", "--project", "<project>"]
```

A direct `url = "http://127.0.0.1:8080/mcp"` entry also works with Codex. Codex treats a failed request as a tool error rather than dropping the server.

Run `codex mcp list` to confirm the connection.

## Claude Desktop

Use the Unity auto setup action, or add this to `claude_desktop_config.json` (replace `<home>` and `<project>` as above):

```json
{
  "mcpServers": {
    "open-unity-mcp": {
      "command": "node",
      "args": ["<home>/.open-unity-mcp/sidecar/open-unity-mcp-sidecar.js", "--port", "8080", "--project", "<project>"]
    }
  }
}
```

Common config locations:

- Windows: `%APPDATA%\Claude\claude_desktop_config.json`
- Windows (Microsoft Store install): `%LOCALAPPDATA%\Packages\Claude_<id>\LocalCache\Roaming\Claude\claude_desktop_config.json`
- macOS: `~/Library/Application Support/Claude/claude_desktop_config.json`
- Linux: `~/.config/Claude/claude_desktop_config.json`

In Claude Desktop, `Settings > Developer > Edit Config` should open `claude_desktop_config.json`. A prompt for `.claude/settings.local.json` is from Claude Code.

On Windows, auto setup also updates a detected MSIX package config path if Claude Desktop is using one.

Fully quit Claude Desktop from the system tray and reopen it after editing config; closing the window leaves it running. Unity does not have to be open first: the sidecar connects immediately and picks up the tools once the editor starts. Enable **Auto Start** in `Preferences > Open Unity MCP` so the server comes up with the editor.

## Troubleshooting disconnects

The client's MCP log shows why a server dropped:

- Claude Desktop: `mcp-server-open-unity-mcp.log` in `%LOCALAPPDATA%\Claude\Logs` or `%APPDATA%\Claude\logs` on Windows, and `~/Library/Logs/Claude` on macOS.
- Claude Code: run `/mcp`, or run `claude --debug`.

| Log message | Cause | Fix |
| --- | --- | --- |
| `Cannot find module '...PackageCache...open-unity-mcp-sidecar.js'` | Config written by 0.16.x or earlier; the package has since updated | Open the project in Unity once (configs are repaired on load), or rerun the setup button, then restart the client |
| `Cannot find module` with another path | The project moved, or the stable copy was deleted | Open the project in Unity once, or rerun setup |
| `The Unity editor appears to be closed` | No server on the port within `--timeout` | Start the server in Unity, or enable Auto Start |
| `Missing or invalid access token` | Token enforcement is on and the sidecar cannot read the project's status file | Check that `--project` points at the project, or rerun setup |

The sidecar logs every recovery to stderr with timestamps, and clients copy stderr into the same log.

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
