# Architecture

Open Unity MCP is intentionally small:

- A `TcpListener` binds to `127.0.0.1` and parses simple HTTP requests.
- `/mcp` accepts JSON-RPC 2.0 POST bodies and returns `application/json`.
- GET on `/mcp` returns 405 because this package does not stream server-sent events yet.
- Unity API work is marshalled to the editor main thread.
- Tools are registered in one deterministic registry.
- The package has no runtime assembly. An optional bundled Node stdio sidecar keeps the client connection alive across editor domain reloads.
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

The in-editor server does not implement resource subscriptions, list-changed notifications, or prompt/resource pagination. The sidecar emits tool-list change notifications after recovery. Reconnect clients after changing tool preferences.

## Workflow interface

The Node sidecar additionally owns a persistent JavaScript worker and a small Unity SDK. Cell-local variables are ephemeral; the explicit `state` object survives cells and editor domain reloads. The worker is a scheduling/lifetime mechanism for trusted local code, not a security sandbox. Editor calls still go through the same registry. SDK dispatch is serialized, and cells have code, call, output, heap, and deadline limits.

The `unity.edit_objects` module stages serialized changes across all targets before applying them. The query's editor epoch prevents using a pre-reload target set. The apply stage runs once on the main thread, groups Undo, and reads final values back. Validation errors happen before changes; apply-time errors report partial results without claiming rollback.

The sidecar permits status and reset while JavaScript is running. Reset terminates the worker; already dispatched Unity operations may still finish, so further tool calls are blocked while they drain. Operation receipts are checkpointed before dispatch and after completion to a per-connection file under `Temp/OpenUnityMcp/sessions`. The latest 64 receipts retain only identities, tool names, states, and timing; arguments and results are not written. A receipt still marked running after process death has an unknown outcome. There is no automatic replay or exactly-once guarantee. Unity Temp cleanup can remove this evidence.

Timing distinguishes cell duration, SDK calls, actual editor requests, and tool dispatch/execute duration. Dispatch timing includes queue/gate overhead; execute timing measures the tool body. Nested dispatcher execution includes its inner work. A compilation wait uses bounded status polling within one cell, not a compile-specific event subscription.

`OpenUnityMcpSkillSetup` installs two files from the shared package skill into the selected agent's project skill directory. Hashes identify managed files; changes are preflighted before writing, and customized files are preserved. MCP initialization also includes SDK guidance for clients without local skills.

The registry owns tool availability, explicit read-only annotations, and the batch allowlist. Unknown capabilities default to mutating and non-batchable. The compact catalog advertises seven tools; discovery exposes summaries or one schema, and dispatch preserves every underlying tool's availability, play-mode, and threading checks.

`UnityMcpWorkflowTools` combines scene queries and dependent execution. Batches execute in one main-thread turn, resolve references to prior structured results, and return bounded per-step output. They stop on failure without rollback. They never replay mutations, and they do not promise durable execution receipts across domain reloads.

The result module returns structured dictionaries plus compatibility text. This gives batches access to native values and lets clients inspect results without parsing text. Scene queries and serialized-property pagination reduce irrelevant output; exact property paths avoid traversal when the target fields are known.

Caller-thread tools, including the general dispatcher, retain the existing split between background work and Unity main-thread work. Batching excludes operations that compile, build, reload, or execute arbitrary code. The sidecar treats batch and dispatch calls as potentially mutating and never retries them after an uncertain mid-flight failure.

## Security Posture

The server binds only to loopback and rejects non-local browser `Origin` headers. Tools still operate inside the Unity project and should be treated as local automation capabilities. MCP clients should ask for user approval before calling tools that write files, mutate scenes, manage scene lifecycle, execute menu items, request script compilation, or build players.

## References

- MCP Streamable HTTP transport: https://modelcontextprotocol.io/specification/2025-06-18/basic/transports
- MCP resources: https://modelcontextprotocol.io/specification/2025-06-18/server/resources
- MCP prompts: https://modelcontextprotocol.io/specification/2025-06-18/server/prompts
- MCP tools: https://modelcontextprotocol.io/specification/2025-06-18/server/tools
- Unity Git dependencies: https://docs.unity3d.com/Manual/upm-git.html
- Unity package layout: https://docs.unity3d.com/Manual/cus-layout.html
