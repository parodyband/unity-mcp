# Domain-Reload Architecture Assessment & Code Review

**Date:** 2026-07-03
**Scope:** `Packages/com.strangeape.open-unity-mcp`
**Status:** Validated against source 2026-07-03; all Part 7 findings confirmed. Workstream 1 (in-editor
fixes) landed in 0.10.0 — see CHANGELOG. Workstream 2 (the reload-surviving sidecar + status file +
client-setup rewrite) landed in 0.11.0: `Server~/open-unity-mcp-sidecar.js` (Node 18+, zero deps) is now
the MCP endpoint clients connect to, backed by `Temp/OpenUnityMcp/server-status.json`; runtime decision
resolved in favor of Node (Part 8). 0.12.0 adds the opt-in access token (Part 7 MEDIUM "no authentication
= local RCE"; off by default, gated by the loopback-origin check unless enabled) and moves the
`execute_csharp` **compile** stage off the main thread (Part 7 HIGH): the external compiler process and its
file IO now run on the caller thread, so the editor UI no longer freezes for the compile window. The
remaining main-thread blocking is **inherent** and not further reducible in-process: `build_player` runs a
synchronous `BuildPipeline.BuildPlayer`, and `execute_csharp`'s **execution** stage runs arbitrary user
code on the main thread (an unbounded loop can still hang the editor). Those are documented rather than
"fixable."

---

## TL;DR

- **The reported bug is real and unfixable in-process.** When the AI triggers a recompile, Unity's
  domain reload terminates *all* managed threads and resets all statics. The in-editor MCP server
  (a `TcpListener` on a background thread) dies for the duration of compile + reload, and MCP clients
  see `ECONNREFUSED`.
- **Keeping an in-process server alive across reload is not possible.** No managed thread survives the
  reload, and there is no prior art for the one theoretical escape hatch (a native plugin owning the
  socket) — which couldn't run tools during the reload anyway, since no managed code runs at all.
- **The fix is the ecosystem consensus: an external "sidecar" process owns the MCP endpoint** the client
  connects to, stays alive across reloads, and waits out the outage instead of surfacing a connection
  error. Every mature Unity MCP project does this, including Unity's own official offering.
- **On Claude Code specifically the outage is a cliff, not a slope:** it auto-reconnects a dropped HTTP
  MCP server for only ~31 seconds, then marks it *failed* until a manual `/mcp` reconnect. Small projects
  reload under 31s and feel fine; large projects exceed it and the MCP is dead for the rest of the session.
- **Separately, a code review found one critical bug that crashes the whole editor** (unbounded JSON
  recursion → uncatchable `StackOverflowException`), plus main-thread-freeze and data-corruption bugs.
  The critical one should be fixed regardless of the architecture decision.

---

## Part 1 — The problem

The server ([`OpenUnityMcpServer.cs`](../Packages/com.strangeape.open-unity-mcp/Editor/OpenUnityMcpServer.cs))
runs entirely inside the Unity Editor: a `TcpListener` bound to `127.0.0.1:8080` on a background thread,
with tool work marshaled to the editor main thread via
[`UnityMainThread.Invoke`](../Packages/com.strangeape.open-unity-mcp/Editor/UnityMainThread.cs).

When a tool triggers script compilation (`unity.request_script_compilation`, `unity.refresh_assets`,
`unity.write_asset_text` of a `.cs` file, or `unity.execute_csharp`), Unity eventually performs a
**domain reload**. The bootstrap already stops the server in `beforeAssemblyReload` and restarts it after
reload via `SessionState`
([`OpenUnityMcpBootstrap.cs`](../Packages/com.strangeape.open-unity-mcp/Editor/OpenUnityMcpBootstrap.cs),
[`UnityMcpReloadState.cs`](../Packages/com.strangeape.open-unity-mcp/Editor/UnityMcpReloadState.cs)) — but
between those two points the server is simply **down**, and the client's connection is refused.

### Clarifying "stalls the main thread"

The user-visible symptom is "recompile → MCP down," but there are three distinct phases:

1. **Compilation** (background/async): the main thread keeps pumping `EditorApplication.update`, and the
   server threads are still alive. The MCP is actually *up* during pure compilation.
2. **Domain reload** (after compile completes): threads terminated, statics reset. **This is the real
   outage.** No C# from the old domain runs until the new domain initializes.
3. **Asset import** phase of `AssetDatabase.Refresh` (synchronous): the main thread is blocked, so any tool
   work queued to it via `Invoke` stalls even though the accept loop thread is alive.

This distinction matters for the fix: a sidecar restores **connection continuity** across phase 2, but it
does **not** make tools respond during a genuinely blocked main thread (phase 3, and the code-review
main-thread bugs below). Those are separate problems.

---

## Part 2 — Why an in-process server cannot survive reload

**Confirmed via Unity docs.** The domain-reload sequence
([Unity: domain reloading](https://docs.unity3d.com/Manual/domain-reloading.html);
[ConfigurableEnterPlayModeDetails, 2022.3](https://docs.unity3d.com/2022.3/Documentation/Manual/ConfigurableEnterPlayModeDetails.html))
explicitly includes: wait for async ops → `AppDomain.DomainUnload` → GC + finalizers →
**"Threads are terminated"** → "All JIT info is deleted" → new child domain → "Constructors are called,
and statics are assigned their default values." All managed threads (ThreadPool included) die; all statics
reset.

**The one theoretical escape hatch has no prior art.** A native (C/C++) plugin DLL is loaded once per editor
process and never unloaded until the editor exits, so it *could* own a process-lifetime socket. But:

- No search turned up **any** project doing this for a Unity editor server.
- A native DLL cannot be updated/redeployed without restarting the editor (locked file on Windows), which is
  hostile to a UPM package that ships updates.
- Even if it held the socket, **no managed code runs during the reload**, so it could only answer with a
  canned 503/retry — it cannot execute tools. It buys nothing over an external process.

**Conclusion:** the endpoint the client connects to must live outside the Unity process. This is not a
limitation of the current implementation; it is a property of the Unity Editor.

---

## Part 3 — Client behavior during the outage (Claude Code specifics)

From the [official Claude Code MCP docs](https://code.claude.com/docs/en/mcp):

> "If an HTTP or SSE server disconnects mid-session, Claude Code automatically reconnects with exponential
> backoff: up to five attempts, starting at a one-second delay and doubling each time. The server appears as
> pending in `/mcp` while reconnection is in progress. After five failed attempts the server is marked as
> failed and you can retry manually from `/mcp`."

Arithmetic: 1 + 2 + 4 + 8 + 16 ≈ **~31 seconds** of automatic mid-session retry. Consequences:

- **Reload < 31s** → backoff papers over it → feels fine (this is why the bug is intermittent and why it
  rarely reproduces on small test projects).
- **Reload > 31s** → server marked **failed** → **MCP dead for the rest of the session** until a manual
  `/mcp` reconnect, even though Unity is healthy again. Large projects (e.g. IronTusk) land here.
- The **in-flight** `tools/call` that triggered the reload is never transparently retried — the model just
  sees a connection error.

Other clients (from issue trackers, medium confidence):

- **Claude Desktop** via `npx mcp-remote`: `mcp-remote` has **no** retry-on-down-server behavior (it's an
  OAuth/transport shim). Desktop dies on the **first** reload.
- **Cursor / Codex CLI**: do not auto-recover a restarted streamable-HTTP server on their own; require manual
  reconnect.

**Spec note:** a fully stateless server (no `Mcp-Session-Id`, POST-only, `Connection: close` — i.e. exactly
this implementation) is fully conformant with the MCP Streamable HTTP transport spec
([2025-06-18](https://modelcontextprotocol.io/specification/2025-06-18/basic/transports)) and, empirically,
*safer* on today's clients: several clients mishandle the spec's session-restart (404 → re-initialize) flow,
and statelessness sidesteps that entire failure class. Keep the server stateless.

---

## Part 4 — What the ecosystem does (consensus)

Every maintained Unity MCP project puts a **persistent external process between the AI client and Unity**, so
the client's MCP session survives while the in-Unity half dies and respawns around each reload. The external
process converts the outage into a client-visible behavior other than connection-refused.

| Project | External endpoint (client connects here) | Bridge to Unity | Reload behavior |
|---|---|---|---|
| **CoplayDev/unity-mcp** | Python server (`uv`), stdio or HTTP | Unity TCP :6400 (legacy) / Python WS hub (beta) | Hold + retry ~20s, then structured `{success:false, error:"reloading", hint:"retry", retry_after_ms}` |
| **CoderGamester/mcp-unity** | Node server, stdio | Unity **WebSocket** :8090 | **Queue** new requests (100 cap, 60s expiry), replay on reconnect |
| **hatayama/uLoopMCP** | Node server, stdio | Unity **TCP** | Return a **successful** MCP result telling the LLM to `sleep 3 && retry` (most agent-ergonomic) |
| **IvanMurzak/Unity-MCP** | **.NET binary** (auto-downloaded), stdio/HTTP | **SignalR**, Unity dials out | Server stays up; "brief disconnections are normal"; plugin PID persisted in EditorPrefs across reload |
| **Unity official** (`com.unity.ai.assistant`) | **relay binary** in `~/.unity/relay/`, stdio | named pipe / Unix domain socket | In-process bridge (rebuilt on reload; loses approval state in ≥2.7 — undocumented buffering) |
| **notargs/UnityNaturalMCP** | *(in-process HTTP, like us)* | — | **Accepts the outage**; documents it as a known limitation |

**Consensus:** the client-facing endpoint lives outside Unity. The only project matching the current
in-process design (UnityNaturalMCP) simply documents the disconnect and tells users to disable Reload Domain
/ avoid reload-triggering tests. Even Unity did not let clients connect to the editor directly — they ship a
relay.

**Bridge direction trend:** the two most active projects (CoplayDev beta, IvanMurzak) have Unity **dial out**
to the sidecar rather than host a listener, which eliminates a whole class of port-rebind races after reload
(TIME_WAIT leaks, bind conflicts, zombie listeners) that repeatedly bit the projects with in-Unity listeners.

---

## Part 5 — Recommended architecture

**Add a thin sidecar that becomes the MCP endpoint, and have it wait-and-retry against the existing in-Unity
server across reloads, returning structured "retry" results instead of failing.** This is the minimal version
of the consensus design and fixes the bug for *all* clients (Code, Desktop, Cursor, Codex), not just Claude
Code's 31s window.

**Half of this is already built.** The Unity side already has the reload-survival scaffolding:

- `UnityMcpReloadState` persists state across reload via `SessionState`.
- The bootstrap auto-restarts the server on the saved port after reload.
- `/health` endpoint exists.
- Tool responses already emit `recommendedRecovery` payloads describing how to reconnect.

What's missing is the external process that *uses* those signals so the client's session never drops.

**Least-churn implementation path:**

1. Keep the in-Unity HTTP server roughly as-is (it remains the thing that talks to Unity).
2. Ship a small sidecar exposing stdio (or HTTP) to the client, forwarding to `127.0.0.1:8080/mcp`. On
   `ECONNREFUSED`, poll `/health` and retry rather than surfacing the error; if the reload runs long, return a
   *successful* tool result instructing the model to wait and retry (the uLoopMCP trick).
3. Before reload, have Unity write a heartbeat/status file (`reloading: true`) so the sidecar can distinguish
   "rebooting, hold" from "genuinely dead."
4. Rebind the **same** port after reload with a bounded retry window (already restarting on the saved port —
   good; make the window explicit).
5. **Gate mutation retries:** reads retry automatically; writes / `execute_csharp` must not blind-retry
   (see Part 6).

### Open decision — sidecar runtime

| Option | Pros | Cons |
|---|---|---|
| **Node** (like uLoopMCP / CoderGamester) | `npx` already a dependency (Desktop path); smallest marginal cost; ships as a bundled JS file | requires Node on the user's machine |
| **Self-contained .NET binary** (like IvanMurzak) | keeps everything in C#; zero user prerequisites if bundled | bigger download; per-OS build/release pipeline |

Recommendation: **Node** for the smallest, fastest-to-ship version, since `npx` is already in the stack. The
.NET route is more "no prerequisites" if you're willing to own platform binaries.

---

## Part 6 — Implementation lessons worth stealing

- **Never blind-retry a mutation across a reload.** CoplayDev shipped a bug where a reload caused
  non-idempotent operations to run **40+ times** (duplicated script edits) — their issue #790. Retry reads
  freely; verify-then-retry writes. Directly relevant here because `execute_csharp` / `write_asset_text` are
  the non-idempotent tools that *trigger* the reload.
- **Write a status/heartbeat artifact before reload** (CoplayDev: `~/.unity-mcp/unity-mcp-status-*.json` with
  `reloading:true`; uLoopMCP: `Temp/domainreload.lock`) so the sidecar can tell "rebooting" from "dead."
- **Stop the listener synchronously in `beforeAssemblyReload`** and rebind the *same* port with a bounded
  retry window (uLoopMCP: 5s @ 250ms; CoplayDev: 0/1/3/5/10/30s).
- **`EditorApplication.delayCall` work is wiped by reload** — both CoplayDev (#1229) and CoderGamester (#25)
  got burned. Use `[InitializeOnLoad]` + `EditorApplication.update` idle checks and persist intent in
  `EditorPrefs`/`SessionState`/a file. (The current bootstrap uses `delayCall` for the post-reload start —
  worth auditing.)
- **Guard against AssetImportWorker clones** running `[InitializeOnLoad]` (CoplayDev #1134 native crash;
  uLoopMCP has an `IsBackgroundUnityProcess()` check).
- **Guard against compile-errors / Safe Mode:** if a compile fails there is no successful reload, so any
  recovery code gated on `afterAssemblyReload` never runs (IvanMurzak #707/#725).
- **On Windows, an unfocused editor stops ticking the loop**, so queued requests never pump; CoderGamester
  (#150) installs a timer calling `EditorApplication.QueuePlayerLoopUpdate()`.
- **After recovery, push `notifications/tools/list_changed` as a "ready" signal** (uLoopMCP) so clients
  refresh immediately instead of timing out on their next call.

---

## Part 7 — Code review findings

Ranked most-severe first. Items 1 and 6 were independently verified against source during this review; the
rest carry file:line evidence.

### CRITICAL — editor-crashing DoS via the JSON parser
[`McpJson.cs:258–336`](../Packages/com.strangeape.open-unity-mcp/Editor/McpJson.cs) — `ParseValue` /
`ParseObject` / `ParseArray` are mutually recursive with **no depth limit**. A body of ~100k `[` characters
(well under the 1 MB cap) overflows the stack, and `StackOverflowException` is **uncatchable** in .NET — the
`try/catch` in `McpProtocol.Handle` cannot stop it. Unity dies instantly, losing all unsaved work. Reachable
by *any* local process, because the origin check allows requests with no `Origin` header
([`OpenUnityMcpServer.cs:171`](../Packages/com.strangeape.open-unity-mcp/Editor/OpenUnityMcpServer.cs)).
**Fix:** add a nesting-depth counter (cap ~64) that throws a normal `FormatException`. Fix regardless of the
architecture decision.

### HIGH — the 30s `Invoke` timeout abandons work that keeps running
[`UnityMainThread.cs:41–63`](../Packages/com.strangeape.open-unity-mcp/Editor/UnityMainThread.cs) — when a
queued main-thread action exceeds 30s, the waiter throws and disposes the `ManualResetEventSlim`, but the
action **stays queued and still runs later**, calling `.Set()` on a disposed handle. A retrying client
re-queues the same side-effecting op → double execution. Root cause behind the next two items.

### HIGH — `build_player` and `execute_csharp` freeze the editor on the pump
[`UnityMcpExecutionTools.cs:160`](../Packages/com.strangeape.open-unity-mcp/Editor/UnityMcpExecutionTools.cs)
runs a full synchronous build inside `UnityMainThread.Pump`;
[`UnityMcpCSharpExecutionTools.cs:79–81`](../Packages/com.strangeape.open-unity-mcp/Editor/UnityMcpCSharpExecutionTools.cs)
invokes arbitrary user code on the main thread with no bound (a `while(true)` hangs the editor forever; the
30s timeout can't stop it because the thread never returns to pump). These are the genuine "main thread stall"
bugs — **a sidecar does not fix them.**

### MEDIUM — silent data corruption in `set_serialized_property`
[`UnityMcpComponentTools.cs:339–352`](../Packages/com.strangeape.open-unity-mcp/Editor/UnityMcpComponentTools.cs)
(verified) — if a caller sets an object-reference property but supplies neither a target
(`objectReferenceObjectId` / `objectReferencePath`) nor an explicit `value:null`, control falls through to
`ResolveObject("","")`, which returns `Selection.activeObject`
([`UnityMcpObjectUtility.cs:24`](../Packages/com.strangeape.open-unity-mcp/Editor/UnityMcpObjectUtility.cs)) —
so the property is bound to whatever is selected in the editor, then saved.

### MEDIUM — `NaN` / `Infinity` floats emit invalid JSON
[`McpJson.cs:174–177`](../Packages/com.strangeape.open-unity-mcp/Editor/McpJson.cs) writes bare `NaN` /
`Infinity` tokens, corrupting the **entire** response (not just the one field). Reachable via any
transform/property readback on an object with a non-finite value (common after physics glitches or bad
imports). Emit `null` or a string instead.

### MEDIUM — no authentication = local RCE
[`OpenUnityMcpServer.cs:169`](../Packages/com.strangeape.open-unity-mcp/Editor/OpenUnityMcpServer.cs) — the
server authorizes purely on the `Origin` header, which non-browser clients omit; any local process can call
`execute_csharp` and run arbitrary code in the editor. Defensible as a local-dev tradeoff, but given
`execute_csharp` exists, a shared-secret token is worth considering — and the sidecar is the natural place to
hold it.

### LOW
- Unhandled exception in the `HandleClient` catch when a client disconnects mid-request
  ([`OpenUnityMcpServer.cs:104–123`](../Packages/com.strangeape.open-unity-mcp/Editor/OpenUnityMcpServer.cs)) —
  the second `WriteHttpResponse` writes to a dead stream and can escape to the ThreadPool.
- `execute_csharp` leaks temp `.cs`/`.rsp`/`.dll` files and permanently loads assemblies into the domain each
  call ([`UnityMcpCSharpExecutionTools.cs:47–79`](../Packages/com.strangeape.open-unity-mcp/Editor/UnityMcpCSharpExecutionTools.cs)).
- Path sandbox does not resolve symlinks/junctions
  ([`UnityMcpPathUtility.cs:62–69`](../Packages/com.strangeape.open-unity-mcp/Editor/UnityMcpPathUtility.cs)).
- Oversized/mismatched `Content-Length` ties up a worker thread until the 30s receive timeout
  ([`OpenUnityMcpServer.cs:238–267`](../Packages/com.strangeape.open-unity-mcp/Editor/OpenUnityMcpServer.cs)).
- Numeric overflow during parse reported as `-32603` internal error instead of `-32700` parse error.

### Confirmed clean (coverage)
Path traversal via `..` / absolute / UNC paths is properly blocked; JSON *string* escaping and surrogate
pairs are correct; client-config merging refuses to write an unparseable `.mcp.json` (won't corrupt it);
prefab-contents and RenderTexture lifecycles clean up in `finally`; console-log reflection degrades gracefully
when the internal API is absent; and the tool-registry static initializer does **not** touch Unity APIs
off-thread (schema builders only construct dictionaries).

---

## Part 8 — Next steps / open decisions

Two clearly separable workstreams:

1. **Critical fixes (independent of architecture):** JSON depth guard (crash), `NaN`/`Infinity` serialization,
   and the object-reference selection bug. Small, self-contained, safe to land now.
2. **The sidecar (the real fix for the reported bug):** requires two decisions before implementation —
   - **Runtime:** Node (recommended, `npx` already in stack) vs bundled self-contained .NET binary.
   - **Scope:** smallest possible retrying stdio→HTTP proxy first, or the fuller design with the
     heartbeat/status file + post-reload `tools/list_changed` ready signal.

Note that the main-thread-freeze bugs (Part 7, HIGH items) are **not** solved by the sidecar and need their
own fix (bound/parameter-check `execute_csharp`; consider backgrounding or explicitly documenting the
synchronous nature of `build_player`).

---

## Appendix — Sources

**Unity internals**
- Domain reloading — https://docs.unity3d.com/Manual/domain-reloading.html
- ConfigurableEnterPlayModeDetails (2022.3) — https://docs.unity3d.com/2022.3/Documentation/Manual/ConfigurableEnterPlayModeDetails.html
- `EditorApplication.LockReloadAssemblies` — https://docs.unity3d.com/ScriptReference/EditorApplication.LockReloadAssemblies.html
- `AssetDatabase.Refresh` (sync for imports, async for compilation) — https://docs.unity3d.com/ScriptReference/AssetDatabase.Refresh.html
- `CompilationPipeline` — https://docs.unity3d.com/ScriptReference/Compilation.CompilationPipeline.html

**MCP spec & clients**
- Claude Code MCP docs (reconnect/backoff) — https://code.claude.com/docs/en/mcp
- MCP Streamable HTTP transport (2025-06-18) — https://modelcontextprotocol.io/specification/2025-06-18/basic/transports
- MCP 2025-11-25 changelog — https://modelcontextprotocol.io/specification/2025-11-25/changelog
- `geelen/mcp-remote` (no retry-on-down) — https://github.com/geelen/mcp-remote

**Unity MCP implementations**
- CoplayDev/unity-mcp — https://github.com/CoplayDev/unity-mcp (reload retry: `Server/src/transport/legacy/unity_connection.py`; 40×-duplication bug: issue #790; delayCall race: #1229)
- CoderGamester/mcp-unity — https://github.com/CoderGamester/mcp-unity (command queue: `Server~/src/unity/commandQueue.ts`; auto-restart fix: PR #25; Windows loop pump: PR #150)
- hatayama/uLoopMCP (→ unity-cli-loop) — https://github.com/hatayama/unity-cli-loop (`McpServerController.cs`, `unity-client.ts`; DeepWiki: Domain Reload Recovery)
- IvanMurzak/Unity-MCP — https://github.com/IvanMurzak/Unity-MCP (Troubleshooting wiki; `Startup.Editor.cs`; shared-PID bug #830)
- Unity official (`com.unity.ai.assistant`) — https://docs.unity3d.com/Packages/com.unity.ai.assistant@2.7/manual/integration/unity-mcp-overview.html (bridge-rejects-before-approval report: https://discussions.unity.com/t/mcp-bridge-hard-rejects-all-connections-before-the-user-approval-flow-runs/1721430)
- notargs/UnityNaturalMCP (in-process, accepts outage) — https://github.com/notargs/UnityNaturalMCP (issue #88)
