# Unity compatibility

The package targets Unity **6.0 (6000.0) and newer**. Unity 2022 LTS and earlier are outside the supported range.

Install `Packages/com.strangeape.open-unity-mcp` through Package Manager in your existing project. The repository's sample project uses **6000.4.8f1** and Unity 6.4 rendering packages. Opening that sample in an older editor is not a package compatibility test.

## Object IDs

| Editor version | Native identity API | Returned ID type |
| --- | --- | --- |
| Unity 6.0–6.2 | `GetInstanceID` / `EditorUtility.InstanceIDToObject` | `instanceId` |
| Unity 6.3 | `GetInstanceID` / `EditorUtility.EntityIdToObject` with a 32-bit ID conversion | `instanceId` |
| Unity 6.4+ | `GetEntityId` / `EditorUtility.EntityIdToObject` | `entityId` |

Every `objectId` remains a string. Earlier editors can return negative IDs, such as `"-12345"`. Pass the entire string back unchanged. `objectIdType` and prefixed fields such as `rootObjectIdType` report the native representation.

IDs identify objects within an editor session. Query them again after reloads or reopening scenes. Do not save them as durable references, convert them to JavaScript numbers, or reuse them across Unity versions.

## Test coverage

Local validation on Windows, September 27, 2026:

| Unity editor | EditMode results |
| --- | --- |
| 6000.0.44f1 | 84 passed |
| 6000.1.11f1 | 84 passed |
| 6000.2.13f1 | 84 passed |
| 6000.3.2f1 | 84 passed |
| 6000.4.0f1 | 80 passed |
| 6000.4.8f1 | 80 passed |
| 6000.5.4f1 | 80 passed |

Every run completed with zero failures or skipped tests. Older versions run four additional signed 32-bit overflow cases. The 16 Node session and transport tests also passed, including preservation of negative and 64-bit ID strings.

The EditMode CI matrix is configured for **6000.0.44f1, 6000.1.11f1, 6000.2.13f1, 6000.3.2f1, 6000.4.0f1, and 6000.5.4f1**. Each job creates a minimal project that loads the package from this checkout. This keeps the sample project's dependencies out of compatibility checks and compiles both object-ID implementations against their actual Unity APIs.

The suite covers object-ID JSON round trips, signed IDs, invariant formatting, invalid and overflowing IDs, destroyed objects, scene/component edits, prefabs, batches, and protocol behavior. This coverage does not certify every Unity patch or future release. See the [GitHub Actions runs](https://github.com/parodyband/unity-mcp/actions/workflows/unity-editmode.yml) for Linux CI results.

## Run a compatibility test locally

From the repository root, create a new test project with Node.js 20 or newer:

```powershell
node .github/scripts/create-unity-test-project.mjs Logs/UnityTests-6000.0.44f1 6000.0.44f1
```

The script refuses to overwrite an existing project. You can reuse a generated project for later test runs without recreating it. Unity Test Framework 1.6.0 requires 6000.0.44f1 or newer; this test dependency does not change the MCP package's declared minimum.

With that test project closed, run its matching installed editor:

```powershell
& 'C:\Program Files\Unity\Hub\Editor\6000.0.44f1\Editor\Unity.exe' `
  -batchmode `
  -projectPath "$PWD/Logs/UnityTests-6000.0.44f1" `
  -runTests `
  -testPlatform editmode `
  -testResults "$PWD/Logs/UnityTests-6000.0.44f1/results.xml" `
  -logFile "$PWD/Logs/UnityTests-6000.0.44f1/editor.log"
```

Check `results.xml` for the test outcome and `editor.log` for compilation errors. Generated projects, logs, and results stay under the ignored `Logs/` directory. Run editors sequentially because the tests exercise shared Unity editor preferences.
