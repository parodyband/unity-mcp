# Security

Open Unity MCP exposes local editor automation. Treat it like a local developer tool with write access to the Unity project.

Current safeguards:

- Binds to `127.0.0.1`.
- Rejects non-local `Origin` headers.
- Limits text file access to paths that resolve under `Assets` or `Packages`.
- Refuses asset lifecycle mutations against the protected `Assets` and `Packages` root folders.
- Caps request body and file read sizes.
- Does not start automatically unless the user enables `Tools > Open Unity MCP > Auto Start`.

Recommended client behavior:

- Ask for user approval before write-capable tools.
- Ask for user approval before `unity.execute_menu_item`, `unity.set_play_mode`, component editing, prefab mutation, scene mutation, or scene lifecycle tools.
- Ask for user approval before `unity.request_script_compilation` or `unity.build_player`; builds are restricted to `Builds/`, but they can still take time and create large artifacts.

Report security issues privately until a public disclosure path exists.
