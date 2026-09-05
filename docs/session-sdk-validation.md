# Persistent session SDK

The sidecar now offers a persistent JavaScript SDK and agent-specific skill installation. See the [SDK reference](../Packages/com.strangeape.open-unity-mcp/Skills~/open-unity-mcp/references/sdk.md) and [client setup](../Packages/com.strangeape.open-unity-mcp/Documentation~/client-setup.md).

## Implemented

- Persistent `state`, explicit output, image forwarding, worker deadlines, status/reset, and cancellation of queued code cells on reset.
- A single bulk editor request that validates targets, applies serialized properties, and reads final values back, with grouped Undo and reload-epoch checks.
- Compilation-idle waiting inside the sidecar and operation receipts with elapsed time, actual editor-request counts, and Unity dispatch/execute timing.
- Bounded receipt checkpoints under Unity Temp, without source, arguments, or returned values. Unknown mutations are never replayed automatically.
- One bundled skill installed into project-local `.agents/skills/open-unity-mcp` for Codex or `.claude/skills/open-unity-mcp` for Claude Code. Customized copies are preserved. Other clients receive MCP initialization guidance.

## Validation on September 5, 2026

- Unity 6000.4.8f1 EditMode: **69 passed, zero failed**.
- Node session/transport tests: **16 passed, zero failed**.
- Existing sidecar fault-injection suite: **10 checks passed**.
- Live domain-reload acceptance: **8 checks passed**. The editor's assembly sequence advanced from 3 to 4, its epoch changed, and the JavaScript session retained its marker value and session ID. The temporary source asset was deleted afterward.
- Live SDK integration: five lights edited and verified in **one editor request**, with a measured cell duration of **5 ms**, tool execution **0.7556 ms**, and editor dispatch **0.7756 ms**. This is one small test scene in batchmode; it excludes model reasoning and does not establish large-project performance.
- Skill frontmatter validation passed. Skill installer tests cover both agent locations, managed updates, and preservation of customized files without partial skill updates.

Node tests have been added to CI. Remote CI results are available on the release commit. Setup UI captions were not visually inspected.

## Current limits

Session variables survive editor reloads, not sidecar restarts. Receipt checkpoints are diagnostic evidence in Temp, not permanent storage or exactly-once execution. Bulk edits are not transactions, and reset cannot cancel Unity code already running. The worker is not a security sandbox. Compilation waiting uses bounded polling; durable build/test job orchestration and saved-object identity remain separate follow-up work.
