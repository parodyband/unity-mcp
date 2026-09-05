# Tools

The default compact catalog exposes seven workflow tools through `tools/list`. Use `unity.discover_tools` to find other enabled tools and retrieve their schemas, then invoke them through `unity.call_tool`. Turn off **Compact Tool Catalog** in Preferences > Open Unity MCP to advertise every enabled tool. Reconnect the client after changing the catalog or tool permissions.

Unity objects are identified with session-scoped `objectId` strings backed by Unity 6 `EntityId` values. Query again after reload or reopening scenes; do not persist these IDs as durable references.

## Persistent sessions (stdio sidecar)

The sidecar adds `unity.run_code`, `unity.session_status`, and `unity.reset_session` to the editor's catalog. Code cells keep reusable values on `state`, invoke the Unity SDK with `await`, and explicitly `emit` results. See the bundled [SDK reference](../Skills~/open-unity-mcp/references/sdk.md) for examples and limits.

`unity.edit_objects` accepts an `editorEpoch` from `query_scene`, 1–100 target IDs, and a `set` dictionary of 1–16 serialized property paths. It validates targets and values before applying, groups Undo records, and returns final values in the same main-thread turn. It only edits loaded scene objects/components. Reloaded epochs and disabled setters are rejected. Apply-time failures can retain partial changes. The SDK's `unity.edit` resolves matching component IDs from a query result and calls this tool once.

Session code runs as trusted local code in a terminable worker; it is not a security sandbox. `--no-code` disables session tools. A reset or timeout clears JavaScript variables but cannot cancel Unity work already dispatched. Status remains callable during execution and reports in-flight requests, timings, and bounded receipt metadata. The session rejects more edits while a stopped cell's requests drain.

`unity.compilation.wait` performs bounded status polling inside the sidecar, avoiding repeated agent turns. Stable idle is not proof of successful compilation; inspect the returned diagnostics. Session state survives Unity reload but not sidecar restart. Query results carry an epoch so bulk edits can reject stale targets.

## Agent workflow

The compact catalog contains `unity.discover_tools`, `unity.call_tool`, `unity.batch`, `unity.query_scene`, `unity.get_serialized_properties`, `unity.get_compilation_status`, and `unity.capture_scene_view`.

1. Find scene targets with `unity.query_scene`, filtering by `name`, `componentType`, `scenePath`, or `rootObjectId`. Results contain object IDs and matching component IDs. Use `nextOffset` while `hasMore=true`; restart pagination after changing the hierarchy. Default page size is 25, maximum 200. Inactive objects are included by default.
2. Read only needed properties with `unity.get_serialized_properties` and `propertyPaths`. Missing paths appear in `missingPaths`. For exploratory reads, use `filter`, `offset`, and `limit`; `nextOffset` is present when `truncated=true`.
3. Search tool summaries with `unity.discover_tools` and `query`. Pass an exact `name` to retrieve its full schema. The metadata identifies which tools support batching.
4. Invoke a discovered tool through `unity.call_tool` with `name` and `arguments`, or group dependent operations in `unity.batch`.
5. Verify the final state with property reads or a Scene View capture. Edit source files with the client's filesystem tools when available, then invoke `unity.refresh_assets` once and inspect compilation diagnostics.

