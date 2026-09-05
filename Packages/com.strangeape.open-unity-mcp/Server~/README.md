# Open Unity MCP sidecar

`open-unity-mcp-sidecar.js` is a small, dependency-free Node script that gives MCP
clients a **persistent stdio endpoint** in front of the in-editor HTTP server. It
exists to solve one problem: when a tool call triggers a Unity **domain reload**,
the in-editor server drops for the duration of compile + reload, and a directly
connected client sees a connection error (Claude Code only auto-reconnects an HTTP
MCP for ~31s, then marks it failed). The sidecar rides out that outage and retries,
so the client's session survives recompiles.

The in-editor HTTP server remains stateless and directly reachable — the sidecar is
purely additive. Clients that speak Streamable HTTP and don't mind dropping on every
recompile can still point straight at `http://127.0.0.1:<port>/mcp`.

## Requirements

- Node.js **18+** (uses the built-in global `fetch` and `AbortController`).
- No `npm install`, no build step. It's a single `.js` file.

The enclosing folder is named `Server~`; the trailing `~` tells Unity not to import
its contents, so no `.meta` files are generated and the script never enters the asset
database.

## Usage

```text
node open-unity-mcp-sidecar.js [--port <n>] [--project <path>] [--timeout <ms>]
```

| Option | Default | Meaning |
| --- | --- | --- |
| `--port <n>` | `8080` (or `OPEN_UNITY_MCP_PORT`) | Port of the in-editor HTTP server. Forwards to `http://127.0.0.1:<port>/mcp`. |
| `--project <path>` | current working directory | Unity project root. Used to locate the status file at `<project>/Temp/OpenUnityMcp/server-status.json`. |
| `--timeout <ms>` | `90000` | How long to wait for the editor to return after a reload before declaring it gone. |

The environment variable `OPEN_UNITY_MCP_PORT` is honored when `--port` is omitted.

## Transport

- **stdio, newline-delimited JSON-RPC (NDJSON).** One JSON message per line on
  stdin/stdout. This is **not** LSP `Content-Length` framing.
- **stdout is protocol-only.** All logging goes to **stderr**.
- For each incoming message the sidecar POSTs the raw body to the editor with
  `Content-Type: application/json`. Requests with an `id` get the HTTP response body
  written back as one line to stdout; notifications (no `id`) are posted (the editor
  answers `202`) and produce no stdout.

## Behavior during a reload

The sidecar distinguishes *where* a failure happened:

- **Connection-level failure** (`ECONNREFUSED`, connect timeout — the bytes never
  reached the editor): safe to retry any request. The sidecar reads the status file
  and polls `GET /health` with 250–500ms backoff up to the timeout, then sends the
  original request once and replies normally. The client never sees an error.
- **Mid-flight failure** (socket reset/EOF after the request was sent — the tool
  *may* have executed):
  - Idempotent methods (`initialize`, `ping`, `tools/list`, `resources/list`,
    `resources/read`, `prompts/list`, `prompts/get`) are retried transparently.
  - `tools/call` is **not** resent. Once the editor is healthy again the sidecar
    returns a **successful** JSON-RPC result whose text tells the model that Unity
    performed a reload while the call was in flight, the operation may or may not have
    applied, and it should verify state (e.g. `unity.get_compilation_status`, or
    re-read the asset) and retry if needed. A structured success keeps agent loops
    alive where a transport error would kill them, and never duplicates a mutation.
- **Deadline exceeded** (status file says `stopped`, or health never returns within
  `--timeout`): id-bearing requests get a JSON-RPC error explaining the Unity editor
  appears to be closed and how to restart it (Tools > Open Unity MCP > Start Server,
  or enable Auto Start in Preferences > Open Unity MCP).

After a recovery the sidecar emits a `notifications/tools/list_changed` notification
and rewrites the `initialize` result's `capabilities.tools.listChanged` to `true`
so that notification is spec-legal.

The Unity package writes `<project>/Temp/OpenUnityMcp/server-status.json` with a
`state` of `running`, `reloading`, or `stopped` so the sidecar can tell "rebooting,
hold" from "gone". The status file is only a hint: a successful `/health` response is
the sole proof of life, and `stopped` is the only trusted "give up" signal.

## Manual client config

The Unity menu (`Tools > Open Unity MCP > Setup`, or the buttons in
`Preferences > Open Unity MCP`) writes these for you with absolute paths. To wire a
client by hand, use the sidecar as an stdio command. Replace `<abs>` with the absolute
path to this script and `<project>` with your Unity project root.

Claude Code — `.mcp.json` in the project root:

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

Codex — `~/.codex/config.toml`:

```toml
[mcp_servers.open-unity-mcp]
command = "node"
args = ["<abs>/Server~/open-unity-mcp-sidecar.js", "--port", "8080", "--project", "<project>"]
```

Claude Desktop — `claude_desktop_config.json` (same shape as Claude Code above).

## Test

`test/sidecar-e2e.mjs` spawns the sidecar as a child process and drives it end to end
against a live editor, including forcing a real domain reload and asserting the
follow-up call rides it out with no transport error surfaced:

```text
node test/sidecar-e2e.mjs [--port <n>] [--project <path>]
```

## Persistent Unity SDK sessions

The sidecar now adds `unity.run_code`, `unity.session_status`, and `unity.reset_session` to the forwarded editor catalog. Use the bundled [skill and SDK reference](../Skills~/open-unity-mcp/SKILL.md) for the API. Add `--no-code` to retain transport-only behavior.

Session variables live in the worker's explicit `state` object. Reset/timeout loses those variables but does not cancel Unity mutations already dispatched. The sidecar waits for those requests to drain before accepting more edits. Trusted code executes in a worker with a VM context for API organization; Node's VM is [not a security mechanism](https://nodejs.org/api/vm.html).

Run the hermetic session and transport tests with:

```sh
node --test test/session.mjs test/session-transport.mjs
node test/sidecar-fault-injection.mjs
```

The Unity EditMode suite includes an optional live Node integration test. It creates five lights and verifies a bulk SDK edit through the actual stdio/HTTP/editor path. The test skips when Node is absent. Test results report editor-request count and elapsed time, not model latency.
