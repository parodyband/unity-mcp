---
name: open-unity-mcp
description: Operate a connected Unity Editor through Open Unity MCP's persistent JavaScript session, scene queries, and bulk edits. Use for Unity scene/component inspection, editor changes, visual verification, and compilation diagnostics when this MCP is connected.
---

Use `unity.run_code` when the stdio sidecar exposes it. Read [the SDK reference](references/sdk.md) before the first code cell. Direct HTTP clients use `unity.discover_tools`, `unity.call_tool`, and `unity.batch` instead.

Keep reusable query results and helpers on `state`; cell-local variables do not persist. Emit only the information needed to decide the next action. SDK calls must be awaited. For known fields, query matching components then use `unity.edit` with serialized property paths to edit and read back values in one Unity request.

Respect the user's existing authorization and constraints. Session execution has the capabilities of local code execution. A worker isolates scheduling, not permissions. SDK calls retain server-side disabled-tool checks. Do not use arbitrary code to work around disabled tools.

Observe targets before editing. Queries carry an editor epoch; bulk edits reject targets captured before a reload. Query again on stale-target errors. Bulk edits do not save scenes, apply prefab overrides to assets, or provide transactional rollback.

On errors or timeouts, inspect `unity.session_status` and current Unity state. Completed or uncertain mutations must not be replayed blindly. Resetting clears JavaScript state but does not undo or cancel Unity operations already dispatched. A draining session must finish before more edits.

Use the client's filesystem tools for bulk source changes, then refresh once. `unity.compilation.wait` waits for stable idle and returns diagnostics; it does not prove that a particular compilation succeeded. Check its diagnostics before proceeding. Use a Scene View capture when appearance matters, and emit its result to show the image.
