using System;
using System.Collections.Generic;

namespace StrangeApe.OpenUnityMcp
{
    internal sealed class McpTool
    {
        public readonly string Name;
        public readonly string Description;
        public readonly Dictionary<string, object> InputSchema;
        public readonly Func<Dictionary<string, object>, Dictionary<string, object>> Execute;

        public McpTool(string name, string description, Dictionary<string, object> inputSchema, Func<Dictionary<string, object>, Dictionary<string, object>> execute)
        {
            Name = name;
            Description = description;
            InputSchema = inputSchema;
            Execute = execute;
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
            UnityMcpAssetTools.CopyAsset,
            UnityMcpAssetTools.CreateFolder,
            UnityMcpAssetTools.DeleteAsset,
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
            UnityMcpPrefabTools.GetPrefabInfo,
            UnityMcpPrefabTools.InstantiatePrefab,
            UnityMcpPrefabTools.SaveAsPrefabAsset,
            UnityMcpTools.GetProjectInfo,
            UnityMcpTools.ExecuteMenuItem,
            UnityMcpTools.FindAssets,
            UnityMcpTools.GetConsoleLogs,
            UnityMcpTools.ReadAssetText,
            UnityMcpTools.RefreshAssets,
            UnityMcpSceneTools.CloseScene,
            UnityMcpSceneTools.CreateGameObject,
            UnityMcpSceneTools.GetHierarchy,
            UnityMcpSceneTools.GetOpenScenes,
            UnityMcpSceneTools.OpenScene,
            UnityMcpSceneTools.SaveAllScenes,
            UnityMcpSceneTools.SaveScene,
            UnityMcpSceneTools.SelectObject,
            UnityMcpTools.WriteAssetText
        };

        static McpToolRegistry()
        {
            Array.Sort(Tools, (left, right) => string.CompareOrdinal(left.Name, right.Name));
        }

        public static Dictionary<string, object> ListTools()
        {
            var tools = new List<object>();
            foreach (var tool in Tools)
            {
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

                try
                {
                    return UnityMainThread.Invoke(() => tool.Execute(arguments));
                }
                catch (Exception ex)
                {
                    return ToolText("Tool failed: " + ex.Message, true);
                }
            }

            throw new InvalidOperationException("Unknown tool: " + name);
        }

        public static Dictionary<string, object> ToolText(string text, bool isError = false)
        {
            return McpJson.Object(
                "content", McpJson.Array(McpJson.Object(
                    "type", "text",
                    "text", text ?? string.Empty)),
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

        public static Dictionary<string, object> BooleanProperty(string description)
        {
            return McpJson.Object("type", "boolean", "description", description);
        }
    }
}
