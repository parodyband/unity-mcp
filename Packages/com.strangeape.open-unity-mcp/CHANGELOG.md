# Changelog

## 0.12.0

- Added an opt-in access token for the in-editor MCP server (addresses the "no authentication = local RCE" review finding). It is **off by default**: `/mcp` is gated only by the loopback-origin check unless you enable "Require Access Token" in `Preferences > Open Unity MCP`.
- When enabled, `POST /mcp` requires the token via `Authorization: Bearer <token>` (or `X-Open-Unity-Mcp-Token: <token>` for clients that cannot set `Authorization`); a missing or wrong token returns `401` with a JSON-RPC error explaining where to find the token. `/health` stays unauthenticated (liveness probe, no sensitive data).
- The token is a stable per-user+project 64-hex-character secret generated on first use and persisted in `EditorPrefs`, so it survives editor restarts and client configs never go stale. Preferences shows a masked/copyable token with a "Regenerate" action.
- The required flag and token are snapshotted on the main thread when the server starts (never read from `EditorPrefs` on the accept-loop threads), so a settings or token change takes effect on the **next server restart** — noted in the Preferences UI.
- The server always writes the token into `Temp/OpenUnityMcp/server-status.json` (whether or not enforcement is on). The bundled stdio sidecar reads it from there and attaches both headers on every forward, so enforcement can be toggled on in Unity with **no client-side change**. The sidecar refreshes the token after a recovery and, on a `401`, silently re-reads the status file and resends once (the editor rejects before running any tool, so the resend cannot duplicate a mutation) before forwarding a persistent `401` through.
- Client setup is unchanged for the sidecar (the token flows via the gitignored status file, never written into `.mcp.json`). When enforcement is on at setup time, the Claude Code setup dialog now notes that the named `open-unity-mcp-http` fallback entry needs a manually-added `Authorization` header — the secret is deliberately not written into any committable config.
- Moved the `unity.execute_csharp` compile stage off the editor main thread (addresses the last "main-thread freeze" review finding for this tool). The external compiler process — which can run for the full user-configurable `timeoutSeconds` (up to 60s) — plus its temp-file IO now run on the server accept-loop thread, so the editor UI stays responsive throughout the compile instead of stalling on the main-thread pump.
- Only the two stages that touch Unity APIs remain on the main thread: a short idle check that rejects the call while the editor is compiling/updating and snapshots main-thread-only paths, and the execution stage that loads the compiled assembly, invokes the entry point, and serializes the result (bounded by a 60s main-thread timeout). Load-time path snapshots (project root, editor application path) are captured on an `[InitializeOnLoadMethod]` so the compile stage never reads `Application.dataPath` or `EditorApplication.applicationPath` off-thread.
- The execution stage re-checks `isCompiling`/`isUpdating` before loading the assembly, so if a domain reload begins between the idle check and execution the call returns the "editor is compiling" error rather than loading code into a dying domain. Arbitrary user code running on the main thread can still hang the editor (e.g. an unbounded loop); this remains inherent to in-process execution and is now noted in the tool description. The response payload shape (`compiled`/`executed`/`sourcePath`/`result`/etc.) is unchanged.
- Added an end-to-end NUnit smoke test that compiles and runs `return 1 + 1;` through `tools/call` and asserts `executed:true`, exercising the new caller-thread dispatch path with the real Unity compiler.
- Added a per-tool `runOnCallerThread` opt-out in the tool registry so a tool can run its body on the caller thread instead of the main-thread pump; the enabled-check still marshals onto the main thread to read `EditorPrefs`, and the `try/catch → "Tool failed: …"` behavior is identical to main-thread tools. Only `unity.execute_csharp` opts in.
- Removed dead code: an unused `using System;` in `OpenUnityMcpSettings.cs` and the unreferenced `OpenUnityMcpClientSetup.ClaudeDesktopConfigPath` property (`ClaudeDesktopConfigPaths` remains).

## 0.11.0

- Added a reload-surviving stdio sidecar (`Server~/open-unity-mcp-sidecar.js`, Node 18+, zero npm dependencies) that becomes the MCP endpoint clients connect to and forwards JSON-RPC to the in-editor HTTP server. When a tool triggers a domain reload, the sidecar waits the outage out and retries instead of surfacing a connection error, so the client's MCP session stays alive across recompiles.
- The sidecar classifies transport failures: connection-level failures (the request never reached the editor) are retried transparently for any method; a mid-flight failure on `tools/call` (the tool may already have run) returns a successful JSON-RPC result telling the model the operation may or may not have applied and to verify state before retrying, rather than blind-retrying a mutation. Idempotent reads retry transparently in both cases.
- If the editor is genuinely gone (clean quit or the recovery deadline elapses), the sidecar returns a JSON-RPC error explaining that the Unity editor appears closed and how to restart the server.
- After a recovery the sidecar emits `notifications/tools/list_changed` and rewrites the `initialize` result's `capabilities.tools.listChanged` to `true` so the readiness signal is spec-legal.
- Added a Unity-side status file (`Temp/OpenUnityMcp/server-status.json`) written on server start (`running`), before an assembly reload (`reloading`), and on editor quit (`stopped`) so the sidecar can distinguish "rebooting, hold" from "gone". All writes are best-effort and never disturb a reload or quit.
- Client setup now writes the sidecar over stdio (`node <Server~/open-unity-mcp-sidecar.js> --port <port> --project <root>`) for Claude Code, Codex, and Claude Desktop. Claude Code keeps a named `open-unity-mcp-http` fallback entry for direct HTTP; Claude Desktop no longer uses the `npx mcp-remote` bridge (which had no retry-on-outage behavior).

