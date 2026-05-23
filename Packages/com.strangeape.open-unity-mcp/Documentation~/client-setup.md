# Client Setup

Start the server in Unity:

```text
Tools > Open Unity MCP > Start Server
```

Default endpoint:

```text
http://127.0.0.1:8080/mcp
```

## Generic Streamable HTTP Client

Configure the client with:

```text
http://127.0.0.1:8080/mcp
```

The server accepts JSON-RPC over HTTP POST and returns JSON responses. GET returns `405 Method Not Allowed` because the package does not implement server-sent event streaming yet.

## Codex

Add this to Codex config:

```toml
[mcp_servers.unity]
url = "http://127.0.0.1:8080/mcp"
```

Restart the client after editing config.

## Security

Only connect local clients you trust. The server can read and write project files under `Assets` and `Packages`, mutate scenes, open/save/close scene assets, execute editor menu items, request script compilation, and build players when a client calls those tools. Scene lifecycle tools protect dirty scenes by default, and player build output is restricted to `Builds/`.