Structured tool payloads are returned as `structuredContent` and as serialized JSON in a compatibility text block, following the [MCP tool result specification](https://modelcontextprotocol.io/specification/2025-11-25/server/tools#structured-content). Read-only tools include `annotations.readOnlyHint=true`. Dispatch and batch tools are conservatively marked as mutating; client approval rules must cover their nested operations. Disabled tools remain unavailable through discovery, dispatch, and batches.

### Dependent batch example

Call `unity.batch` with these arguments to create a light, configure it, and verify its intensity:

```json
{
  "operations": [
    {
      "id": "object",
      "name": "unity.create_game_object",
      "arguments": { "name": "Key Light", "select": false },
      "select": ["/created/objectId"]
    },
    {
      "id": "light",
      "name": "unity.add_component",
      "arguments": {
        "objectId": { "$ref": "object/created/objectId" },
        "componentType": "UnityEngine.Light"
      },
      "select": ["/component/objectId"]
    },
    {
      "id": "configure",
      "name": "unity.set_serialized_property",
      "arguments": {
        "objectId": { "$ref": "light/component/objectId" },
        "propertyPath": "m_Intensity",
        "value": 3.5
      },
      "select": []
    },
    {
      "id": "verify",
      "name": "unity.get_serialized_properties",
      "arguments": {
        "objectId": { "$ref": "light/component/objectId" },
        "propertyPaths": ["m_Intensity"]
      },
      "select": ["/properties/0/value"]
    }
  ]
}
```

The final step returns `result: {"/properties/0/value": 3.5}`. Each step reports its ID and error state. References address the full structured payload, even when `select` limits the returned output. Pointer segments support `~0` for `~` and `~1` for `/`.

Batches run sequentially in one main-thread turn, with at most 16 operations. They support reviewed read tools plus object creation, transforms, component addition, and serialized-property writes. Reloads, builds, C#, scene lifecycle, recursive dispatch, and asset lifecycle mutations are excluded.

The entire plan's structure, tool availability, and reference ordering are checked before execution. Unity validation and reference-path lookup occur during execution. Batches stop on the first error and retain earlier changes: **they are not transactions**. Inspect per-step results and current state before retrying. A failed mutation can have partial effects; transport interruption can also leave its outcome unknown.

A ten-second budget is checked between operations; it cannot interrupt a Unity API call already running. Returned payloads have a 262,144-character budget before the outer MCP envelope. Oversized output is marked `outputOmitted` and remaining steps stop; the operation has already run. A projection error is reported separately from execution success. Use `select` to reduce output without repeating mutations.

## Project And Editor

- `unity.get_project_info`
- `unity.get_selection`
- `unity.list_packages`
- `unity.set_play_mode`
- `unity.execute_menu_item`
- `unity.execute_csharp`
- `unity.get_compilation_status`
- `unity.validate_project`
- `unity.request_script_compilation`
- `unity.get_build_settings`

`unity.request_script_compilation` is asynchronous from the agent's point of view. If Unity reloads assemblies after a successful compile, the in-process MCP server is unloaded and restarted when it was running before the reload, so clients must tolerate a brief disconnect. After requesting compilation, retry the server `/health` endpoint, then call `unity.get_compilation_status` with `includeConsole=true` and a suitable `logLimit` to collect post-compile diagnostics. The status payload includes reload markers such as `assemblyLoadSequence`, `serverWasRunningBeforeLastAssemblyReload`, and `serverReloadedSinceLastScriptCompilationRequest`.

## Assets

- `unity.find_assets`
- `unity.get_asset_metadata`
- `unity.create_asset`
- `unity.create_scriptable_object`
- `unity.create_folder`
- `unity.copy_asset`
- `unity.move_asset`
- `unity.delete_asset`
- `unity.read_asset_text`
- `unity.write_asset_text`
- `unity.import_asset`
- `unity.refresh_assets`

`unity.write_asset_text` defers `AssetDatabase.Refresh()` by default for code-related files (`.cs`, `.asmdef`, `.asmref`, `.rsp`, and `.dll`) so agents can edit multiple files without forcing Unity to compile and reload assemblies mid-task. The tool result returns `requiresRefresh=true` and `nextTool="unity.refresh_assets"` when the write is pending import. Call `unity.refresh_assets` once after the batch of script/package edits is complete, then reconnect on `/health` if Unity reloads assemblies and call `unity.get_compilation_status` with `includeConsole=true`.

Tools that force an `AssetDatabase` refresh or script compilation are unavailable while the editor is in play mode (including the enter/exit transitions), because refreshing or recompiling during play destabilizes the editor. This covers `unity.refresh_assets`, `unity.request_script_compilation`, `unity.import_asset`, `unity.create_asset`, `unity.create_scriptable_object`, `unity.create_folder`, `unity.copy_asset`, `unity.move_asset`, `unity.delete_asset`, refreshing `unity.write_asset_text` calls (pass `refresh=false` to write without refreshing during play), and `unity.execute_menu_item` with `Assets/Refresh` or `Assets/Reimport All`. Blocked calls return an error telling the agent to exit play mode first with `unity.set_play_mode`.

## Components And Serialized Properties

- `unity.get_components`
- `unity.add_component`
- `unity.get_serialized_properties`
- `unity.set_serialized_property`

## Console

- `unity.get_console_logs`
- `unity.clear_console`

## C# Fallback

- `unity.execute_csharp`

`unity.execute_csharp` compiles transient editor C# under `Temp/OpenUnityMcp/ExecuteCSharp` and invokes a static method. By default, `code` is wrapped as statements inside `StrangeApe.OpenUnityMcp.Generated.OpenUnityMcpUserCode.Execute`; use `return` to return a value. Set `wrap=false` and provide `entryPoint` for full source. The response keeps compiler `stdout`/`stderr` separate from invocation output: `runtimeStdout` captures `System.Console.Write`/`WriteLine`, `runtimeStderr` captures `System.Console.Error`, and `logs` captures `Debug.Log`, warnings, errors, and exceptions emitted during invocation. Use `logLimit` (0-200, default 50) to bound Unity log capture. For structured inspection, prefer returning a JSON-safe value instead of relying on logs. This tool can do anything editor C# can do and should be client approval-gated.

## Scenes And Hierarchy

- `unity.get_open_scenes`
- `unity.get_hierarchy`
- `unity.find_child`
- `unity.select_object`
- `unity.create_game_object`
- `unity.create_game_objects`
- `unity.set_transform`
- `unity.open_scene`
- `unity.save_scene`
- `unity.save_all_scenes`
- `unity.close_scene`

`unity.create_game_object`, `unity.create_game_objects`, and `unity.set_transform` accept transform vectors as objects like `{"x":0,"y":1,"z":0}` or arrays like `[0,1,0]`. Use `localPosition`, `localRotationEuler`, and `localScale` when parenting objects; use `position` and `rotationEuler` for world-space values. Tool results include the final transform read back from Unity.

Example batch shape for a simple snowman:

```json
{
  "objects": [
    { "name": "Snowman", "localPosition": { "x": 0, "y": 0, "z": 0 } },
    { "name": "Body", "primitiveType": "Sphere", "parentIndex": 0, "localPosition": { "x": 0, "y": 1, "z": 0 }, "scale": { "x": 2, "y": 2, "z": 2 } },
    { "name": "Head", "primitiveType": "Sphere", "parentIndex": 0, "localPosition": { "x": 0, "y": 2.7, "z": 0 }, "scale": { "x": 1, "y": 1, "z": 1 } }
  ]
}
```

## Scene View

- `unity.set_scene_view_camera`
- `unity.frame_scene_view`
- `unity.capture_scene_view`

`unity.set_scene_view_camera` accepts a `pivot`, `rotationEuler`, `size`, `orthographic`, optional target `objectId`, and optional camera `position`. If `position` is provided, it derives the Scene View rotation from the position to the pivot or target object center.

`unity.frame_scene_view` frames a target `objectId`, or the current selection when no `objectId` is provided. It uses renderer and collider bounds when available.

`unity.capture_scene_view` renders the active Scene View camera to PNG and returns two MCP content blocks: a text metadata block and an `image` block with `mimeType: "image/png"` and base64 `data`. Set `saveToTemp` to also write the PNG under `Temp/OpenUnityMcp` and include that project-relative path in the metadata.

## Prefabs

- `unity.get_prefab_info`
- `unity.find_child`
- `unity.instantiate_prefab`
- `unity.save_as_prefab_asset`
- `unity.save_prefab_asset`
- `unity.apply_prefab_changes`

`unity.get_prefab_info` returns the prefab root object and root component IDs. `unity.get_hierarchy`, `unity.find_child`, `unity.get_components`, and `unity.add_component` accept `path`/`objectId` targets plus optional `childPath` or `childName` when you need to operate on a prefab child such as `Hit Collider` or `Display`. Use `unity.save_prefab_asset` after direct prefab asset edits, or `unity.apply_prefab_changes` to push scene prefab instance overrides back to the asset.

## Build

- `unity.build_player`

Build output is restricted to the project's ignored `Builds/` folder. For example, use `Builds/Windows/MyGame.exe`.

Write-capable and scene-mutating tools should be gated by client-side approval.
Asset lifecycle tools refuse to modify the protected `Assets` and `Packages` root folders.
Scene lifecycle tools protect dirty scenes by default; callers must explicitly save or discard unsaved changes before replacing or closing dirty scenes.
Build and compilation tools should also be gated by client-side approval because they can take time and mutate generated project artifacts.

# Resources

Resources are exposed through `resources/list` and `resources/read`.

- `unity://project/info`
- `unity://project/manifest`
- `unity://project/packages-lock`
- `unity://editor/selection`
- `unity://scene/open-scenes`
- `unity://docs/tools`
- `unity://docs/client-setup`

# Prompts

Prompts are exposed through `prompts/list` and `prompts/get`.

- `unity_editor_task`
- `unity_code_review`