## 0.10.0

- Fixed an editor-crashing denial of service in the JSON parser: deeply nested request bodies now fail with a normal parse error (maximum nesting depth 64) instead of an uncatchable `StackOverflowException` that killed the editor and any unsaved work.
- Fixed `NaN`/`Infinity` float values corrupting entire JSON responses; non-finite numbers now serialize as `"NaN"`/`"Infinity"`/`"-Infinity"` strings.
- Numbers beyond double range now surface as JSON-RPC parse errors (`-32700`) instead of internal errors, and integers beyond long range parse as doubles instead of failing.
- Fixed `unity.set_serialized_property` silently binding object-reference properties to the current editor selection when no target was supplied; explicit `objectReferenceObjectId`/`objectReferencePath` (or `value:null` to clear) is now required.
- Fixed a main-thread dispatch race where a timed-out tool call could still execute later — double-executing retried requests and signaling a disposed wait handle; timed-out work is now cancelled before it runs.
- Added per-tool main-thread timeout budgets so `unity.build_player` (10 minutes) and `unity.execute_csharp` (90 seconds) no longer fail at the fixed 30-second dispatch timeout while their work keeps running.
- Hardened the HTTP server: a client disconnect while writing an error response no longer escapes to the thread pool, and invalid `Content-Length` headers are rejected cleanly.
- Fixed `unity.execute_csharp` leaking temporary `.cs`/`.rsp`/`.dll` files on every call; compiler artifacts are now deleted after each run.
- Made the post-reload server restart robust against back-to-back domain reloads (restart intent is no longer lost if a second reload lands before the pending start runs) and stopped using `EditorApplication.delayCall` for it.
- Skipped MCP bootstrap entirely in AssetImportWorker background processes so package clones cannot contend for the server port.
- Fixed the HTTP server test permanently stopping an already-running MCP server: it now parks the live server during the test and restores it on the original port afterwards.

## 0.9.0

- Fixed `unity.execute_csharp` on Windows by launching Unity's bundled `csc.exe` through the Mono runtime (`MonoBleedingEdge/bin/mono.exe`) instead of the .NET Framework CLR, which failed to load `System.Text.Encoding.CodePages` and aborted compilation.

## 0.7.0

- Added prefab child targeting, ScriptableObject asset creation, prefab save/apply tools, C# fallback execution, and expanded prefab inspection metadata.
- Fixed the HTTP server test so it no longer blocks Unity main-thread dispatch while waiting for tool listings.

## 0.6.0

- Fixed MCP startup failures by dispatching tool listing and tool enablement checks through the Unity main thread.

## 0.5.0

- Moved server settings and client setup into `Preferences > Open Unity MCP`.
- Replaced the dedicated status tool window with a Preferences shortcut and kept quick server actions under `Tools > Open Unity MCP`.

## 0.4.0

- Added Scene View camera positioning and object framing tools.
- Added `unity.capture_scene_view`, which returns MCP image content with a PNG payload for vision-capable clients.
- Added optional Scene View capture persistence under `Temp/OpenUnityMcp`.

## 0.3.0

- Added explicit transform inputs and readback for scene object creation.
- Added `unity.set_transform` for correcting existing scene object transforms.
- Added `unity.create_game_objects` for batch primitive and hierarchy creation.

## 0.2.0

- Added a Scene View toolbar badge for server status and quick start/stop access.
- Added client setup helpers for Claude Code, Codex, and Claude Desktop.
- Restored full `Tools > Open Unity MCP` menu commands alongside the toolbar badge.

## 0.1.0

- Initial in-editor Streamable HTTP MCP server.
- Added project info, asset search/read/write, asset refresh, console log, and menu execution tools.
- Added asset metadata/import, package listing, selection, play-mode, console clear, scene listing, hierarchy, object selection, and GameObject creation tools.
- Added component inspection/editing and prefab inspection/instantiate/save tools.
- Added scene open, save, save-all, and close tools with dirty-scene protection.
- Added asset folder creation, copy, move, and delete tools with root-folder protection.
- Added MCP resources and prompts for project context, docs, Unity task guidance, and code review guidance.
- Added compilation status, project validation, script compilation request, build settings, and restricted player build tools.
- Added editor menu controls and package documentation.
