using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace StrangeApe.OpenUnityMcp
{
    internal static class UnityMcpCSharpExecutionTools
    {
        private const int MaxCodeChars = 200000;
        private const int DefaultLogLimit = 50;
        private const int MaxLogLimit = 200;
        // Keep the largest possible captured payload comfortably below the server's 1 MiB
        // response-body limit: 2 x 50k stream chars plus 200 x 2 x 1k log-field chars.
        private const int MaxCapturedStreamChars = 50000;
        private const int MaxCapturedLogFieldChars = 1000;

        // Console.Out/Error are process-wide. Serializing the short in-process execution
        // window prevents concurrent execute_csharp calls from replacing one another's
        // writers while still allowing the external compiler stage to run concurrently.
        private static readonly object ConsoleCaptureLock = new object();

        // Bounds the main-thread execution stage (Assembly.Load + entry-point Invoke +
        // result serialization). The compile stage runs off the main thread, so this only
        // covers user code and JSON serialization. Arbitrary user code with an unbounded
        // loop can still hang the editor here — that hazard is inherent to running code
        // in-process and is called out in the tool description.
        private const int ExecutionStageTimeoutSeconds = 60;

        // Snapshotted on the main thread in stage 1 (EnsureIdleAndPaths) so the
        // caller-thread compile stage never touches Application.dataPath or
        // EditorApplication.applicationContentsPath off-thread. Populated lazily rather than in an
        // [InitializeOnLoadMethod] on purpose: forcing this type's static init at load time
        // reentrantly triggers McpToolRegistry's cctor while this tool's own field is still
        // null, which breaks the registry's name-sort. Stage 1 always runs before stage 2.
        private static string _projectRoot;
        private static string _editorContentsPath;

        public static readonly McpTool ExecuteCSharp = new McpTool(
            "unity.execute_csharp",
            "Compile and execute transient Unity editor C# code. Returns the method result plus runtime Console.Out text and Unity log messages emitted during invocation. This is an unrestricted fallback tool and should be approval-gated by MCP clients. The compile stage runs off the editor main thread so it does not freeze the UI; the compiled code itself then runs on the main thread, where an unbounded loop can still hang the editor.",
            McpToolRegistry.ObjectSchema(
                "code", McpToolRegistry.StringProperty("C# statements to run inside a generated static Execute method, or full source when wrap=false."),
                "wrap", McpToolRegistry.BooleanProperty("Wrap code in a generated editor static method. Defaults to true."),
                "entryPoint", McpToolRegistry.StringProperty("Static method to invoke as Namespace.Type.Method. Defaults to the generated wrapper entry point."),
                "timeoutSeconds", McpToolRegistry.IntegerProperty("Compiler process timeout in seconds.", 1, 60),
                "allowUnsafe", McpToolRegistry.BooleanProperty("Compile with /unsafe enabled."),
                "logLimit", McpToolRegistry.IntegerProperty("Maximum Unity log entries to capture during invocation. Defaults to 50; use 0 to disable log capture.", 0, MaxLogLimit),
                new[] { "code" }),
            ExecuteCSharpImpl,
            // The compiler process alone may take the full 60s timeoutSeconds before user code runs.
            90,
            // The compile stage (external process + file IO, no Unity API) runs on the caller
            // thread so it does not freeze the editor; only the execution stage hops to the
            // main thread via its own bounded Invoke.
            runOnCallerThread: true);

        private static Dictionary<string, object> ExecuteCSharpImpl(Dictionary<string, object> args)
        {
            // Stage 1 (main thread): reject if the editor is mid-compile/update, and snapshot
            // the main-thread-only paths the caller-thread stage needs. isCompiling/isUpdating
            // must be read fresh on the main thread, not cached.
            var busy = UnityMainThread.Invoke(EnsureIdleAndPaths);
            if (busy != null)
            {
                return busy;
            }

            var code = RequireString(args, "code");
            if (code.Length > MaxCodeChars)
            {
                return McpToolRegistry.ToolText("code exceeds the maximum length of " + MaxCodeChars + " characters.", true);
            }

            var wrap = McpJson.AsBool(args, "wrap", true);
            var entryPoint = McpJson.AsString(args, "entryPoint", DefaultEntryPoint(wrap));
            var timeoutSeconds = Math.Max(1, Math.Min(60, McpJson.AsInt(args, "timeoutSeconds", 15)));
            var allowUnsafe = McpJson.AsBool(args, "allowUnsafe", false);
            var logLimit = Math.Max(0, Math.Min(MaxLogLimit, McpJson.AsInt(args, "logLimit", DefaultLogLimit)));

            // Stage 2 (caller thread): write temp files, run the compiler process, read the
            // resulting assembly bytes. No Unity API is touched here, so the editor UI stays
            // responsive for the entire compile window.
            var workDirectory = Path.Combine(_projectRoot, "Temp", "OpenUnityMcp", "ExecuteCSharp");
            Directory.CreateDirectory(workDirectory);

            var stamp = DateTime.UtcNow.ToString("yyyyMMddHHmmssfff", CultureInfo.InvariantCulture);
            var runId = stamp + "-" + Guid.NewGuid().ToString("N");
            var sourcePath = Path.Combine(workDirectory, "OpenUnityMcpExecute-" + runId + ".cs");
            var assemblyPath = Path.Combine(workDirectory, "OpenUnityMcpExecute-" + runId + ".dll");
            var responsePath = Path.Combine(workDirectory, "OpenUnityMcpExecute-" + runId + ".rsp");

            string compiler;
            string runtime;
            CompileResult compile;
            byte[] assemblyBytes = null;
            try
            {
                File.WriteAllText(sourcePath, wrap ? WrapCode(code) : code, new UTF8Encoding(false));
                File.WriteAllText(responsePath, BuildCompilerResponseFile(sourcePath, assemblyPath, allowUnsafe), new UTF8Encoding(false));

                compiler = ResolveCompilerPath();
                runtime = ResolveMonoRuntimePath();
                compile = RunCompiler(runtime, compiler, responsePath, timeoutSeconds);
                if (compile.ExitCode == 0)
                {
                    assemblyBytes = File.ReadAllBytes(assemblyPath);
                }
            }
            finally
            {
                // Transient compiler artifacts otherwise accumulate under Temp on every call.
                TryDeleteFile(sourcePath);
                TryDeleteFile(responsePath);
                TryDeleteFile(assemblyPath);
            }

            if (compile.ExitCode != 0)
            {
                return JsonText(McpJson.Object(
                    "compiled", false,
                    "executed", false,
                    "compiler", compiler,
                    "runtime", runtime ?? string.Empty,
                    "sourcePath", MakeProjectRelative(sourcePath),
                    "assemblyPath", MakeProjectRelative(assemblyPath),
                    "exitCode", compile.ExitCode,
                    "stdout", compile.Stdout,
                    "stderr", compile.Stderr), true);
            }

            // Stage 3 (main thread): loading the assembly and running user code touches Unity
            // APIs, so it is marshaled back onto the main thread with its own bounded timeout.
            return UnityMainThread.Invoke(
                () => ExecuteStage(assemblyBytes, entryPoint, sourcePath, assemblyPath, compile, logLimit),
                ExecutionStageTimeoutSeconds);
        }

        // Runs on the main thread. Re-checks isCompiling/isUpdating to guard the rare race
        // where the editor started compiling between stage 1 and stage 3: loading into a
        // domain that is about to be torn down would be unsafe, so bail with the same error.
        private static Dictionary<string, object> ExecuteStage(byte[] assemblyBytes, string entryPoint, string sourcePath, string assemblyPath, CompileResult compile, int logLimit)
        {
            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                return McpToolRegistry.ToolText("Unity started compiling or updating assets before the compiled code could run. Try again after the editor is idle.", true);
            }

            var stopwatch = Stopwatch.StartNew();
            var capture = new ExecutionCapture(logLimit);
            try
            {
                // Loading from bytes keeps the temp .dll deletable; the assembly itself still
                // stays in the domain until the next reload, which is inherent to in-process code.
                var assembly = Assembly.Load(assemblyBytes);
                var method = ResolveEntryPoint(assembly, entryPoint);
                var result = capture.Invoke(method);
                stopwatch.Stop();

                return JsonText(McpJson.Object(
                    "compiled", true,
                    "executed", true,
                    "entryPoint", entryPoint,
                    "sourcePath", MakeProjectRelative(sourcePath),
                    "assemblyPath", MakeProjectRelative(assemblyPath),
                    "elapsedMilliseconds", stopwatch.ElapsedMilliseconds,
                    "stdout", compile.Stdout,
                    "stderr", compile.Stderr,
                    "runtimeStdout", capture.Stdout,
                    "runtimeStderr", capture.Stderr,
                    "logs", capture.Logs,
                    "logsTruncated", capture.LogsTruncated,
                    "resultType", result != null ? result.GetType().FullName : string.Empty,
                    "result", ToJsonSafe(result, 0)));
            }
            catch (TargetInvocationException ex)
            {
                stopwatch.Stop();
                return ExecutionError(entryPoint, sourcePath, assemblyPath, stopwatch.ElapsedMilliseconds, ex.InnerException ?? ex, capture);
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                return ExecutionError(entryPoint, sourcePath, assemblyPath, stopwatch.ElapsedMilliseconds, ex, capture);
            }
        }

        // Returns an error payload if the editor is busy or the load-time path snapshot is
        // unavailable, otherwise null. Runs on the main thread (called via Invoke).
        private static Dictionary<string, object> EnsureIdleAndPaths()
        {
            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                return McpToolRegistry.ToolText("Unity is compiling or updating assets. Try again after the editor is idle.", true);
            }

            if (string.IsNullOrEmpty(_projectRoot))
            {
                _projectRoot = UnityMcpPathUtility.ProjectRoot;
            }

            if (string.IsNullOrEmpty(_editorContentsPath))
            {
                _editorContentsPath = EditorApplication.applicationContentsPath;
            }

            return null;
        }

        // Project-relative rendering uses the cached root so it is safe to call from the
        // caller thread; MakeProjectRelative on UnityMcpPathUtility reads Application.dataPath.
        private static string MakeProjectRelative(string fullPath)
        {
            var root = Path.GetFullPath(_projectRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var normalized = Path.GetFullPath(fullPath);
            if (!UnityMcpPathUtility.IsSameOrChildPath(normalized, root))
            {
                return normalized.Replace('\\', '/');
            }

            return normalized.Substring(root.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Replace('\\', '/');
        }

        private static string DefaultEntryPoint(bool wrap)
        {
            return wrap
                ? "StrangeApe.OpenUnityMcp.Generated.OpenUnityMcpUserCode.Execute"
                : string.Empty;
        }

        private static string WrapCode(string code)
        {
            return
                "using System;\r\n" +
                "using System.Collections.Generic;\r\n" +
                "using System.IO;\r\n" +
                "using System.Linq;\r\n" +
                "using UnityEditor;\r\n" +
                "using UnityEngine;\r\n" +
                "\r\n" +
                "namespace StrangeApe.OpenUnityMcp.Generated\r\n" +
                "{\r\n" +
                "    public static class OpenUnityMcpUserCode\r\n" +
                "    {\r\n" +
                "        public static object Execute()\r\n" +
                "        {\r\n" +
                code + "\r\n" +
                "            return null;\r\n" +
                "        }\r\n" +
                "    }\r\n" +
                "}\r\n";
        }

        private static string BuildCompilerResponseFile(string sourcePath, string assemblyPath, bool allowUnsafe)
        {
            var builder = new StringBuilder();
            builder.AppendLine("/nologo");
            builder.AppendLine("/target:library");
            builder.AppendLine("/langversion:latest");
            builder.AppendLine("/out:\"" + assemblyPath + "\"");
            if (allowUnsafe)
            {
                builder.AppendLine("/unsafe+");
            }

            foreach (var reference in GetReferencePaths())
            {
                builder.AppendLine("/reference:\"" + reference + "\"");
            }

            builder.AppendLine("\"" + sourcePath + "\"");
            return builder.ToString();
        }

        private static IEnumerable<string> GetReferencePaths()
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (assembly.IsDynamic)
                {
                    continue;
                }

                string location;
                try
                {
                    location = assembly.Location;
                }
                catch
                {
                    continue;
                }

                if (string.IsNullOrEmpty(location) || !File.Exists(location) || !seen.Add(location))
                {
                    continue;
                }

                yield return location;
            }
        }

        // The MonoBleedingEdge folder sits directly under the editor "contents" directory
        // on Windows/Linux (".../Editor/Data/MonoBleedingEdge") but under
        // "Resources/Scripting" inside the app bundle on macOS
        // (".../Unity.app/Contents/Resources/Scripting/MonoBleedingEdge"). Probe both so
        // the compiler and runtime resolve on every platform. Deriving the base from
        // applicationContentsPath also avoids the macOS pitfall that applicationPath points
        // at the ".app" bundle itself, so GetDirectoryName(...) + "Data" lands nowhere.
        private static string ResolveMonoBleedingEdgeRoot()
        {
            var contents = _editorContentsPath ?? string.Empty;
            var candidates = new[]
            {
                Path.Combine(contents, "MonoBleedingEdge"),                          // Windows / Linux
                Path.Combine(contents, "Resources", "Scripting", "MonoBleedingEdge")  // macOS
            };

            foreach (var candidate in candidates)
            {
                if (Directory.Exists(candidate))
                {
                    return candidate;
                }
            }

            return null;
        }

        private static string ResolveCompilerPath()
        {
            var monoRoot = ResolveMonoBleedingEdgeRoot();
            if (monoRoot != null)
            {
                var candidates = new[]
                {
                    Path.Combine(monoRoot, "lib", "mono", "4.5", "csc.exe"),
                    Path.Combine(monoRoot, "lib", "mono", "msbuild", "Current", "bin", "Roslyn", "csc.exe")
                };

                foreach (var candidate in candidates)
                {
                    if (File.Exists(candidate))
                    {
                        return candidate;
                    }
                }
            }

            throw new InvalidOperationException("Could not find Unity's C# compiler.");
        }

        private static string ResolveMonoRuntimePath()
        {
            var monoRoot = ResolveMonoBleedingEdgeRoot();
            if (monoRoot == null)
            {
                return null;
            }

            var monoBin = Path.Combine(monoRoot, "bin");

            // Unity ships both the Windows PE (mono.exe) and the native ELF (mono) side by
            // side even in the Linux editor. Launching mono.exe on Linux fails with a native
            // "Access denied" because it is not an executable image for that OS, so pick the
            // binary that matches the current platform first.
            var onWindows = Environment.OSVersion.Platform == PlatformID.Win32NT;
            var candidates = onWindows
                ? new[] { Path.Combine(monoBin, "mono.exe"), Path.Combine(monoBin, "mono") }
                : new[] { Path.Combine(monoBin, "mono"), Path.Combine(monoBin, "mono.exe") };

            foreach (var candidate in candidates)
            {
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }

            return null;
        }

        private static CompileResult RunCompiler(string runtimePath, string compilerPath, string responsePath, int timeoutSeconds)
        {
            string fileName;
            string arguments;
            if (!string.IsNullOrEmpty(runtimePath))
            {
                // Unity ships csc.exe as a Mono assembly. Launching it directly under the
                // Windows .NET Framework CLR fails to load System.Text.Encoding.CodePages and
                // aborts emit, so run it through Unity's bundled Mono runtime instead.
                fileName = runtimePath;
                arguments = "\"" + compilerPath + "\" @\"" + responsePath + "\"";
            }
            else
            {
                fileName = compilerPath;
                arguments = "@\"" + responsePath + "\"";
            }

            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = fileName,
                    Arguments = arguments,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                }
            };

            process.Start();
            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(timeoutSeconds * 1000))
            {
                try
                {
                    process.Kill();
                }
                catch
                {
                    // Best-effort cleanup after compiler timeout.
                }

                return new CompileResult(-1, ReadTaskResult(stdout), ReadTaskResult(stderr) + "\nCompiler timed out.");
            }

            return new CompileResult(process.ExitCode, ReadTaskResult(stdout), ReadTaskResult(stderr));
        }

        private static string ReadTaskResult(System.Threading.Tasks.Task<string> task)
        {
            try
            {
                return task.GetAwaiter().GetResult();
            }
            catch
            {
                return string.Empty;
            }
        }

        private static MethodInfo ResolveEntryPoint(Assembly assembly, string entryPoint)
        {
            if (string.IsNullOrEmpty(entryPoint))
            {
                throw new ArgumentException("entryPoint is required when wrap=false.");
            }

            var separator = entryPoint.LastIndexOf('.');
            if (separator <= 0 || separator >= entryPoint.Length - 1)
            {
                throw new ArgumentException("entryPoint must use Namespace.Type.Method format.");
            }

            var typeName = entryPoint.Substring(0, separator);
            var methodName = entryPoint.Substring(separator + 1);
            var type = assembly.GetType(typeName);
            if (type == null)
            {
                foreach (var candidate in assembly.GetTypes())
                {
                    if (string.Equals(candidate.FullName, typeName, StringComparison.Ordinal) ||
                        string.Equals(candidate.Name, typeName, StringComparison.Ordinal))
                    {
                        type = candidate;
                        break;
                    }
                }
            }

            var method = type?.GetMethod(methodName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
            if (method == null)
            {
                throw new ArgumentException("Static entry point not found: " + entryPoint);
            }

            if (method.GetParameters().Length != 0)
            {
                throw new ArgumentException("entryPoint must not require parameters: " + entryPoint);
            }

            return method;
        }

        private static object ToJsonSafe(object value, int depth)
        {
            if (value == null || value is string || value is bool ||
                value is byte || value is sbyte || value is short || value is ushort ||
                value is int || value is uint || value is long || value is ulong ||
                value is float || value is double || value is decimal)
            {
                return value;
            }

            if (depth >= 4)
            {
                return Convert.ToString(value, CultureInfo.InvariantCulture);
            }

            if (value is Enum enumValue)
            {
                return enumValue.ToString();
            }

            if (value is UnityEngine.Object unityObject)
            {
                return UnityMcpEditorTools.DescribeObject(unityObject);
            }

            if (value is IDictionary dictionary)
            {
                var result = new Dictionary<string, object>(StringComparer.Ordinal);
                var count = 0;
                foreach (DictionaryEntry entry in dictionary)
                {
                    if (count >= 100)
                    {
                        result["truncated"] = true;
                        break;
                    }

                    result[Convert.ToString(entry.Key, CultureInfo.InvariantCulture)] = ToJsonSafe(entry.Value, depth + 1);
                    count++;
                }

                return result;
            }

            if (value is IEnumerable enumerable)
            {
                var result = new List<object>();
                var count = 0;
                foreach (var item in enumerable)
                {
                    if (count >= 100)
                    {
                        result.Add("truncated");
                        break;
                    }

                    result.Add(ToJsonSafe(item, depth + 1));
                    count++;
                }

                return result;
            }

            return Convert.ToString(value, CultureInfo.InvariantCulture);
        }

        private static Dictionary<string, object> ExecutionError(string entryPoint, string sourcePath, string assemblyPath, long elapsedMilliseconds, Exception ex, ExecutionCapture capture)
        {
            return JsonText(McpJson.Object(
                "compiled", true,
                "executed", false,
                "entryPoint", entryPoint,
                "sourcePath", UnityMcpPathUtility.MakeProjectRelative(sourcePath),
                "assemblyPath", UnityMcpPathUtility.MakeProjectRelative(assemblyPath),
                "elapsedMilliseconds", elapsedMilliseconds,
                "runtimeStdout", capture != null ? capture.Stdout : string.Empty,
                "runtimeStderr", capture != null ? capture.Stderr : string.Empty,
                "logs", capture != null ? capture.Logs : new List<object>(),
                "logsTruncated", capture != null && capture.LogsTruncated,
                "exception", McpJson.Object(
                    "type", ex.GetType().FullName,
                    "message", ex.Message,
                    "stackTrace", ex.StackTrace ?? string.Empty)), true);
        }

        private sealed class ExecutionCapture
        {
            private readonly int _logLimit;
            private readonly BoundedTextWriter _stdoutWriter = new BoundedTextWriter(MaxCapturedStreamChars);
            private readonly BoundedTextWriter _stderrWriter = new BoundedTextWriter(MaxCapturedStreamChars);
            private readonly List<object> _logs = new List<object>();
            private bool _logsTruncated;

            public ExecutionCapture(int logLimit)
            {
                _logLimit = logLimit;
            }

            public string Stdout => _stdoutWriter.GetText();
            public string Stderr => _stderrWriter.GetText();
            public List<object> Logs => _logs;
            public bool LogsTruncated => _logsTruncated;

            public object Invoke(MethodInfo method)
            {
                lock (ConsoleCaptureLock)
                {
                    var previousOut = Console.Out;
                    var previousError = Console.Error;
                    try
                    {
                        Console.SetOut(_stdoutWriter);
                        Console.SetError(_stderrWriter);
                        if (_logLimit > 0)
                        {
                            Application.logMessageReceived += OnLogMessage;
                        }

                        return method.Invoke(null, null);
                    }
                    finally
                    {
                        if (_logLimit > 0)
                        {
                            Application.logMessageReceived -= OnLogMessage;
                        }

                        Console.SetOut(previousOut);
                        Console.SetError(previousError);
                    }
                }
            }

            private void OnLogMessage(string condition, string stackTrace, LogType type)
            {
                if (_logs.Count >= _logLimit)
                {
                    _logsTruncated = true;
                    return;
                }

                _logs.Add(McpJson.Object(
                    "type", type.ToString().ToLowerInvariant(),
                    "message", Truncate(condition, MaxCapturedLogFieldChars),
                    "stackTrace", Truncate(stackTrace, MaxCapturedLogFieldChars)));
            }
        }

        private sealed class BoundedTextWriter : System.IO.TextWriter
        {
            private const string TruncatedMarker = "\n[output truncated]";
            private readonly int _maxChars;
            private readonly StringBuilder _builder = new StringBuilder();
            private bool _truncated;

            public BoundedTextWriter(int maxChars)
            {
                _maxChars = maxChars;
            }

            public override Encoding Encoding => Encoding.UTF8;

            public override void Write(char value)
            {
                Append(value.ToString());
            }

            public override void Write(string value)
            {
                Append(value);
            }

            public override void Write(char[] buffer, int index, int count)
            {
                if (buffer == null)
                {
                    return;
                }

                Append(new string(buffer, index, count));
            }

            public string GetText()
            {
                return _builder + (_truncated ? TruncatedMarker : string.Empty);
            }

            private void Append(string value)
            {
                if (string.IsNullOrEmpty(value))
                {
                    return;
                }

                if (_builder.Length >= _maxChars)
                {
                    _truncated = true;
                    return;
                }

                var remaining = _maxChars - _builder.Length;
                if (value.Length > remaining)
                {
                    _builder.Append(value, 0, remaining);
                    _truncated = true;
                    return;
                }

                _builder.Append(value);
            }
        }

        private static string Truncate(string value, int maxChars)
        {
            if (string.IsNullOrEmpty(value) || value.Length <= maxChars)
            {
                return value ?? string.Empty;
            }

            return value.Substring(0, maxChars) + "\n[output truncated]";
        }

        private static void TryDeleteFile(string path)
        {
            try
            {
                File.Delete(path);
            }
            catch
            {
                // Temp cleanup is best-effort; Unity clears Temp on the next editor restart.
            }
        }

        private static string RequireString(Dictionary<string, object> args, string name)
        {
            var value = McpJson.AsString(args, name);
            if (string.IsNullOrEmpty(value))
            {
                throw new ArgumentException("Missing required argument: " + name);
            }

            return value;
        }

        private static Dictionary<string, object> JsonText(Dictionary<string, object> payload, bool isError = false)
        {
            return McpToolRegistry.ToolJson(payload, isError);
        }

        private readonly struct CompileResult
        {
            public readonly int ExitCode;
            public readonly string Stdout;
            public readonly string Stderr;

            public CompileResult(int exitCode, string stdout, string stderr)
            {
                ExitCode = exitCode;
                Stdout = stdout ?? string.Empty;
                Stderr = stderr ?? string.Empty;
            }
        }
    }
}
