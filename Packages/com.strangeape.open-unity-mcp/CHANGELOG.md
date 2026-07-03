# Changelog

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
