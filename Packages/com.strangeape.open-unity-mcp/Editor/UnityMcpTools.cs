using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace StrangeApe.OpenUnityMcp
{
    internal static class UnityMcpTools
    {
        public static readonly McpTool GetProjectInfo = new McpTool(
            "unity.get_project_info",
            "Get Unity version, project paths, editor state, active scene, and selection counts.",
            McpToolRegistry.ObjectSchema(),
            _ => JsonText(McpJson.Object(
                "unityVersion", Application.unityVersion,
                "projectPath", ProjectRoot,
                "assetsPath", Application.dataPath,
                "packagePath", PackageRoot,
                "companyName", Application.companyName,
                "productName", Application.productName,
                "isPlaying", EditorApplication.isPlaying,
                "isPaused", EditorApplication.isPaused,
                "isCompiling", EditorApplication.isCompiling,
                "activeScene", SceneManager.GetActiveScene().path,
                "openSceneCount", SceneManager.sceneCount,
                "selectedObjectCount", Selection.objects.Length)));

        public static readonly McpTool FindAssets = new McpTool(
            "unity.find_assets",
            "Find assets with Unity AssetDatabase.FindAssets and return paths, GUIDs, and main asset types.",
            McpToolRegistry.ObjectSchema(
                "filter", McpToolRegistry.StringProperty("Unity asset search filter, such as t:Material or PlayerController."),
                "folder", McpToolRegistry.StringProperty("Optional folder to search, relative to the project root, such as Assets or Packages/com.example."),
                "limit", McpToolRegistry.IntegerProperty("Maximum results to return.", 1, 200)),
            FindAssetsImpl);

        public static readonly McpTool ReadAssetText = new McpTool(
            "unity.read_asset_text",
            "Read a UTF-8 text asset from Assets or Packages.",
            McpToolRegistry.ObjectSchema(
                "path", McpToolRegistry.StringProperty("Project-relative path under Assets or Packages."),
                "maxBytes", McpToolRegistry.IntegerProperty("Maximum bytes to read.", 1, 1048576),
                new[] { "path" }),
            ReadAssetTextImpl);

        public static readonly McpTool WriteAssetText = new McpTool(
            "unity.write_asset_text",
            "Write a UTF-8 text file under Assets or Packages and refresh the AssetDatabase.",
            McpToolRegistry.ObjectSchema(
                "path", McpToolRegistry.StringProperty("Project-relative path under Assets or Packages."),
                "text", McpToolRegistry.StringProperty("UTF-8 text to write."),
                "createDirectories", McpToolRegistry.BooleanProperty("Create missing parent directories."),
                new[] { "path", "text" }),
            WriteAssetTextImpl);

        public static readonly McpTool RefreshAssets = new McpTool(
            "unity.refresh_assets",
            "Refresh Unity's AssetDatabase.",
            McpToolRegistry.ObjectSchema(),
            _ =>
            {
                AssetDatabase.Refresh();
                return McpToolRegistry.ToolText("AssetDatabase refreshed.");
            });

        public static readonly McpTool GetConsoleLogs = new McpTool(
            "unity.get_console_logs",
            "Read recent Unity console log entries.",
            McpToolRegistry.ObjectSchema(
                "limit", McpToolRegistry.IntegerProperty("Maximum log entries to return.", 1, 200)),
            GetConsoleLogsImpl);

        public static readonly McpTool ExecuteMenuItem = new McpTool(
            "unity.execute_menu_item",
            "Execute a Unity editor menu item by path, for example File/Save or Assets/Refresh.",
            McpToolRegistry.ObjectSchema(
                "menuItem", McpToolRegistry.StringProperty("Unity editor menu item path."),
                new[] { "menuItem" }),
            ExecuteMenuItemImpl);

        private static string ProjectRoot => UnityMcpPathUtility.ProjectRoot;
        private static string PackageRoot => UnityMcpPathUtility.PackageRoot;

        private static Dictionary<string, object> FindAssetsImpl(Dictionary<string, object> args)
        {
            var filter = McpJson.AsString(args, "filter", string.Empty);
            var folder = McpJson.AsString(args, "folder");
            var limit = Math.Max(1, Math.Min(200, McpJson.AsInt(args, "limit", 50)));
            string[] searchFolders = null;

            if (!string.IsNullOrEmpty(folder))
            {
                searchFolders = new[] { UnityMcpPathUtility.ResolveAssetOrPackageRelativePath(folder) };
            }

            var guids = AssetDatabase.FindAssets(filter, searchFolders);
            var results = new List<object>();
            for (var i = 0; i < guids.Length && results.Count < limit; i++)
            {
                var path = AssetDatabase.GUIDToAssetPath(guids[i]);
                var type = AssetDatabase.GetMainAssetTypeAtPath(path);
                results.Add(McpJson.Object(
                    "guid", guids[i],
                    "path", path,
                    "type", type != null ? type.FullName : string.Empty));
            }

            return JsonText(McpJson.Object(
                "filter", filter,
                "count", results.Count,
                "truncated", guids.Length > results.Count,
                "assets", results));
        }

        private static Dictionary<string, object> ReadAssetTextImpl(Dictionary<string, object> args)
        {
            var relativePath = RequireString(args, "path");
            var maxBytes = Math.Max(1, Math.Min(1048576, McpJson.AsInt(args, "maxBytes", 65536)));
            var fullPath = UnityMcpPathUtility.ResolveReadableProjectPath(relativePath);

            if (!File.Exists(fullPath))
            {
                return McpToolRegistry.ToolText("File not found: " + relativePath, true);
            }

            var fileInfo = new FileInfo(fullPath);
            if (fileInfo.Length > maxBytes)
            {
                return McpToolRegistry.ToolText("File is " + fileInfo.Length + " bytes, which exceeds maxBytes " + maxBytes + ".", true);
            }

            var text = File.ReadAllText(fullPath, Encoding.UTF8);
                return JsonText(McpJson.Object(
                "path", UnityMcpPathUtility.NormalizeProjectRelativePath(relativePath),
                "bytes", Encoding.UTF8.GetByteCount(text),
                "text", text));
        }

        private static Dictionary<string, object> WriteAssetTextImpl(Dictionary<string, object> args)
        {
            var relativePath = RequireString(args, "path");
            var text = RequireString(args, "text");
            var createDirectories = McpJson.AsBool(args, "createDirectories", true);
            var fullPath = UnityMcpPathUtility.ResolveWritableProjectPath(relativePath, true);
            var directory = Path.GetDirectoryName(fullPath);

            if (!Directory.Exists(directory))
            {
                if (!createDirectories)
                {
                    return McpToolRegistry.ToolText("Parent directory does not exist: " + directory, true);
                }

                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(fullPath, text, new UTF8Encoding(false));
            AssetDatabase.Refresh();

            return JsonText(McpJson.Object(
                "path", UnityMcpPathUtility.NormalizeProjectRelativePath(relativePath),
                "bytes", Encoding.UTF8.GetByteCount(text),
                "refreshed", true));
        }

        private static Dictionary<string, object> GetConsoleLogsImpl(Dictionary<string, object> args)
        {
            var limit = Math.Max(1, Math.Min(200, McpJson.AsInt(args, "limit", 50)));
            var logs = UnityMcpConsoleLogReader.Read(limit);
            return JsonText(McpJson.Object(
                "count", logs.Count,
                "logs", logs));
        }

        private static Dictionary<string, object> ExecuteMenuItemImpl(Dictionary<string, object> args)
        {
            var menuItem = RequireString(args, "menuItem");
            var success = EditorApplication.ExecuteMenuItem(menuItem);
            return JsonText(McpJson.Object(
                "menuItem", menuItem,
                "executed", success));
        }

        private static Dictionary<string, object> JsonText(Dictionary<string, object> payload)
        {
            return McpToolRegistry.ToolText(McpJson.Stringify(payload));
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
    }
}
