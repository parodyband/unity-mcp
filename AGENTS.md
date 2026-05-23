# Repository Guidelines

## Project Structure & Module Organization

This Unity 6 project hosts the Open Unity MCP package in `Packages/com.strangeape.open-unity-mcp`.

- `Packages/com.strangeape.open-unity-mcp/Editor`: editor-only MCP server, registries, tools, settings, and UI.
- `Packages/com.strangeape.open-unity-mcp/Tests/Editor`: Unity Test Framework EditMode tests.
- `Packages/com.strangeape.open-unity-mcp/Documentation~`: package documentation for architecture, tools, and client setup.
- `Assets/Scenes` and `Assets/Settings`: sample Unity scene and render/input settings.
- `.github/workflows/unity-editmode.yml`: CI workflow for EditMode tests.

## Build, Test, and Development Commands

Open the repository root with Unity 6 or newer. Open the MCP window with `Tools > Open Unity MCP` and click **Start**; the default endpoint is `http://127.0.0.1:8080/mcp`.

Run EditMode tests locally from a closed Unity project:

```powershell
& 'C:\Program Files\Unity\Hub\Editor\6000.4.8f1\Editor\Unity.exe' `
  -batchmode `
  -projectPath 'D:\Unity-MCP' `
  -runTests `
  -testPlatform editmode `
  -testResults 'D:\Unity-MCP\TestResults.xml' `
  -logFile 'D:\Unity-MCP\TestRun.log'
```

Use `git status --short` before committing to verify that generated `Library/`, `Temp/`, logs, and build output stay untracked.

## Coding Style & Naming Conventions

Follow `.editorconfig`: UTF-8, CRLF line endings, final newline, trimmed trailing whitespace, 4-space indentation for C#, and 2-space indentation for JSON, Markdown, YAML, and TOML.

C# code uses the `StrangeApe.OpenUnityMcp` root namespace. Keep editor code in `StrangeApe.OpenUnityMcp.Editor` and tests in `StrangeApe.OpenUnityMcp.Tests`. Use PascalCase for public types and methods, camelCase for locals and parameters, and descriptive `UnityMcp*`, `Mcp*`, or `OpenUnityMcp*` type names.

## Testing Guidelines

Use Unity Test Framework EditMode tests under `Packages/com.strangeape.open-unity-mcp/Tests/Editor`. Name files after the behavior or subsystem, such as `McpProtocolTests.cs` or `UnityToolMutationTests.cs`. Add focused tests for protocol behavior, bounded results, path restrictions, and write-capable Unity tools.

CI runs `game-ci/unity-test-runner@v4` on pushes to `main`, pull requests, and manual workflow dispatch.

## Commit & Pull Request Guidelines

Recent commits use short, imperative subjects, for example `Document Package Manager installation` and `Allow manual Unity CI runs`. Keep each commit scoped to one change.

Pull requests should include a clear summary, test results or a reason tests were not run, linked issues when applicable, and screenshots for visible editor UI changes. Note changes to MCP tool behavior, security boundaries, package metadata, or CI secrets.

## Security & Configuration Notes

The MCP server must remain local-only: bind to `127.0.0.1`, reject non-local browser origins, and keep write tools scoped to safe project paths such as `Assets` and `Packages`. Do not commit Unity license credentials or personal settings.
