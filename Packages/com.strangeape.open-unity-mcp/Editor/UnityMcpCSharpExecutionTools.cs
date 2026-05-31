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

        public static readonly McpTool ExecuteCSharp = new McpTool(
            "unity.execute_csharp",
            "Compile and execute transient Unity editor C# code. This is an unrestricted fallback tool and should be approval-gated by MCP clients.",
            McpToolRegistry.ObjectSchema(
                "code", McpToolRegistry.StringProperty("C# statements to run inside a generated static Execute method, or full source when wrap=false."),
                "wrap", McpToolRegistry.BooleanProperty("Wrap code in a generated editor static method. Defaults to true."),
                "entryPoint", McpToolRegistry.StringProperty("Static method to invoke as Namespace.Type.Method. Defaults to the generated wrapper entry point."),
                "timeoutSeconds", McpToolRegistry.IntegerProperty("Compiler process timeout in seconds.", 1, 60),
                "allowUnsafe", McpToolRegistry.BooleanProperty("Compile with /unsafe enabled."),
                new[] { "code" }),
            ExecuteCSharpImpl);

        private static Dictionary<string, object> ExecuteCSharpImpl(Dictionary<string, object> args)
        {
            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                return McpToolRegistry.ToolText("Unity is compiling or updating assets. Try again after the editor is idle.", true);
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
            var workDirectory = Path.Combine(UnityMcpPathUtility.ProjectRoot, "Temp", "OpenUnityMcp", "ExecuteCSharp");
            Directory.CreateDirectory(workDirectory);

            var stamp = DateTime.UtcNow.ToString("yyyyMMddHHmmssfff", CultureInfo.InvariantCulture);
            var runId = stamp + "-" + Guid.NewGuid().ToString("N");
            var sourcePath = Path.Combine(workDirectory, "OpenUnityMcpExecute-" + runId + ".cs");
            var assemblyPath = Path.Combine(workDirectory, "OpenUnityMcpExecute-" + runId + ".dll");
            var responsePath = Path.Combine(workDirectory, "OpenUnityMcpExecute-" + runId + ".rsp");

            File.WriteAllText(sourcePath, wrap ? WrapCode(code) : code, new UTF8Encoding(false));
            File.WriteAllText(responsePath, BuildCompilerResponseFile(sourcePath, assemblyPath, allowUnsafe), new UTF8Encoding(false));

            var compiler = ResolveCompilerPath();
            var compile = RunCompiler(compiler, responsePath, timeoutSeconds);
            if (compile.ExitCode != 0)
            {
                return JsonText(McpJson.Object(
                    "compiled", false,
                    "executed", false,
                    "compiler", compiler,
                    "sourcePath", UnityMcpPathUtility.MakeProjectRelative(sourcePath),
                    "assemblyPath", UnityMcpPathUtility.MakeProjectRelative(assemblyPath),
                    "exitCode", compile.ExitCode,
                    "stdout", compile.Stdout,
                    "stderr", compile.Stderr), true);
            }

            var stopwatch = Stopwatch.StartNew();
            try
            {
                var assembly = Assembly.LoadFile(assemblyPath);
                var method = ResolveEntryPoint(assembly, entryPoint);
                var result = method.Invoke(null, null);
                stopwatch.Stop();

                return JsonText(McpJson.Object(
                    "compiled", true,
                    "executed", true,
                    "entryPoint", entryPoint,
                    "sourcePath", UnityMcpPathUtility.MakeProjectRelative(sourcePath),
                    "assemblyPath", UnityMcpPathUtility.MakeProjectRelative(assemblyPath),
                    "elapsedMilliseconds", stopwatch.ElapsedMilliseconds,
                    "stdout", compile.Stdout,
                    "stderr", compile.Stderr,
                    "resultType", result != null ? result.GetType().FullName : string.Empty,
                    "result", ToJsonSafe(result, 0)));
            }
            catch (TargetInvocationException ex)
            {
                stopwatch.Stop();
                return ExecutionError(entryPoint, sourcePath, assemblyPath, stopwatch.ElapsedMilliseconds, ex.InnerException ?? ex);
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                return ExecutionError(entryPoint, sourcePath, assemblyPath, stopwatch.ElapsedMilliseconds, ex);
            }
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

        private static string ResolveCompilerPath()
        {
            var editorDirectory = Path.GetDirectoryName(EditorApplication.applicationPath);
            var dataDirectory = Path.Combine(editorDirectory ?? string.Empty, "Data");
            var candidates = new[]
            {
                Path.Combine(dataDirectory, "MonoBleedingEdge", "lib", "mono", "4.5", "csc.exe"),
                Path.Combine(dataDirectory, "MonoBleedingEdge", "lib", "mono", "msbuild", "Current", "bin", "Roslyn", "csc.exe")
            };

            foreach (var candidate in candidates)
            {
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }

            throw new InvalidOperationException("Could not find Unity's C# compiler.");
        }

        private static CompileResult RunCompiler(string compilerPath, string responsePath, int timeoutSeconds)
        {
            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = compilerPath,
                    Arguments = "@\"" + responsePath + "\"",
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

        private static Dictionary<string, object> ExecutionError(string entryPoint, string sourcePath, string assemblyPath, long elapsedMilliseconds, Exception ex)
        {
            return JsonText(McpJson.Object(
                "compiled", true,
                "executed", false,
                "entryPoint", entryPoint,
                "sourcePath", UnityMcpPathUtility.MakeProjectRelative(sourcePath),
                "assemblyPath", UnityMcpPathUtility.MakeProjectRelative(assemblyPath),
                "elapsedMilliseconds", elapsedMilliseconds,
                "exception", McpJson.Object(
                    "type", ex.GetType().FullName,
                    "message", ex.Message,
                    "stackTrace", ex.StackTrace ?? string.Empty)), true);
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
            return McpToolRegistry.ToolText(McpJson.Stringify(payload), isError);
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
