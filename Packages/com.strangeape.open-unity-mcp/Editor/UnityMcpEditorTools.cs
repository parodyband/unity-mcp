using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace StrangeApe.OpenUnityMcp
{
    internal static class UnityMcpEditorTools
    {
        public static readonly McpTool GetSelection = new McpTool(
            "unity.get_selection",
            "Get the current Unity editor selection.",
            McpToolRegistry.ObjectSchema(),
            _ => JsonText(McpJson.Object(
                "count", Selection.objects.Length,
                "objects", DescribeObjects(Selection.objects))));

        public static readonly McpTool ClearConsole = new McpTool(
            "unity.clear_console",
            "Clear Unity console entries.",
            McpToolRegistry.ObjectSchema(),
            _ =>
            {
                var editorAssembly = typeof(EditorApplication).Assembly;
                var logEntriesType = editorAssembly.GetType("UnityEditor.LogEntries");
                var clear = logEntriesType?.GetMethod("Clear", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                clear?.Invoke(null, null);
                return McpToolRegistry.ToolText(clear != null ? "Console cleared." : "Console clear API was not found.", clear == null);
            });

        public static readonly McpTool SetPlayMode = new McpTool(
            "unity.set_play_mode",
            "Enter or exit Unity play mode, and optionally pause or unpause the editor.",
            McpToolRegistry.ObjectSchema(
                "isPlaying", McpToolRegistry.BooleanProperty("Set Unity play mode."),
                "isPaused", McpToolRegistry.BooleanProperty("Set Unity pause state.")),
            SetPlayModeImpl);

        public static readonly McpTool ListPackages = new McpTool(
            "unity.list_packages",
            "List packages from Packages/manifest.json and Packages/packages-lock.json.",
            McpToolRegistry.ObjectSchema(),
            _ => JsonText(McpJson.Object(
                "manifest", ReadJsonFile("Packages/manifest.json"),
                "lock", ReadJsonFile("Packages/packages-lock.json"))));

        private static Dictionary<string, object> SetPlayModeImpl(Dictionary<string, object> args)
        {
            var changed = false;
            if (args.ContainsKey("isPaused"))
            {
                EditorApplication.isPaused = McpJson.AsBool(args, "isPaused", false);
                changed = true;
            }

            if (args.ContainsKey("isPlaying"))
            {
                EditorApplication.isPlaying = McpJson.AsBool(args, "isPlaying", false);
                changed = true;
            }

            return JsonText(McpJson.Object(
                "changed", changed,
                "isPlaying", EditorApplication.isPlaying,
                "isPaused", EditorApplication.isPaused,
                "isCompiling", EditorApplication.isCompiling));
        }

        internal static List<object> DescribeObjects(UnityEngine.Object[] objects)
        {
            var result = new List<object>();
            foreach (var obj in objects)
            {
                result.Add(DescribeObject(obj));
            }

            return result;
        }

        internal static Dictionary<string, object> DescribeObject(UnityEngine.Object obj)
        {
            if (obj == null)
            {
                return McpJson.Object();
            }

            var gameObject = obj as GameObject;
            var component = obj as Component;
            if (gameObject == null && component != null)
            {
                gameObject = component.gameObject;
            }

            var payload = McpJson.Object(
                "name", obj.name,
                "type", obj.GetType().FullName,
                "assetPath", AssetDatabase.GetAssetPath(obj),
                "scenePath", gameObject != null ? gameObject.scene.path : string.Empty,
                "hierarchyPath", gameObject != null ? UnityMcpSceneTools.GetHierarchyPath(gameObject.transform) : string.Empty);
            if (gameObject != null)
            {
                payload["gameObject"] = UnityMcpSceneTools.DescribeGameObject(gameObject);
            }

            UnityMcpObjectUtility.AddObjectId(payload, obj);
            return payload;
        }

        private static object ReadJsonFile(string relativePath)
        {
            var fullPath = Path.Combine(UnityMcpPathUtility.ProjectRoot, relativePath);
            if (!File.Exists(fullPath))
            {
                return McpJson.Object();
            }

            try
            {
                return McpJson.Parse(File.ReadAllText(fullPath));
            }
            catch
            {
                return McpJson.Object("raw", File.ReadAllText(fullPath));
            }
        }

        private static Dictionary<string, object> JsonText(Dictionary<string, object> payload)
        {
            return McpToolRegistry.ToolText(McpJson.Stringify(payload));
        }
    }
}
