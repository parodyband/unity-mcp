# Tools

All tools are exposed through `tools/list` and called with `tools/call`.
Unity objects are identified with `objectId` strings backed by Unity 6 `EntityId` values.

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
