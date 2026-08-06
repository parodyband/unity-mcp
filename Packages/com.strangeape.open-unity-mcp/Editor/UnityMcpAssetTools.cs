using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace StrangeApe.OpenUnityMcp
{
    internal static class UnityMcpAssetTools
    {
        public static readonly McpTool CreateFolder = new McpTool(
            "unity.create_folder",
            "Create a folder under Assets or Packages. Unavailable while the editor is in play mode.",
            McpToolRegistry.ObjectSchema(
                "path", McpToolRegistry.StringProperty("Project-relative folder path under Assets or Packages."),
                new[] { "path" }),
            CreateFolderImpl,
            blockedInPlayMode: true);

        public static readonly McpTool CreateAsset = new McpTool(
            "unity.create_asset",
            "Create a ScriptableObject asset under Assets or Packages. Unavailable while the editor is in play mode.",
            McpToolRegistry.ObjectSchema(
                "typeName", McpToolRegistry.StringProperty("ScriptableObject type name, such as MyNamespace.BoarHerdSettings or BoarHerdSettings."),
                "path", McpToolRegistry.StringProperty("Project-relative .asset path under Assets or Packages."),
                "createDirectories", McpToolRegistry.BooleanProperty("Create missing parent directories."),
                new[] { "typeName", "path" }),
            CreateScriptableObjectImpl,
            blockedInPlayMode: true);

        public static readonly McpTool CreateScriptableObject = new McpTool(
            "unity.create_scriptable_object",
            "Create a ScriptableObject asset under Assets or Packages. Unavailable while the editor is in play mode.",
            McpToolRegistry.ObjectSchema(
                "typeName", McpToolRegistry.StringProperty("ScriptableObject type name, such as MyNamespace.BoarHerdSettings or BoarHerdSettings."),
                "path", McpToolRegistry.StringProperty("Project-relative .asset path under Assets or Packages."),
                "createDirectories", McpToolRegistry.BooleanProperty("Create missing parent directories."),
                new[] { "typeName", "path" }),
            CreateScriptableObjectImpl,
            blockedInPlayMode: true);

        public static readonly McpTool CopyAsset = new McpTool(
            "unity.copy_asset",
            "Copy an asset or folder under Assets or Packages. Unavailable while the editor is in play mode.",
            McpToolRegistry.ObjectSchema(
                "sourcePath", McpToolRegistry.StringProperty("Existing project-relative asset or folder path under Assets or Packages."),
                "destinationPath", McpToolRegistry.StringProperty("New project-relative asset or folder path under Assets or Packages."),
                "createDirectories", McpToolRegistry.BooleanProperty("Create missing destination parent folders."),
                new[] { "sourcePath", "destinationPath" }),
            CopyAssetImpl,
            blockedInPlayMode: true);

        public static readonly McpTool MoveAsset = new McpTool(
            "unity.move_asset",
            "Move or rename an asset or folder under Assets or Packages. Unavailable while the editor is in play mode.",
            McpToolRegistry.ObjectSchema(
                "sourcePath", McpToolRegistry.StringProperty("Existing project-relative asset or folder path under Assets or Packages."),
                "destinationPath", McpToolRegistry.StringProperty("New project-relative asset or folder path under Assets or Packages."),
                "createDirectories", McpToolRegistry.BooleanProperty("Create missing destination parent folders."),
                new[] { "sourcePath", "destinationPath" }),
            MoveAssetImpl,
            blockedInPlayMode: true);

        public static readonly McpTool DeleteAsset = new McpTool(
            "unity.delete_asset",
            "Delete an asset or folder under Assets or Packages. Folder deletion requires recursive=true. Unavailable while the editor is in play mode.",
            McpToolRegistry.ObjectSchema(
                "path", McpToolRegistry.StringProperty("Project-relative asset or folder path under Assets or Packages."),
                "recursive", McpToolRegistry.BooleanProperty("Allow deleting a folder and its contents."),
                new[] { "path" }),
            DeleteAssetImpl,
            blockedInPlayMode: true);

        public static readonly McpTool GetAssetMetadata = new McpTool(
            "unity.get_asset_metadata",
            "Get GUID, type, labels, importer, and optional dependency information for an asset under Assets or Packages.",
            McpToolRegistry.ObjectSchema(
                "path", McpToolRegistry.StringProperty("Project-relative asset path under Assets or Packages."),
                "includeDependencies", McpToolRegistry.BooleanProperty("Include direct asset dependencies."),
                new[] { "path" }),
            GetAssetMetadataImpl);

        public static readonly McpTool ImportAsset = new McpTool(
            "unity.import_asset",
            "Import one asset path through AssetDatabase.ImportAsset. Unavailable while the editor is in play mode.",
            McpToolRegistry.ObjectSchema(
                "path", McpToolRegistry.StringProperty("Project-relative asset path under Assets or Packages."),
                "forceUpdate", McpToolRegistry.BooleanProperty("Force Unity to re-import the asset."),
                new[] { "path" }),
            ImportAssetImpl,
            blockedInPlayMode: true);

        private static Dictionary<string, object> CreateFolderImpl(Dictionary<string, object> args)
        {
            var path = UnityMcpPathUtility.ResolveAssetOrPackageRelativePath(RequireString(args, "path"));
            if (AssetExists(path))
            {
                return JsonText(McpJson.Object(
                    "path", path,
                    "created", false,
                    "alreadyExists", true,
                    "guid", AssetDatabase.AssetPathToGUID(path)));
            }

            EnsureNotProtectedRoot(path);
            EnsureFolderPath(path);
            EnsureParentFolders(Path.GetDirectoryName(path)?.Replace('\\', '/'));
            var parent = Path.GetDirectoryName(path)?.Replace('\\', '/');
            var folderName = Path.GetFileName(path);
            var guid = AssetDatabase.CreateFolder(parent, folderName);
            AssetDatabase.Refresh();

            return JsonText(McpJson.Object(
                "path", path,
                "created", !string.IsNullOrEmpty(guid),
                "guid", guid));
        }

        private static Dictionary<string, object> CreateScriptableObjectImpl(Dictionary<string, object> args)
        {
            var type = UnityMcpObjectUtility.ResolveType(RequireString(args, "typeName"), typeof(ScriptableObject));
            var path = UnityMcpPathUtility.ResolveAssetOrPackageRelativePath(RequireString(args, "path"));
            if (!path.EndsWith(".asset", StringComparison.OrdinalIgnoreCase))
            {
                return McpToolRegistry.ToolText("ScriptableObject asset path must end with .asset.", true);
            }

            if (AssetExists(path))
            {
                return McpToolRegistry.ToolText("Asset already exists: " + path, true);
            }

            EnsureNotProtectedRoot(path);
            var parentPath = Path.GetDirectoryName(path)?.Replace('\\', '/');
            if (!string.IsNullOrEmpty(parentPath) && !AssetDatabase.IsValidFolder(parentPath) && !McpJson.AsBool(args, "createDirectories", true))
            {
                return McpToolRegistry.ToolText("Parent directory does not exist: " + parentPath, true);
            }

            if (McpJson.AsBool(args, "createDirectories", true))
            {
                EnsureParentFolders(parentPath);
            }

            var asset = ScriptableObject.CreateInstance(type);
            AssetDatabase.CreateAsset(asset, path);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            return JsonText(McpJson.Object(
                "path", path,
                "created", true,
                "asset", UnityMcpEditorTools.DescribeObject(asset)));
        }

        private static Dictionary<string, object> CopyAssetImpl(Dictionary<string, object> args)
        {
            var sourcePath = UnityMcpPathUtility.ResolveAssetOrPackageRelativePath(RequireString(args, "sourcePath"));
            var destinationPath = UnityMcpPathUtility.ResolveAssetOrPackageRelativePath(RequireString(args, "destinationPath"));
            if (!AssetExists(sourcePath))
            {
                return McpToolRegistry.ToolText("Source asset path not found: " + sourcePath, true);
            }

            EnsureNotProtectedRoot(sourcePath);
            EnsureNotProtectedRoot(destinationPath);
            if (AssetExists(destinationPath))
            {
                return McpToolRegistry.ToolText("Destination already exists: " + destinationPath, true);
            }

            if (McpJson.AsBool(args, "createDirectories", true))
            {
                EnsureParentFolders(Path.GetDirectoryName(destinationPath)?.Replace('\\', '/'));
            }

            var copied = AssetDatabase.CopyAsset(sourcePath, destinationPath);
            AssetDatabase.Refresh();
            return JsonText(McpJson.Object(
                "sourcePath", sourcePath,
                "destinationPath", destinationPath,
                "copied", copied,
                "guid", AssetDatabase.AssetPathToGUID(destinationPath)));
        }

        private static Dictionary<string, object> MoveAssetImpl(Dictionary<string, object> args)
        {
            var sourcePath = UnityMcpPathUtility.ResolveAssetOrPackageRelativePath(RequireString(args, "sourcePath"));
            var destinationPath = UnityMcpPathUtility.ResolveAssetOrPackageRelativePath(RequireString(args, "destinationPath"));
            if (!AssetExists(sourcePath))
            {
                return McpToolRegistry.ToolText("Source asset path not found: " + sourcePath, true);
            }

            EnsureNotProtectedRoot(sourcePath);
            EnsureNotProtectedRoot(destinationPath);
            if (AssetExists(destinationPath))
            {
                return McpToolRegistry.ToolText("Destination already exists: " + destinationPath, true);
            }

            if (McpJson.AsBool(args, "createDirectories", true))
            {
                EnsureParentFolders(Path.GetDirectoryName(destinationPath)?.Replace('\\', '/'));
            }

            var error = AssetDatabase.MoveAsset(sourcePath, destinationPath);
            AssetDatabase.Refresh();
            if (!string.IsNullOrEmpty(error))
            {
                return McpToolRegistry.ToolText(error, true);
            }

            return JsonText(McpJson.Object(
                "sourcePath", sourcePath,
                "destinationPath", destinationPath,
                "moved", true,
                "guid", AssetDatabase.AssetPathToGUID(destinationPath)));
        }

        private static Dictionary<string, object> DeleteAssetImpl(Dictionary<string, object> args)
        {
            var path = UnityMcpPathUtility.ResolveAssetOrPackageRelativePath(RequireString(args, "path"));
            if (!AssetExists(path))
            {
                return McpToolRegistry.ToolText("Asset path not found: " + path, true);
            }

            EnsureNotProtectedRoot(path);
            if (AssetDatabase.IsValidFolder(path) && !McpJson.AsBool(args, "recursive", false))
            {
                return McpToolRegistry.ToolText("Folder deletion requires recursive=true: " + path, true);
            }

            var deleted = AssetDatabase.DeleteAsset(path);
            AssetDatabase.Refresh();
            return JsonText(McpJson.Object(
                "path", path,
                "deleted", deleted));
        }

        private static Dictionary<string, object> GetAssetMetadataImpl(Dictionary<string, object> args)
        {
            var path = UnityMcpPathUtility.ResolveAssetOrPackageRelativePath(RequireString(args, "path"));
            var includeDependencies = McpJson.AsBool(args, "includeDependencies", false);
            var fullPath = Path.Combine(UnityMcpPathUtility.ProjectRoot, path);

            if (!File.Exists(fullPath) && !Directory.Exists(fullPath))
            {
                return McpToolRegistry.ToolText("Asset path not found: " + path, true);
            }

            var importer = AssetImporter.GetAtPath(path);
            var asset = AssetDatabase.LoadMainAssetAtPath(path);
            var dependencies = new List<object>();
            if (includeDependencies)
            {
                foreach (var dependency in AssetDatabase.GetDependencies(path, false))
                {
                    dependencies.Add(dependency);
                }
            }

            return JsonText(McpJson.Object(
                "path", path,
                "guid", AssetDatabase.AssetPathToGUID(path),
                "type", asset != null ? asset.GetType().FullName : string.Empty,
                "labels", asset != null ? new List<object>(AssetDatabase.GetLabels(asset)) : new List<object>(),
                "importer", importer != null ? importer.GetType().FullName : string.Empty,
                "assetBundleName", importer != null ? importer.assetBundleName : string.Empty,
                "isFolder", AssetDatabase.IsValidFolder(path),
                "dependencies", dependencies));
        }

        private static Dictionary<string, object> ImportAssetImpl(Dictionary<string, object> args)
        {
            var path = UnityMcpPathUtility.ResolveAssetOrPackageRelativePath(RequireString(args, "path"));
            var options = McpJson.AsBool(args, "forceUpdate", false)
                ? ImportAssetOptions.ForceUpdate
                : ImportAssetOptions.Default;

            AssetDatabase.ImportAsset(path, options);
            return JsonText(McpJson.Object(
                "path", path,
                "imported", true,
                "forceUpdate", McpJson.AsBool(args, "forceUpdate", false)));
        }

        private static bool AssetExists(string path)
        {
            var fullPath = Path.Combine(UnityMcpPathUtility.ProjectRoot, path);
            return File.Exists(fullPath) || Directory.Exists(fullPath);
        }

        private static void EnsureFolderPath(string path)
        {
            if (string.IsNullOrEmpty(Path.GetFileName(path)))
            {
                throw new ArgumentException("Folder path must include a folder name.");
            }
        }

        private static void EnsureParentFolders(string parentPath)
        {
            if (string.IsNullOrEmpty(parentPath))
            {
                return;
            }

            parentPath = UnityMcpPathUtility.ResolveAssetOrPackageRelativePath(parentPath);
            if (AssetDatabase.IsValidFolder(parentPath))
            {
                return;
            }

            var parts = parentPath.Split('/');
            if (parts.Length == 0)
            {
                return;
            }

            var current = parts[0];
            if (!AssetDatabase.IsValidFolder(current))
            {
                throw new ArgumentException("Parent root folder does not exist: " + current);
            }

            for (var i = 1; i < parts.Length; i++)
            {
                var next = current + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next))
                {
                    AssetDatabase.CreateFolder(current, parts[i]);
                }

                current = next;
            }
        }

        private static void EnsureNotProtectedRoot(string path)
        {
            path = UnityMcpPathUtility.NormalizeProjectRelativePath(path);
            if (string.Equals(path, "Assets", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(path, "Packages", StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException("Refusing to modify protected root folder: " + path);
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

        private static Dictionary<string, object> JsonText(Dictionary<string, object> payload)
        {
            return McpToolRegistry.ToolText(McpJson.Stringify(payload));
        }
    }
}
