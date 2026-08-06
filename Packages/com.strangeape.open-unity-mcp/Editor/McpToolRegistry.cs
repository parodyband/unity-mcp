using System;
using System.Collections.Generic;
using UnityEditor;

namespace StrangeApe.OpenUnityMcp
{
    internal sealed class McpTool
    {
        public readonly string Name;
        public readonly string Description;
        public readonly Dictionary<string, object> InputSchema;
        public readonly Func<Dictionary<string, object>, Dictionary<string, object>> Execute;
        public readonly int TimeoutSeconds;

        // When true, the tool body runs on the caller (accept-loop ThreadPool) thread
        // instead of being marshaled onto the editor main thread, so its non-Unity work
        // (external processes, file IO) does not freeze the editor UI. Such a tool is
        // responsible for hopping onto the main thread itself for any Unity-API work.
        public readonly bool RunOnCallerThread;

        // When true, the tool is rejected while the editor is in (or entering/exiting)
        // play mode. Forcing an AssetDatabase refresh or script compilation during play
        // mode destabilizes the editor, so those tools refuse to run until play mode ends.
        public readonly bool BlockedInPlayMode;

        public McpTool(string name, string description, Dictionary<string, object> inputSchema, Func<Dictionary<string, object>, Dictionary<string, object>> execute, int timeoutSeconds = UnityMainThread.DefaultTimeoutSeconds, bool runOnCallerThread = false, bool blockedInPlayMode = false)
        {
            Name = name;
            Description = description;
            InputSchema = inputSchema;
            Execute = execute;
            TimeoutSeconds = timeoutSeconds;
            RunOnCallerThread = runOnCallerThread;
            BlockedInPlayMode = blockedInPlayMode;
        }
    }

    internal static class McpToolRegistry
    {
        private static readonly McpTool[] Tools =
        {
            UnityMcpComponentTools.AddComponent,
            UnityMcpComponentTools.GetComponents,
            UnityMcpComponentTools.GetSerializedProperties,
            UnityMcpComponentTools.SetSerializedProperty,
            UnityMcpPrefabTools.ApplyPrefabChanges,
            UnityMcpAssetTools.CopyAsset,
            UnityMcpAssetTools.CreateAsset,
            UnityMcpAssetTools.CreateFolder,
            UnityMcpAssetTools.CreateScriptableObject,
            UnityMcpAssetTools.DeleteAsset,
            UnityMcpCSharpExecutionTools.ExecuteCSharp,
            UnityMcpAssetTools.GetAssetMetadata,
            UnityMcpAssetTools.ImportAsset,
            UnityMcpAssetTools.MoveAsset,
            UnityMcpExecutionTools.BuildPlayer,
            UnityMcpEditorTools.ClearConsole,
            UnityMcpExecutionTools.GetBuildSettings,
            UnityMcpExecutionTools.GetCompilationStatus,
            UnityMcpEditorTools.GetSelection,
            UnityMcpEditorTools.ListPackages,
            UnityMcpExecutionTools.RequestScriptCompilation,
            UnityMcpEditorTools.SetPlayMode,
            UnityMcpExecutionTools.ValidateProject,
            UnityMcpPrefabTools.FindChild,
            UnityMcpPrefabTools.GetPrefabInfo,
            UnityMcpPrefabTools.InstantiatePrefab,
            UnityMcpPrefabTools.SaveAsPrefabAsset,
            UnityMcpPrefabTools.SavePrefabAsset,
            UnityMcpTools.GetProjectInfo,
            UnityMcpTools.ExecuteMenuItem,
            UnityMcpTools.FindAssets,
            UnityMcpTools.GetConsoleLogs,
            UnityMcpTools.ReadAssetText,
            UnityMcpTools.RefreshAssets,
            UnityMcpSceneTools.CloseScene,
            UnityMcpSceneTools.CreateGameObject,
            UnityMcpSceneTools.CreateGameObjects,
            UnityMcpSceneTools.GetHierarchy,
            UnityMcpSceneTools.GetOpenScenes,
            UnityMcpSceneTools.OpenScene,
            UnityMcpSceneTools.SaveAllScenes,
            UnityMcpSceneTools.SaveScene,
            UnityMcpSceneTools.SelectObject,
            UnityMcpSceneTools.SetTransform,
            UnityMcpSceneViewTools.CaptureSceneView,
            UnityMcpSceneViewTools.FrameSceneView,
            UnityMcpSceneViewTools.SetSceneViewCamera,
            UnityMcpTools.WriteAssetText
        };

        static McpToolRegistry()
        {
            Array.Sort(Tools, (left, right) => string.CompareOrdinal(left.Name, right.Name));
        }

        public static IEnumerable<string> AllToolNames
        {
            get
            {
                foreach (var tool in Tools)
                {
                    yield return tool.Name;
                }
            }
        }

        public static string GetToolDescription(string name)
        {
            foreach (var tool in Tools)
            {
                if (string.Equals(tool.Name, name, StringComparison.Ordinal))
                {
                    return tool.Description;
                }
            }

            return string.Empty;
        }

        public static Dictionary<string, object> ListTools()
        {
            return UnityMainThread.Invoke(ListToolsOnMainThread);
        }

        private static Dictionary<string, object> ListToolsOnMainThread()
        {
            var tools = new List<object>();
            foreach (var tool in Tools)
            {
                if (!OpenUnityMcpSettings.IsToolEnabled(tool.Name))
                {
                    continue;
                }

                tools.Add(McpJson.Object(
                    "name", tool.Name,
                    "description", tool.Description,
                    "inputSchema", tool.InputSchema));
            }

            return McpJson.Object("tools", tools);
        }

