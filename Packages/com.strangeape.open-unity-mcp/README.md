# Open Unity MCP

Open Unity MCP runs a local MCP server inside the Unity 6+ Editor. It exposes a small set of project and asset tools over Streamable HTTP without a separate executable or paid relay.

## Install

Open your Unity 6+ project, then install the package through Unity Package Manager:

1. Open `Window > Package Manager`.
2. Click the `+` button in the top-left corner.
3. Choose `Add package from git URL...`.
4. Paste this URL:

```text
https://github.com/parodyband/unity-mcp.git?path=/Packages/com.strangeape.open-unity-mcp
```

5. Click `Add`.

## Start

Open the MCP window:

```text
Tools > Open Unity MCP > Status
```

Click **Start** to run the server. Default endpoint:

```text
http://127.0.0.1:8080/mcp
```

Enable **Auto Start** in the window to launch the server with each editor session.
The Scene View toolbar badge also shows server status and provides quick start/stop access.

## Client Config

In the same window, use the **Client Setup** buttons, or use the matching setup commands under `Tools > Open Unity MCP > Setup`:

- **Setup Claude Code** - writes `.mcp.json` in the project root
- **Setup Codex** - writes `~/.codex/config.toml`
- **Setup Claude Desktop Bridge** - writes `claude_desktop_config.json` with an `mcp-remote` stdio bridge

Claude Code and Codex connect directly to the HTTP endpoint. Claude Desktop is configured through a local `mcp-remote` stdio bridge because its local MCP config starts processes.

For clients that accept an HTTP MCP endpoint manually:

```toml
[mcp_servers.open-unity-mcp]
url = "http://127.0.0.1:8080/mcp"
```

See `Documentation~/client-setup.md` for Claude Code, Codex, and Claude Desktop config details.

## Tools

Tools identify Unity objects with `objectId` strings backed by Unity 6 `EntityId` values.

- `unity.get_project_info`
- `unity.get_selection`
- `unity.list_packages`
- `unity.set_play_mode`
- `unity.get_compilation_status`
- `unity.validate_project`
- `unity.request_script_compilation`
- `unity.get_build_settings`
- `unity.get_components`
- `unity.add_component`
- `unity.get_serialized_properties`
- `unity.set_serialized_property`
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
- `unity.get_console_logs`
- `unity.clear_console`
- `unity.execute_menu_item`
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
- `unity.get_prefab_info`
- `unity.instantiate_prefab`
- `unity.save_as_prefab_asset`
- `unity.build_player`

The server binds to `127.0.0.1` only and rejects non-local `Origin` headers.
Scene replacement and close operations refuse to discard dirty scene changes unless the tool call explicitly saves or discards them.
Player builds are restricted to the project `Builds/` folder.

More detail: `Documentation~/tools.md`.

## Resources And Prompts

The server also exposes MCP resources for project/editor context and package docs, plus two prompts:

- `unity_editor_task`
- `unity_code_review`

See `Documentation~/tools.md` for the full list.
