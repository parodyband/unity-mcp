using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.Compilation;
using UnityEngine;

namespace StrangeApe.OpenUnityMcp
{
    internal static class UnityMcpExecutionTools
    {
        public static readonly McpTool GetCompilationStatus = new McpTool(
            "unity.get_compilation_status",
            "Get Unity editor compilation, update, play-mode, and build target status.",
            McpToolRegistry.ObjectSchema(
                "includeConsole", McpToolRegistry.BooleanProperty("Include recent console log counts and entries."),
                "logLimit", McpToolRegistry.IntegerProperty("Maximum console entries to inspect when includeConsole is true.", 1, 200)),
            GetCompilationStatusImpl);

        public static readonly McpTool ValidateProject = new McpTool(
            "unity.validate_project",
            "Summarize whether the project is ready for editor automation by checking compile/update state and recent console errors.",
            McpToolRegistry.ObjectSchema(
                "logLimit", McpToolRegistry.IntegerProperty("Maximum recent console entries to inspect.", 1, 200)),
            ValidateProjectImpl);

        public static readonly McpTool RequestScriptCompilation = new McpTool(
            "unity.request_script_compilation",
            "Ask Unity to recompile scripts. A successful compile can reload assemblies, temporarily disconnecting this in-process MCP server; it restarts after reload if it was running, so reconnect on /health and then poll unity.get_compilation_status with includeConsole=true.",
            McpToolRegistry.ObjectSchema(),
            _ =>
            {
                UnityMcpReloadState.MarkScriptCompilationRequested();
                CompilationPipeline.RequestScriptCompilation();
                var payload = CreateCompilationStatusPayload();
                var port = OpenUnityMcpServer.Port > 0 ? OpenUnityMcpServer.Port : OpenUnityMcpSettings.Port;
                payload["requested"] = true;
                payload["serverMayTemporarilyDisconnect"] = true;
                payload["recommendedRecovery"] = McpJson.Object(
                    "healthUrl", "http://127.0.0.1:" + port + "/health",
                    "retryDelayMilliseconds", 500,
                    "nextTool", "unity.get_compilation_status",
                    "nextArguments", McpJson.Object(
                        "includeConsole", true,
                        "logLimit", 100));
                return JsonText(payload);
            });

        public static readonly McpTool GetBuildSettings = new McpTool(
            "unity.get_build_settings",
            "Get active build target and enabled scenes from Unity build settings.",
            McpToolRegistry.ObjectSchema(),
            _ => JsonText(McpJson.Object(
                "activeBuildTarget", EditorUserBuildSettings.activeBuildTarget.ToString(),
                "activeBuildTargetGroup", BuildPipeline.GetBuildTargetGroup(EditorUserBuildSettings.activeBuildTarget).ToString(),
                "scenes", DescribeBuildScenes())));

        public static readonly McpTool BuildPlayer = new McpTool(
            "unity.build_player",
            "Build a player with Unity BuildPipeline. Output is restricted to the project Builds folder.",
            McpToolRegistry.ObjectSchema(
                "outputPath", McpToolRegistry.StringProperty("Project-relative output path under Builds, such as Builds/Windows/OpenUnityMcp.exe."),
                "target", McpToolRegistry.StringProperty("Optional UnityEditor.BuildTarget name. Defaults to the active build target."),
                "scenes", McpJson.Object(
                    "type", "array",
                    "description", "Optional scene paths under Assets or Packages. Defaults to enabled build settings scenes.",
                    "items", McpJson.Object("type", "string")),
                "development", McpToolRegistry.BooleanProperty("Enable Development Build."),
                "allowDebugging", McpToolRegistry.BooleanProperty("Enable script debugging. Implies Development Build."),
                new[] { "outputPath" }),
            BuildPlayerImpl);

        private static string ProjectRoot => UnityMcpPathUtility.ProjectRoot;

