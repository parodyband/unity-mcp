# Unity session SDK

The stdio sidecar exposes `unity.run_code({code, timeoutMs?})`, `unity.session_status({})`, and `unity.reset_session({})`. MCP clients may prefix server tool names. Direct HTTP has no JavaScript session. Configure the sidecar with `--no-code` to disable the session tools.

Each cell has `unity`, `state`, and `emit(value)`. `await` is supported at the top level of the cell. Store persistent values as `state.targets` or `state.helper`; local `let`, `const`, and `var` declarations are scoped to the cell. State survives Unity reloads but not a sidecar restart, reset, or worker termination. It is private to this sidecar process.

```js
state.lights = await unity.scene.query({
  componentType: "UnityEngine.Light",
  name: "Key",
  limit: 100
});
emit(state.lights);
```

After inspecting the targets, a later cell can reuse them:

```js
emit(await unity.edit({
  targets: state.lights,
  set: { m_Intensity: 2.5 },
  label: "Adjust key lighting"
}));
```

`targets` is the full query response, with `editorEpoch` and `objects`. Component queries edit the matching components; queries without a component filter edit the GameObjects. Queries with more pages are rejected by `edit` to prevent accidental partial application. Narrow the query, or deliberately use `{...page, hasMore:false}` to edit one observed page. Maximum 100 targets and 16 property paths. Field names are serialized paths, not C# property names; inspect them first when unknown.

Bulk editing validates all targets and values before applying. An apply-time failure retains earlier changes and returns partial results. Final values are read back and related Undo records are grouped. No scene save or prefab asset write occurs.

| SDK method | Behavior |
| --- | --- |
| `unity.scene.query(args)` | Existing query_scene filters and pagination; returns reload marker and IDs |
| `unity.edit({targets,set,label?})` | One edit_objects request with integrated final-value readback |
| `unity.properties.read(objectId, propertyPaths)` | Exact serialized property reads |
| `unity.discover(name)` | Retrieve one enabled tool's schema |
| `unity.call(name,args)` | Invoke an underlying tool and unwrap structured content |
| `unity.batch(operations)` | Existing dependent batch format |
| `unity.view.capture(args?)` | Raw MCP capture result; `emit(await unity.view.capture())` displays the image |
| `unity.compilation.wait({timeoutMs?})` | Poll status inside the sidecar until idle for 750 ms; returns diagnostics |

SDK failures throw and mark the cell as errored, including caught failures; emitted diagnostics are retained. For a tool error, `error.result` contains the response details, including partial mutation results. Explicitly emit relevant details if catching the error. Returning a value does not emit it. Unawaited calls are drained before the cell completes, but do not rely on that for ordering.

For a compilation wait up to 60 seconds, give the containing cell a longer timeout, e.g. `unity.run_code({code:"emit(await unity.compilation.wait({timeoutMs:60000}));",timeoutMs:90000})`. A blocked main thread can still delay status reads. This is condition polling, not a compile-specific completion event.

Limits: 32 KiB source, 64 SDK calls per cell, 128 KiB emitted JSON, 8 MiB emitted image results, and a 30-second default cell deadline (100 ms to 120 seconds configurable). The worker has a bounded V8 heap. A timeout terminates the worker and loses its variables; Unity operations already sent may still apply. Reset also cancels queued code cells from the old session generation. Inspect status until `inFlight` is zero.

Status includes the latest cell's elapsed time, SDK-call count, actual editor-request count, and up to 64 operation receipts with tool name, state, and elapsed time. The sidecar checkpoints receipt metadata to a per-connection JSON file under `Temp/OpenUnityMcp/sessions`; `receiptPath` identifies it and `storageError` reports write failures. Source, arguments, and returned values are not persisted. These files are diagnostic evidence, not automatic replay instructions. A `running` receipt left by a dead process has an unknown outcome. A completed receipt means a tool response was received, not that all nested operations succeeded. These metrics do not include model reasoning latency. Unity's Temp directory is not permanent storage.
