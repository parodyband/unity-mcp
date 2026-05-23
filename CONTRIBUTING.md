# Contributing

Open Unity MCP is intentionally small. Contributions should keep the package easy to audit, dependency-free where practical, and runnable inside the Unity Editor without a relay executable.

## Local Test

Run edit-mode tests from a closed copy of the project:

```powershell
& 'C:\Program Files\Unity\Hub\Editor\6000.4.8f1\Editor\Unity.exe' `
  -batchmode `
  -projectPath 'D:\Unity-MCP' `
  -runTests `
  -testPlatform editmode `
  -testResults 'D:\Unity-MCP\TestResults.xml' `
  -logFile 'D:\Unity-MCP\TestRun.log'
```

Unity cannot batch-test a project that is already open in another editor instance. Use a temp copy when needed.

## Tool Guidelines

- Prefer focused tools over one large command tool.
- Keep schemas explicit and small.
- Marshal Unity API work to the main thread.
- Bound result size for hierarchy, logs, search, and file reads.
- Keep write-capable tools scoped to `Assets` and `Packages`.
- Prefer serialized-property APIs over reflection when editing Unity objects.