        private static Dictionary<string, object> GetCompilationStatusImpl(Dictionary<string, object> args)
        {
            var payload = CreateCompilationStatusPayload();
            if (McpJson.AsBool(args, "includeConsole", false))
            {
                AddConsoleSummary(payload, Math.Max(1, Math.Min(200, McpJson.AsInt(args, "logLimit", 50))), true);
            }

            return JsonText(payload);
        }

        private static Dictionary<string, object> ValidateProjectImpl(Dictionary<string, object> args)
        {
            var payload = CreateCompilationStatusPayload();
            var blockingStates = new List<object>();
            if (EditorApplication.isCompiling)
            {
                blockingStates.Add("compiling");
            }

            if (EditorApplication.isUpdating)
            {
                blockingStates.Add("asset_database_updating");
            }

            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                blockingStates.Add("play_mode_transition");
            }

            var logLimit = Math.Max(1, Math.Min(200, McpJson.AsInt(args, "logLimit", 100)));
            var counts = AddConsoleSummary(payload, logLimit, false);
            if (counts.ErrorCount > 0)
            {
                blockingStates.Add("console_errors");
            }

            payload["ready"] = blockingStates.Count == 0;
            payload["blockingStates"] = blockingStates;
            return JsonText(payload);
        }

        private static Dictionary<string, object> BuildPlayerImpl(Dictionary<string, object> args)
        {
            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                return McpToolRegistry.ToolText("Unity is compiling or updating assets. Try again after the editor is idle.", true);
            }

            var outputPath = ResolveBuildOutputPath(RequireString(args, "outputPath"));
            var target = ParseBuildTarget(McpJson.AsString(args, "target"));
            var targetGroup = BuildPipeline.GetBuildTargetGroup(target);
            var scenes = ResolveBuildScenes(args);
            if (scenes.Length == 0)
            {
                return McpToolRegistry.ToolText("No build scenes were provided and no enabled scenes are present in Build Settings.", true);
            }

