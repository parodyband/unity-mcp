# Tools

All tools are exposed through `tools/list` and called with `tools/call`.
Unity objects are identified with `objectId` strings backed by Unity 6 `EntityId` values.

## Project And Editor

- `unity.get_project_info`
- `unity.get_selection`
- `unity.list_packages`
- `unity.set_play_mode`
- `unity.execute_menu_item`
- `unity.get_compilation_status`
- `unity.validate_project`
- `unity.request_script_compilation`
- `unity.get_build_settings`

## Assets

- `unity.find_assets`
- `unity.get_asset_metadata`
- `unity.create_folder`
- `unity.copy_asset`
- `unity.move_asset`
- `unity.delete_asset`
- `unity.read_asset_text`
- `unity.write_asset_text`
- `unity.import_asset`
- `unity.refresh_assets`

## Components And Serialized Properties

- `unity.get_components`
- `unity.add_component`
- `unity.get_serialized_properties`
- `unity.set_serialized_property`

## Console

- `unity.get_console_logs`
- `unity.clear_console`

## Scenes And Hierarchy

- `unity.get_open_scenes`
- `unity.get_hierarchy`
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

## Prefabs

- `unity.get_prefab_info`
- `unity.instantiate_prefab`
- `unity.save_as_prefab_asset`

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
