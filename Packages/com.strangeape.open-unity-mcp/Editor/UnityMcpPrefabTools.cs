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

        private static Dictionary<string, object> GetPrefabInfoImpl(Dictionary<string, object> args)
        {
            var gameObject = UnityMcpObjectUtility.ResolveGameObject(McpJson.AsString(args, "objectId"), McpJson.AsString(args, "path"));
            if (gameObject == null)
            {
                return McpToolRegistry.ToolText("Target is not a GameObject, Component, or prefab asset.", true);
            }

            return JsonText(DescribePrefab(gameObject));
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

        private static Dictionary<string, object> DescribePrefab(GameObject gameObject)
        {
            var assetPath = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(gameObject);
            if (string.IsNullOrEmpty(assetPath))
            {
                assetPath = AssetDatabase.GetAssetPath(gameObject);
            }

            return McpJson.Object(
                "target", UnityMcpEditorTools.DescribeObject(gameObject),
                "assetPath", assetPath,
                "assetType", PrefabUtility.GetPrefabAssetType(gameObject).ToString(),
                "instanceStatus", PrefabUtility.GetPrefabInstanceStatus(gameObject).ToString(),
                "isAnyPrefabInstanceRoot", PrefabUtility.IsAnyPrefabInstanceRoot(gameObject),
                "isPartOfPrefabAsset", PrefabUtility.IsPartOfPrefabAsset(gameObject),
                "isPartOfPrefabInstance", PrefabUtility.IsPartOfPrefabInstance(gameObject));
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