            var parentDirectory = Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrEmpty(parentDirectory))
            {
                Directory.CreateDirectory(parentDirectory);
            }

            var buildOptions = BuildOptions.None;
            if (McpJson.AsBool(args, "development", false) || McpJson.AsBool(args, "allowDebugging", false))
            {
                buildOptions |= BuildOptions.Development;
            }

            if (McpJson.AsBool(args, "allowDebugging", false))
            {
                buildOptions |= BuildOptions.AllowDebugging;
            }

            var options = new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = outputPath,
                target = target,
                targetGroup = targetGroup,
                options = buildOptions
            };

            var report = BuildPipeline.BuildPlayer(options);
            var summary = report.summary;
            return JsonText(McpJson.Object(
                "result", summary.result.ToString(),
                "platform", summary.platform.ToString(),
                "platformGroup", targetGroup.ToString(),
                "outputPath", UnityMcpPathUtility.MakeProjectRelative(outputPath),
                "totalErrors", summary.totalErrors,
                "totalWarnings", summary.totalWarnings,
                "totalSize", summary.totalSize,
                "totalTimeSeconds", summary.totalTime.TotalSeconds,
                "scenes", scenes));
        }

        private static Dictionary<string, object> CreateCompilationStatusPayload()
        {
            var activeBuildTarget = EditorUserBuildSettings.activeBuildTarget;
            var payload = McpJson.Object(
                "isCompiling", EditorApplication.isCompiling,
                "isUpdating", EditorApplication.isUpdating,
                "isPlaying", EditorApplication.isPlaying,
                "isPaused", EditorApplication.isPaused,
                "isPlayingOrWillChangePlaymode", EditorApplication.isPlayingOrWillChangePlaymode,
                "activeBuildTarget", activeBuildTarget.ToString(),
                "activeBuildTargetGroup", BuildPipeline.GetBuildTargetGroup(activeBuildTarget).ToString(),
                "editorTimeSinceStartup", EditorApplication.timeSinceStartup);
            UnityMcpReloadState.AddToPayload(payload);
            return payload;
        }

        private static ConsoleCounts AddConsoleSummary(Dictionary<string, object> payload, int logLimit, bool includeEntries)
        {
            var logs = UnityMcpConsoleLogReader.Read(logLimit);
            var errors = 0;
            var warnings = 0;
            foreach (var entry in logs)
            {
                var log = entry as Dictionary<string, object>;
                if (log == null)
                {
                    continue;
                }

                var type = McpJson.AsString(log, "type", string.Empty);
                if (string.Equals(type, "error", StringComparison.Ordinal) || string.Equals(type, "assert", StringComparison.Ordinal))
                {
                    errors++;
                }
                else if (string.Equals(type, "warning", StringComparison.Ordinal))
                {
                    warnings++;
                }
            }

            payload["inspectedConsoleLogCount"] = logs.Count;
            payload["recentConsoleErrorCount"] = errors;
            payload["recentConsoleWarningCount"] = warnings;
            if (includeEntries)
            {
                payload["recentConsoleLogs"] = logs;
            }

            return new ConsoleCounts(errors, warnings);
        }

        private static List<object> DescribeBuildScenes()
        {
            var scenes = new List<object>();
            foreach (var scene in EditorBuildSettings.scenes)
            {
                scenes.Add(McpJson.Object(
                    "path", scene.path,
                    "guid", scene.guid.ToString(),
                    "enabled", scene.enabled));
            }

            return scenes;
        }

        private static BuildTarget ParseBuildTarget(string targetName)
        {
            if (string.IsNullOrEmpty(targetName))
            {
                return EditorUserBuildSettings.activeBuildTarget;
            }

            try
            {
                return (BuildTarget)Enum.Parse(typeof(BuildTarget), targetName, true);
            }
            catch (ArgumentException)
            {
                throw new ArgumentException("Unknown BuildTarget: " + targetName);
            }
        }

        private static string[] ResolveBuildScenes(Dictionary<string, object> args)
        {
            var scenes = new List<string>();
            if (args != null && args.TryGetValue("scenes", out var rawScenes) && rawScenes != null)
            {
                var array = McpJson.AsArray(rawScenes);
                if (array == null)
                {
                    throw new ArgumentException("scenes must be an array of scene paths.");
                }

                foreach (var item in array)
                {
                    var scenePath = Convert.ToString(item);
                    if (string.IsNullOrEmpty(scenePath))
                    {
                        throw new ArgumentException("scenes cannot contain empty paths.");
                    }

                    scenePath = UnityMcpPathUtility.ResolveAssetOrPackageRelativePath(scenePath);
                    if (!scenePath.EndsWith(".unity", StringComparison.OrdinalIgnoreCase))
                    {
                        throw new ArgumentException("Build scenes must use .unity files: " + scenePath);
                    }

                    scenes.Add(scenePath);
                }

                return scenes.ToArray();
            }

            foreach (var scene in EditorBuildSettings.scenes)
            {
                if (scene.enabled && !string.IsNullOrEmpty(scene.path))
                {
                    scenes.Add(scene.path);
                }
            }

            return scenes.ToArray();
        }

        private static string ResolveBuildOutputPath(string path)
        {
            path = (path ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(path))
            {
                throw new ArgumentException("Missing required argument: outputPath");
            }

            var fullPath = Path.GetFullPath(Path.IsPathRooted(path) ? path : Path.Combine(ProjectRoot, path));
            var buildsRoot = Path.GetFullPath(Path.Combine(ProjectRoot, "Builds"));
            if (!UnityMcpPathUtility.IsSameOrChildPath(fullPath, buildsRoot) ||
                string.Equals(
                    fullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                    buildsRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException("Build output must resolve under the project Builds folder.");
            }

            return fullPath;
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

        private static Dictionary<string, object> JsonText(Dictionary<string, object> payload)
        {
            return McpToolRegistry.ToolText(McpJson.Stringify(payload));
        }

        private readonly struct ConsoleCounts
        {
            public readonly int ErrorCount;
            public readonly int WarningCount;

            public ConsoleCounts(int errorCount, int warningCount)
            {
                ErrorCount = errorCount;
                WarningCount = warningCount;
            }
        }
    }
}
