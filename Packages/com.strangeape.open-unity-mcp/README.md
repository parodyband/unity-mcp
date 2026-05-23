# Open Unity MCP

Run a local Model Context Protocol server inside the Unity 6+ Editor.

Open Unity MCP lets MCP clients inspect and automate your Unity project through a local Streamable HTTP endpoint. It is packaged as a normal Unity Package Manager dependency, so there is no separate Unity relay app to install.

## What You Get

- Local-only MCP endpoint at `http://127.0.0.1:8080/mcp`
- Unity Preferences page for server status, port, auto-start, and client setup
- Scene and hierarchy tools for reading, creating, transforming, and selecting GameObjects
- Batch object creation for fast scene construction
- Scene View camera framing and PNG capture returned as MCP image content
- Component, serialized property, prefab, asset, console, build, and validation tools
- MCP resources and prompts for project context and Unity-focused agent guidance
- Built-in setup helpers for Claude Code, Codex, and Claude Desktop

## Requirements

- Unity 6 or newer
- An MCP client that supports Streamable HTTP, or Claude Desktop with the included `mcp-remote` bridge setup
- Node.js/npm only if you use the Claude Desktop bridge

## Install

Install from Unity Package Manager:

1. Open **Window > Package Manager**.
2. Click the **+** button.
3. Choose **Add package from git URL...**.
4. Paste:

```text
https://github.com/parodyband/unity-mcp.git?path=/Packages/com.strangeape.open-unity-mcp
```

5. Click **Add**.

You can also add it directly to `Packages/manifest.json`:

```json
{
  "dependencies": {
    "com.strangeape.open-unity-mcp": "https://github.com/parodyband/unity-mcp.git?path=/Packages/com.strangeape.open-unity-mcp"
  }
}
```

## Quick Start

1. Open **Preferences > Open Unity MCP**.
2. Click **Start**.
3. Keep Unity open while your MCP client connects to:

```text
http://127.0.0.1:8080/mcp
```

The Scene View toolbar badge shows whether the server is running and includes quick start/stop controls. You can also use **Tools > Open Unity MCP > Preferences** to jump back to the Preferences page.

## Client Setup

Open **Preferences > Open Unity MCP** and use the **Client Setup** buttons:

| Client | What gets configured | Transport |
| --- | --- | --- |
| Claude Code | `.mcp.json` in the Unity project root | Direct HTTP |
| Codex | `~/.codex/config.toml` | Direct HTTP |
| Claude Desktop | `claude_desktop_config.json` | Local `mcp-remote` stdio bridge |

For clients that accept a Streamable HTTP endpoint manually, use:

```toml
[mcp_servers.open-unity-mcp]
url = "http://127.0.0.1:8080/mcp"
```

Detailed client setup: [Documentation~/client-setup.md](Documentation~/client-setup.md).

## Tool Coverage

Open Unity MCP exposes tools for:

- Project and editor state
- Package listing
- Asset search, metadata, import, refresh, read/write, create, copy, move, and delete
- Component listing, component add, serialized property read, and serialized property write
- Console read and clear
- Scene listing, hierarchy reads, open, save, save all, and close
- GameObject creation, batch creation, selection, and transform correction
- Scene View camera positioning, object framing, and screenshot capture
- Prefab inspection, prefab instantiation, and prefab asset saving
- Menu execution, play-mode control, script compilation, validation, build settings, and restricted player builds

Full tool reference: [Documentation~/tools.md](Documentation~/tools.md).

## Security Model

The server is designed for local editor automation:

- It binds to `127.0.0.1` only.
- It rejects non-local browser `Origin` headers.
- Asset file writes are restricted to `Assets` and `Packages`.
- Player build output is restricted to the project `Builds/` folder.
- Scene open and close tools protect dirty scenes unless the caller explicitly saves or discards changes.

Only connect local MCP clients you trust. Some tools can mutate project files, scenes, generated artifacts, and editor state.

## Documentation

- [Client setup](Documentation~/client-setup.md)
- [Tool reference](Documentation~/tools.md)
- [Architecture notes](Documentation~/architecture.md)
- [Changelog](CHANGELOG.md)

## License

MIT
