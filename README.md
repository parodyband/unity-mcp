<p align="center">
  <img src="Images/Logo.png" alt="Open Unity MCP" width="190" />
</p>

<p align="center">
  <strong>A local Model Context Protocol server for the Unity 6+ Editor.</strong>
</p>

<p align="center">
  <a href="https://unity.com/releases/editor/whats-new/6000.0.0"><img alt="Unity 6+" src="https://img.shields.io/badge/Unity-6%2B-111111?logo=unity&logoColor=white"></a>
  <img alt="MCP Streamable HTTP" src="https://img.shields.io/badge/MCP-Streamable%20HTTP-2563eb">
  <img alt="Local only" src="https://img.shields.io/badge/server-local--only-16a34a">
  <a href="LICENSE.md"><img alt="License MIT" src="https://img.shields.io/badge/license-MIT-f59e0b"></a>
</p>

<p align="center">
  <a href="#quick-start">Quick Start</a> |
  <a href="Packages/com.strangeape.open-unity-mcp/Documentation~/client-setup.md">Client Setup</a> |
  <a href="Packages/com.strangeape.open-unity-mcp/Documentation~/tools.md">Tools</a> |
  <a href="Packages/com.strangeape.open-unity-mcp/Documentation~/architecture.md">Architecture</a>
</p>

Open Unity MCP runs inside the Unity Editor and exposes a local Streamable HTTP MCP endpoint. Agents can inspect your project, create and modify scene objects, work with prefabs, capture Scene View images, and use editor automation tools without a separate Unity relay application.

## Highlights

- **Package-first install:** add it through Unity Package Manager with a Git URL.
- **Local by default:** the server binds to `127.0.0.1` and rejects non-local browser origins.
- **Agent-ready Unity context:** tools expose project state, object IDs, hierarchy data, transforms, prefabs, console logs, and build status.
- **Fast scene construction:** create batches of GameObjects, set transforms, frame objects, save prefabs, and capture Scene View PNGs.
- **Client setup built in:** configure Claude Code, Codex, and Claude Desktop from Unity Preferences.

## Quick Start

Install the package in a Unity 6+ project:

1. Open **Window > Package Manager**.
2. Click **+**.
3. Choose **Add package from git URL...**.
4. Paste:

```text
https://github.com/parodyband/unity-mcp.git?path=/Packages/com.strangeape.open-unity-mcp
```

5. Click **Add**.
6. Open **Preferences > Open Unity MCP**.
7. Click **Start**.

The MCP endpoint is:

```text
http://127.0.0.1:8080/mcp
```

The Scene View toolbar badge shows server status and includes quick start/stop controls.

## Install Through Manifest

You can also edit `Packages/manifest.json` directly:

```json
{
  "dependencies": {
    "com.strangeape.open-unity-mcp": "https://github.com/parodyband/unity-mcp.git?path=/Packages/com.strangeape.open-unity-mcp"
  }
}
```

## Client Setup

Open **Preferences > Open Unity MCP** and use the **Client Setup** buttons:

| Client | Config updated | Transport |
| --- | --- | --- |
| Claude Code | `.mcp.json` in the Unity project root | Direct Streamable HTTP |
| Codex | `~/.codex/config.toml` | Direct Streamable HTTP |
| Claude Desktop | `claude_desktop_config.json` | Local `mcp-remote` stdio bridge |

Manual Streamable HTTP config:

```toml
[mcp_servers.open-unity-mcp]
url = "http://127.0.0.1:8080/mcp"
```

Full guide: [Packages/com.strangeape.open-unity-mcp/Documentation~/client-setup.md](Packages/com.strangeape.open-unity-mcp/Documentation~/client-setup.md).

## What Agents Can Do

| Category | Examples |
| --- | --- |
| Project context | Project info, packages, build settings, compilation status |
| Assets | Find, import, refresh, read/write text, create folders, copy, move, delete |
| Scenes | List scenes, read hierarchy, open, save, save all, close |
| GameObjects | Create objects, create batches, select objects, set transforms |
| Components | List components, add components, inspect and edit serialized properties |
| Prefabs | Inspect prefab info, instantiate prefabs, save scene objects as prefab assets |
| Scene View | Set camera, frame objects, capture PNG screenshots as MCP image content |
| Editor automation | Read and clear console logs, execute menu items, enter play mode |
| Validation and builds | Validate project state, request script compilation, run restricted builds |

Full reference: [Packages/com.strangeape.open-unity-mcp/Documentation~/tools.md](Packages/com.strangeape.open-unity-mcp/Documentation~/tools.md).

## Repository Layout

```text
Packages/com.strangeape.open-unity-mcp/
  Editor/          Editor-only MCP server, tools, settings, and UI
  Tests/Editor/    Unity Test Framework EditMode tests
  Documentation~/  Client setup, tools, and architecture docs
Images/
  Logo.png         Repository and package README logo
```

## Safety Boundaries

Open Unity MCP is built for local editor automation, but many tools can mutate your project. Keep client-side approvals enabled for write-capable tools.

- Server binds to `127.0.0.1` only.
- Non-local browser origins are rejected.
- Asset writes are scoped to `Assets` and `Packages`.
- Protected roots such as `Assets` and `Packages` cannot be deleted through lifecycle tools.
- Scene lifecycle tools protect dirty scenes unless the caller explicitly saves or discards changes.
- Player builds are restricted to the project `Builds/` folder.

## Documentation

- [Package README](Packages/com.strangeape.open-unity-mcp/README.md)
- [Client setup](Packages/com.strangeape.open-unity-mcp/Documentation~/client-setup.md)
- [Tool reference](Packages/com.strangeape.open-unity-mcp/Documentation~/tools.md)
- [Architecture notes](Packages/com.strangeape.open-unity-mcp/Documentation~/architecture.md)
- [Changelog](Packages/com.strangeape.open-unity-mcp/CHANGELOG.md)
- [Security policy](SECURITY.md)
- [License](LICENSE.md)
