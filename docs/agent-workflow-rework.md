# Agent workflow rework

## Findings and implemented changes

The original 48-tool catalog required agents to learn many individual schemas. Component editing usually required repeated calls to locate an object, discover component IDs, set fields, and verify results. The implementation adds a seven-tool default catalog, on-demand discovery, a general dispatcher, dependent batches, and focused queries.

| Module | Friction | Implemented interface | Benefit |
| --- | --- | --- | --- |
| Tool registry | Every tool schema appears up front | Compact catalog, discover one schema, dispatch by name | Less initial schema context; legacy tools remain available |
| Workflow execution | Each dependency requires a round trip | Ordered batches with references and output selection | Create, configure, and verify in one request |
| Scene inspection | Hierarchy dumps contain unrelated objects and transforms | Filtered pages with object and matching component IDs | Direct targeting with smaller responses |
| Serialized inspection | First-N property dumps miss desired fields | Exact paths, substring filters, continuation offset | Read only requested state |
| Result encoding | Structured values are embedded in text | Shared structured-content response module | Programmatic references and consistent compatibility output |

The deeper workflow interface concentrates dependency handling and failure reporting in one module. The registry remains the seam for tool availability and reviewed capabilities. Existing mutation implementations remain responsible for Unity validation, path restrictions, and Undo behavior. This preserves locality without duplicating mutation code.

## What was removed from the default surface

The compact catalog omits individual filesystem, menu, selection, prefab, lifecycle, and build schemas. These operations remain available through discovery and dispatch. Deleting their implementations would lose useful Unity semantics, particularly GUID-preserving asset moves and dirty-scene protections. C# remains an escape hatch behind discovery instead of being advertised as the default editing workflow.

Clients that rely on per-tool approval policies can use the full catalog and disable `unity.call_tool` and `unity.batch`. Wrapper tools must be treated as having the combined capabilities of their nested operations. Server-side disabled-tool checks apply regardless of how a tool is reached.

## Deliberate limits and next architecture candidates

1. **Durable operation receipts and test jobs.** The sidecar already survives reload, but a lost response cannot establish whether a mutation completed. Persist accepted/running/completed receipts before adding automatic retries or compile/test workflows that cross reloads. This needs crash/reload tests, bounded retention, and request identity scoped to a project and editor session.
2. **Stable saved-object identity.** Current EntityIds are session-scoped. Add saved scene/asset identities alongside them, with explicit handling for unsaved objects and unloaded scenes. Never treat a stale ID as a valid new target.
3. **Incremental diagnostics.** Console tools resend recent logs. A reset-aware cursor with severity and stack-trace controls would reduce repeated diagnostic context. It must distinguish cleared logs from domain reload and avoid silently dropping errors.
4. **Large-scene traversal.** Query output is paged, but finding a late match still traverses preceding objects. Measure this in a representative large project before adding an index with invalidation costs.

These are follow-on work, not claims about the current implementation. The current batch is intentionally non-atomic and cannot interrupt a blocking Unity call. Catalog-size reduction and request-count reduction are measurable; end-to-end agent speed still needs representative task benchmarks.

## Validation

Validated on Unity 6000.4.8f1 on September 5, 2026: all 64 EditMode tests passed. The sidecar fault-injection suite also passed all 10 reported checks. No live domain-reload acceptance run or visual Preferences inspection was performed for this change.

EditMode coverage includes the HTTP dispatcher, discovery, catalog size, disabled tools, dependent references, unsafe-plan rejection, partial mutation failure, projection failure, output-budget exhaustion, scene pagination, abstract component queries, targeted properties, and missing-value safety. The existing sidecar fault-injection suite verifies connection recovery without replaying uncertain mutations.

The catalog test measured 5,506 JSON characters for the compact catalog and 37,876 for the full 52-tool catalog: about 85% less schema text. This measures serialized catalog size, not tokenizer output or end-to-end task latency. The dependent editing test adds a component, sets its intensity, and verifies it in one request.
