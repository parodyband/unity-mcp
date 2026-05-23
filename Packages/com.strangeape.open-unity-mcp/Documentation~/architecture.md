# Architecture

Open Unity MCP is intentionally small:

- A `TcpListener` binds to `127.0.0.1` and parses simple HTTP requests.
- `/mcp` accepts JSON-RPC 2.0 POST bodies and returns `application/json`.
- GET on `/mcp` returns 405 because this package does not stream server-sent events yet.
- Unity API work is marshalled to the editor main thread.
- Tools are registered in one deterministic registry.
- The package has no runtime assembly and no external process.
- Result sizes are bounded for hierarchy, logs, search, HTTP body, and text reads.
- File tools only operate on paths that resolve under `Assets` or `Packages`.
- Asset lifecycle tools refuse to modify the protected `Assets` and `Packages` root folders.
- Player build output is restricted to the ignored project `Builds/` folder.
- Scene replacement and close tools refuse to discard dirty scene changes unless a caller explicitly saves or discards them.
- Unity object identity uses Unity 6 `EntityId` values exposed as JSON-safe `objectId` strings.

## Supported MCP Methods

- `initialize`
- `notifications/initialized`
- `ping`
- `resources/list`
- `resources/read`
- `prompts/list`
- `prompts/get`
- `tools/list`
- `tools/call`

The server does not implement resource subscriptions, list-changed notifications, or prompt/resource pagination yet. Lists are intentionally small and deterministic.

## Security Posture

The server binds only to loopback and rejects non-local browser `Origin` headers. Tools still operate inside the Unity project and should be treated as local automation capabilities. MCP clients should ask for user approval before calling tools that write files, mutate scenes, manage scene lifecycle, execute menu items, request script compilation, or build players.

## References

- MCP Streamable HTTP transport: https://modelcontextprotocol.io/specification/2025-06-18/basic/transports
- MCP resources: https://modelcontextprotocol.io/specification/2025-06-18/server/resources
- MCP prompts: https://modelcontextprotocol.io/specification/2025-06-18/server/prompts
- MCP tools: https://modelcontextprotocol.io/specification/2025-06-18/server/tools
- Unity Git dependencies: https://docs.unity3d.com/Manual/upm-git.html
- Unity package layout: https://docs.unity3d.com/Manual/cus-layout.html
