using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace StrangeApe.OpenUnityMcp
{
    internal static class UnityMcpPrefabTools
    {
        public static readonly McpTool GetPrefabInfo = new McpTool(
            "unity.get_prefab_info",
            "Inspect prefab status for a GameObject, Component, or prefab asset.",
            McpToolRegistry.ObjectSchema(
                "objectId", McpToolRegistry.StringProperty("Target GameObject or Component objectId from another tool result."),
                "path", McpToolRegistry.StringProperty("Optional prefab asset path under Assets or Packages.")),
            GetPrefabInfoImpl);

        public static readonly McpTool FindChild = new McpTool(
            "unity.find_child",
            "Find child GameObjects below a scene object or prefab asset by child path or name.",
            McpToolRegistry.ObjectSchema(
                "objectId", McpToolRegistry.StringProperty("Root GameObject or Component objectId from another tool result."),
                "path", McpToolRegistry.StringProperty("Optional root prefab asset path under Assets or Packages."),
                "childPath", McpToolRegistry.StringProperty("Slash-separated child Transform path below the root. The root name may be included or omitted."),
                "childName", McpToolRegistry.StringProperty("Child Transform name to find recursively."),
                "name", McpToolRegistry.StringProperty("Alias for childName."),
                "includeInactive", McpToolRegistry.BooleanProperty("Include inactive children."),
                "limit", McpToolRegistry.IntegerProperty("Maximum matches to return for name searches.", 1, 100)),
            FindChildImpl);

        public static readonly McpTool InstantiatePrefab = new McpTool(
            "unity.instantiate_prefab",
            "Instantiate a prefab asset into the active scene.",
            McpToolRegistry.ObjectSchema(
                "prefabPath", McpToolRegistry.StringProperty("Project-relative prefab asset path under Assets or Packages."),
                "parentObjectId", McpToolRegistry.StringProperty("Optional parent GameObject or Transform objectId."),
                "name", McpToolRegistry.StringProperty("Optional instance name."),
                "select", McpToolRegistry.BooleanProperty("Select the created prefab instance."),
                new[] { "prefabPath" }),
            InstantiatePrefabImpl);

        public static readonly McpTool SaveAsPrefabAsset = new McpTool(
            "unity.save_as_prefab_asset",
            "Save a scene GameObject as a prefab asset.",
            McpToolRegistry.ObjectSchema(
                "objectId", McpToolRegistry.StringProperty("Scene GameObject or Component objectId."),
                "path", McpToolRegistry.StringProperty("Destination prefab path under Assets or Packages."),
                "connect", McpToolRegistry.BooleanProperty("Connect the scene object to the new prefab asset."),
                "createDirectories", McpToolRegistry.BooleanProperty("Create missing parent directories."),
                new[] { "objectId", "path" }),
            SaveAsPrefabAssetImpl);

        public static readonly McpTool SavePrefabAsset = new McpTool(
            "unity.save_prefab_asset",
            "Save a prefab asset after direct asset-object modifications.",
            McpToolRegistry.ObjectSchema(
                "path", McpToolRegistry.StringProperty("Prefab asset path under Assets or Packages."),
                "objectId", McpToolRegistry.StringProperty("Prefab asset GameObject or Component objectId.")),
            SavePrefabAssetImpl);

        public static readonly McpTool ApplyPrefabChanges = new McpTool(
            "unity.apply_prefab_changes",
            "Apply overrides from a scene prefab instance back to its prefab asset.",
            McpToolRegistry.ObjectSchema(
                "objectId", McpToolRegistry.StringProperty("Scene prefab instance GameObject or Component objectId."),
                new[] { "objectId" }),
            ApplyPrefabChangesImpl);

        private static Dictionary<string, object> GetPrefabInfoImpl(Dictionary<string, object> args)
        {
            var path = McpJson.AsString(args, "path");
            var gameObject = UnityMcpObjectUtility.ResolveGameObject(McpJson.AsString(args, "objectId"), path);
            if (gameObject == null)
            {
                if (!string.IsNullOrEmpty(path))
                {
                    return DescribeNonPrefabAsset(path);
                }

                return McpToolRegistry.ToolText("Target is not a GameObject, Component, or prefab asset.", true);
            }

            return JsonText(DescribePrefab(gameObject));
        }

        private static Dictionary<string, object> FindChildImpl(Dictionary<string, object> args)
        {
            var root = UnityMcpObjectUtility.ResolveGameObject(McpJson.AsString(args, "objectId"), McpJson.AsString(args, "path"));
            if (root == null)
            {
                return McpToolRegistry.ToolText("Root is not a GameObject, Component, or prefab asset.", true);
            }

            var childPath = McpJson.AsString(args, "childPath");
            var childName = McpJson.AsString(args, "childName", McpJson.AsString(args, "name"));
            if (string.IsNullOrEmpty(childPath) && string.IsNullOrEmpty(childName))
            {
                return McpToolRegistry.ToolText("Provide childPath, childName, or name.", true);
            }

            var includeInactive = McpJson.AsBool(args, "includeInactive", true);
            var limit = Math.Max(1, Math.Min(100, McpJson.AsInt(args, "limit", 20)));
            var transforms = UnityMcpObjectUtility.FindChildren(root.transform, childPath, childName, includeInactive, limit);
            var matches = new List<object>();
            foreach (var transform in transforms)
            {
                matches.Add(UnityMcpSceneTools.DescribeGameObject(transform.gameObject));
            }

            return JsonText(McpJson.Object(
                "root", UnityMcpSceneTools.DescribeGameObject(root),
                "childPath", childPath,
                "childName", childName,
                "count", matches.Count,
                "truncated", transforms.Count >= limit && string.IsNullOrEmpty(childPath),
                "child", matches.Count > 0 ? matches[0] : McpJson.Object(),
                "matches", matches));
        }

        private static Dictionary<string, object> InstantiatePrefabImpl(Dictionary<string, object> args)
        {
            var prefabPath = UnityMcpPathUtility.ResolveAssetOrPackageRelativePath(RequireString(args, "prefabPath"));
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (prefab == null)
            {
                return McpToolRegistry.ToolText("Prefab asset not found or not a GameObject: " + prefabPath, true);
            }

            var instance = PrefabUtility.InstantiatePrefab(prefab) as GameObject;
            if (instance == null)
            {
                return McpToolRegistry.ToolText("Unity failed to instantiate prefab: " + prefabPath, true);
            }

            Undo.RegisterCreatedObjectUndo(instance, "Instantiate MCP Prefab");

            var parentObjectId = McpJson.AsString(args, "parentObjectId");
            if (!string.IsNullOrEmpty(parentObjectId))
            {
                var parent = UnityMcpObjectUtility.ResolveObjectById(parentObjectId);
                var parentTransform = ResolveTransform(parent);
                if (parentTransform == null)
                {
                    UnityEngine.Object.DestroyImmediate(instance);
                    return McpToolRegistry.ToolText("Parent objectId is not a GameObject, Component, or Transform.", true);
                }

                Undo.SetTransformParent(instance.transform, parentTransform, "Parent MCP Prefab");
            }

            var name = McpJson.AsString(args, "name");
            if (!string.IsNullOrEmpty(name))
            {
                instance.name = name;
            }

            if (McpJson.AsBool(args, "select", true))
            {
                Selection.activeObject = instance;
            }

            return JsonText(McpJson.Object(
                "instance", UnityMcpEditorTools.DescribeObject(instance),
                "prefab", DescribePrefab(instance)));
        }

        private static Dictionary<string, object> SaveAsPrefabAssetImpl(Dictionary<string, object> args)
        {
            var source = UnityMcpObjectUtility.ResolveGameObject(McpJson.AsString(args, "objectId"), null);
            if (source == null)
            {
                return McpToolRegistry.ToolText("Source is not a GameObject or Component.", true);
            }

            var relativePath = UnityMcpPathUtility.ResolveAssetOrPackageRelativePath(RequireString(args, "path"));
            if (!relativePath.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase))
            {
                return McpToolRegistry.ToolText("Prefab destination must end with .prefab.", true);
            }

            var fullPath = Path.Combine(UnityMcpPathUtility.ProjectRoot, relativePath);
            var directory = Path.GetDirectoryName(fullPath);
            if (!Directory.Exists(directory))
            {
                if (!McpJson.AsBool(args, "createDirectories", true))
                {
                    return McpToolRegistry.ToolText("Parent directory does not exist: " + directory, true);
                }

                Directory.CreateDirectory(directory);
                AssetDatabase.Refresh();
            }

            bool success;
            GameObject prefab;
            if (McpJson.AsBool(args, "connect", false))
            {
                prefab = PrefabUtility.SaveAsPrefabAssetAndConnect(source, relativePath, InteractionMode.AutomatedAction, out success);
            }
            else
            {
                prefab = PrefabUtility.SaveAsPrefabAsset(source, relativePath, out success);
            }

            AssetDatabase.Refresh();
            return JsonText(McpJson.Object(
                "path", relativePath,
                "saved", success,
                "prefab", prefab != null ? UnityMcpEditorTools.DescribeObject(prefab) : McpJson.Object()));
        }

        private static Dictionary<string, object> SavePrefabAssetImpl(Dictionary<string, object> args)
        {
            var prefab = ResolvePrefabAsset(args, out var prefabPath, out var error);
            if (prefab == null)
            {
                return McpToolRegistry.ToolText(error, true);
            }

            bool saved;
            var savedPrefab = PrefabUtility.SavePrefabAsset(prefab, out saved);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            return JsonText(McpJson.Object(
                "path", prefabPath,
                "saved", saved,
                "prefab", savedPrefab != null ? DescribePrefab(savedPrefab) : DescribePrefab(prefab)));
        }

        private static Dictionary<string, object> ApplyPrefabChangesImpl(Dictionary<string, object> args)
        {
            var obj = UnityMcpObjectUtility.ResolveObjectById(RequireString(args, "objectId"));
            var gameObject = ToGameObject(obj);
            if (gameObject == null)
            {
                return McpToolRegistry.ToolText("objectId is not a GameObject or Component.", true);
            }

            if (!PrefabUtility.IsPartOfPrefabInstance(gameObject))
            {
                return McpToolRegistry.ToolText("Target is not part of a scene prefab instance.", true);
            }

            var instanceRoot = PrefabUtility.GetNearestPrefabInstanceRoot(gameObject);
            if (instanceRoot == null)
            {
                return McpToolRegistry.ToolText("Prefab instance root could not be resolved.", true);
            }

            var prefabPath = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(instanceRoot);
            PrefabUtility.ApplyPrefabInstance(instanceRoot, InteractionMode.AutomatedAction);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            return JsonText(McpJson.Object(
                "applied", true,
                "instanceRoot", UnityMcpSceneTools.DescribeGameObject(instanceRoot),
                "prefabPath", prefabPath));
        }

        private static Dictionary<string, object> DescribePrefab(GameObject gameObject)
        {
            var assetPath = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(gameObject);
            if (string.IsNullOrEmpty(assetPath))
            {
                assetPath = AssetDatabase.GetAssetPath(gameObject);
            }

            var root = ResolvePrefabRoot(gameObject);
            var rootComponents = new List<object>();
            if (root != null)
            {
                foreach (var component in root.GetComponents<Component>())
                {
                    rootComponents.Add(UnityMcpComponentTools.DescribeComponent(component));
                }
            }

            return McpJson.Object(
                "target", UnityMcpEditorTools.DescribeObject(gameObject),
                "root", root != null ? UnityMcpSceneTools.DescribeGameObject(root) : McpJson.Object(),
                "rootComponents", rootComponents,
                "rootChildCount", root != null ? root.transform.childCount : 0,
                "assetPath", assetPath,
                "assetType", PrefabUtility.GetPrefabAssetType(gameObject).ToString(),
                "instanceStatus", PrefabUtility.GetPrefabInstanceStatus(gameObject).ToString(),
                "isPrefab", PrefabUtility.GetPrefabAssetType(gameObject) != PrefabAssetType.NotAPrefab || !string.IsNullOrEmpty(assetPath),
                "isAnyPrefabInstanceRoot", PrefabUtility.IsAnyPrefabInstanceRoot(gameObject),
                "isPartOfPrefabAsset", PrefabUtility.IsPartOfPrefabAsset(gameObject),
                "isPartOfPrefabInstance", PrefabUtility.IsPartOfPrefabInstance(gameObject));
        }

        private static Dictionary<string, object> DescribeNonPrefabAsset(string path)
        {
            var relativePath = UnityMcpPathUtility.ResolveAssetOrPackageRelativePath(path);
            var asset = AssetDatabase.LoadMainAssetAtPath(relativePath);
            if (asset == null)
            {
                return McpToolRegistry.ToolText("Asset not found: " + relativePath, true);
            }

            return JsonText(McpJson.Object(
                "target", UnityMcpEditorTools.DescribeObject(asset),
                "assetPath", relativePath,
                "assetObjectType", asset.GetType().FullName,
                "assetType", PrefabAssetType.NotAPrefab.ToString(),
                "instanceStatus", PrefabInstanceStatus.NotAPrefab.ToString(),
                "isPrefab", false,
                "isPartOfPrefabAsset", false,
                "isPartOfPrefabInstance", false,
                "root", McpJson.Object(),
                "rootComponents", new List<object>(),
                "rootChildCount", 0));
        }

        private static GameObject ResolvePrefabRoot(GameObject gameObject)
        {
            if (gameObject == null)
            {
                return null;
            }

            var assetPath = AssetDatabase.GetAssetPath(gameObject);
            if (!string.IsNullOrEmpty(assetPath))
            {
                var assetRoot = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
                if (assetRoot != null)
                {
                    return assetRoot;
                }
            }

            var instanceRoot = PrefabUtility.GetNearestPrefabInstanceRoot(gameObject);
            return instanceRoot != null ? instanceRoot : gameObject;
        }

        private static GameObject ResolvePrefabAsset(Dictionary<string, object> args, out string prefabPath, out string error)
        {
            prefabPath = McpJson.AsString(args, "path");
            error = null;

            if (!string.IsNullOrEmpty(prefabPath))
            {
                prefabPath = UnityMcpPathUtility.ResolveAssetOrPackageRelativePath(prefabPath);
            }
            else
            {
                var objectId = McpJson.AsString(args, "objectId");
                if (string.IsNullOrEmpty(objectId))
                {
                    error = "Provide path or objectId.";
                    return null;
                }

                var obj = UnityMcpObjectUtility.ResolveObjectById(objectId);
                var gameObject = ToGameObject(obj);
                if (gameObject == null)
                {
                    error = "objectId is not a GameObject or Component.";
                    return null;
                }

                prefabPath = AssetDatabase.GetAssetPath(obj);
                if (string.IsNullOrEmpty(prefabPath))
                {
                    prefabPath = AssetDatabase.GetAssetPath(gameObject);
                }

                if (string.IsNullOrEmpty(prefabPath) && PrefabUtility.IsPartOfPrefabInstance(gameObject))
                {
                    error = "objectId is a scene prefab instance. Use unity.apply_prefab_changes to persist instance overrides.";
                    return null;
                }
            }

            if (string.IsNullOrEmpty(prefabPath) || !prefabPath.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase))
            {
                error = "Prefab path must end with .prefab.";
                return null;
            }

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (prefab == null)
            {
                error = "Prefab asset not found or not a GameObject: " + prefabPath;
                return null;
            }

            return prefab;
        }

        private static GameObject ToGameObject(UnityEngine.Object obj)
        {
            if (obj is GameObject gameObject)
            {
                return gameObject;
            }

            if (obj is Component component)
            {
                return component.gameObject;
            }

            return null;
        }

        private static Transform ResolveTransform(UnityEngine.Object obj)
        {
            if (obj is Transform transform)
            {
                return transform;
            }

            if (obj is GameObject gameObject)
            {
                return gameObject.transform;
            }

            if (obj is Component component)
            {
                return component.transform;
            }

            return null;
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
