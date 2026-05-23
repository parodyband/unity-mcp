# Open Unity MCP

Open Unity MCP is a small Unity 6+ Editor package that runs a Model Context Protocol server inside the Unity editor. It is designed to be installed as a Unity Package Manager Git dependency and does not require a separate relay executable.

## Install With Package Manager

Open your Unity 6+ project, then install the package through Unity Package Manager:

1. Open `Window > Package Manager`.
2. Click the `+` button in the top-left corner.
3. Choose `Add package from git URL...`.
4. Paste this URL:

```text
https://github.com/parodyband/unity-mcp.git?path=/Packages/com.strangeape.open-unity-mcp
```

5. Click `Add`.
6. After Unity imports the package, start it from `Tools > Open Unity MCP > Start Server`.

Requires Unity 6 or newer.

If you prefer editing `Packages/manifest.json` directly, add this dependency:

```json
{
  "dependencies": {
    "com.strangeape.open-unity-mcp": "https://github.com/parodyband/unity-mcp.git?path=/Packages/com.strangeape.open-unity-mcp"
  }
}
```

## MCP Endpoint

Default endpoint:

```text
http://127.0.0.1:8080/mcp
```

The server implements the Streamable HTTP subset needed by MCP clients: one local endpoint, JSON-RPC requests over HTTP POST, JSON responses, and no external process. It binds to `127.0.0.1` and rejects non-local browser origins.

## Tool Coverage

The package currently includes tools for:

- Project/editor state
- Package listing
- Asset search, metadata, import, refresh, text read/write, folder creation, copy, move, and delete
- Component listing, adding, serialized property reads, and basic serialized property writes
- Console read and clear
- Scene listing, bounded hierarchy reads, open, save, and close
- Prefab inspection, instantiation, and prefab asset saving
- Selection and simple GameObject creation
- Menu execution and play-mode control
- Compilation status, project validation, build settings, and restricted player builds

See `Packages/com.strangeape.open-unity-mcp/Documentation~/tools.md`.

It also implements MCP resources and prompts so clients can discover read-only project context and reusable Unity guidance without invoking write-capable tools.

## Research Notes

The implementation follows MCP Streamable HTTP guidance: POST JSON-RPC to one endpoint, return JSON for request responses, return 202 for notifications, allow 405 for unsupported GET streams, bind local, and validate `Origin`. It also advertises MCP `tools`, `resources`, and `prompts` capabilities during initialization.

Unity install shape follows UPM Git dependency and package layout guidance: the package has its own `package.json` under `Packages/com.strangeape.open-unity-mcp`, so consumers install it with `?path=/Packages/com.strangeape.open-unity-mcp`.

## Package

The Unity package lives in:

```text
Packages/com.strangeape.open-unity-mcp
```