        public static Dictionary<string, object> Call(string name, Dictionary<string, object> arguments)
        {
            foreach (var tool in Tools)
            {
                if (!string.Equals(tool.Name, name, StringComparison.Ordinal))
                {
                    continue;
                }

                return tool.RunOnCallerThread
                    ? CallOnCallerThread(tool, arguments)
                    : UnityMainThread.Invoke(() => CallOnMainThread(tool, arguments), tool.TimeoutSeconds);
            }

            throw new InvalidOperationException("Unknown tool: " + name);
        }

        private static Dictionary<string, object> CallOnMainThread(McpTool tool, Dictionary<string, object> arguments)
        {
            if (!OpenUnityMcpSettings.IsToolEnabled(tool.Name))
            {
                return ToolText("Tool '" + tool.Name + "' is disabled in Open Unity MCP preferences.", true);
            }

            if (tool.BlockedInPlayMode && IsInPlayMode())
            {
                return PlayModeBlockedResult(tool.Name);
            }

            return ExecuteGuarded(tool, arguments);
        }

        // Runs the tool body on the caller thread (the server accept loop's ThreadPool
        // thread). The enabled-check reads EditorPrefs, which is main-thread-only, so it
        // is marshaled onto the main thread via a short Invoke; the tool itself is then
        // responsible for hopping onto the main thread for any Unity-API work it does.
        private static Dictionary<string, object> CallOnCallerThread(McpTool tool, Dictionary<string, object> arguments)
        {
            if (!UnityMainThread.Invoke(() => OpenUnityMcpSettings.IsToolEnabled(tool.Name)))
            {
                return ToolText("Tool '" + tool.Name + "' is disabled in Open Unity MCP preferences.", true);
            }

            if (tool.BlockedInPlayMode && UnityMainThread.Invoke(IsInPlayMode))
            {
                return PlayModeBlockedResult(tool.Name);
            }

            return ExecuteGuarded(tool, arguments);
        }

        // Main-thread only. Covers active play mode plus the enter/exit transitions,
        // where refresh and compilation are just as unsafe.
        internal static bool IsInPlayMode()
        {
            return EditorApplication.isPlaying || EditorApplication.isPlayingOrWillChangePlaymode;
        }

        internal static Dictionary<string, object> PlayModeBlockedResult(string toolName)
        {
            return ToolText(
                "Cannot run '" + toolName + "' while the editor is in play mode: forcing an asset refresh or script compilation during play mode destabilizes the editor. " +
                "Exit play mode first (unity.set_play_mode with isPlaying=false), then retry.",
                true);
        }

        private static Dictionary<string, object> ExecuteGuarded(McpTool tool, Dictionary<string, object> arguments)
        {
            try
            {
                return tool.Execute(arguments);
            }
            catch (Exception ex)
            {
                return ToolText("Tool failed: " + ex.Message, true);
            }
        }

        public static Dictionary<string, object> ToolText(string text, bool isError = false)
        {
            return McpJson.Object(
                "content", McpJson.Array(McpJson.Object(
                    "type", "text",
                    "text", text ?? string.Empty)),
                "isError", isError);
        }

        public static Dictionary<string, object> ToolContent(List<object> content, bool isError = false)
        {
            return McpJson.Object(
                "content", content ?? new List<object>(),
                "isError", isError);
        }

        public static Dictionary<string, object> ObjectSchema(params object[] propertiesAndRequired)
        {
            var properties = new Dictionary<string, object>(StringComparer.Ordinal);
            var required = new List<object>();

            for (var i = 0; i + 1 < propertiesAndRequired.Length; i += 2)
            {
                var name = (string)propertiesAndRequired[i];
                var descriptor = propertiesAndRequired[i + 1];
                properties[name] = descriptor;
            }

            if (propertiesAndRequired.Length % 2 == 1 && propertiesAndRequired[propertiesAndRequired.Length - 1] is string[] requiredNames)
            {
                foreach (var requiredName in requiredNames)
                {
                    required.Add(requiredName);
                }
            }

            var schema = McpJson.Object(
                "type", "object",
                "properties", properties,
                "additionalProperties", false);

            if (required.Count > 0)
            {
                schema["required"] = required;
            }

            return schema;
        }

        public static Dictionary<string, object> StringProperty(string description)
        {
            return McpJson.Object("type", "string", "description", description);
        }

        public static Dictionary<string, object> IntegerProperty(string description, int minimum = int.MinValue, int maximum = int.MaxValue)
        {
            var property = McpJson.Object("type", "integer", "description", description);
            if (minimum != int.MinValue)
            {
                property["minimum"] = minimum;
            }

            if (maximum != int.MaxValue)
            {
                property["maximum"] = maximum;
            }

            return property;
        }

        public static Dictionary<string, object> NumberProperty(string description)
        {
            return McpJson.Object("type", "number", "description", description);
        }

        public static Dictionary<string, object> BooleanProperty(string description)
        {
            return McpJson.Object("type", "boolean", "description", description);
        }

        public static Dictionary<string, object> Vector3Property(string description)
        {
            return McpJson.Object(
                "type", "object",
                "description", description,
                "properties", McpJson.Object(
                    "x", NumberProperty("X component."),
                    "y", NumberProperty("Y component."),
                    "z", NumberProperty("Z component.")),
                "required", McpJson.Array("x", "y", "z"),
                "additionalProperties", false);
        }
    }
}
