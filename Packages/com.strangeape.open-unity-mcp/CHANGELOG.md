# Changelog

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
