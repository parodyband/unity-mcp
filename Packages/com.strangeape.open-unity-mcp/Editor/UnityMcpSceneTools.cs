using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace StrangeApe.OpenUnityMcp
{
    internal static class UnityMcpSceneTools
    {
        public static readonly McpTool GetOpenScenes = new McpTool(
            "unity.get_open_scenes",
            "List open Unity scenes with active, loaded, dirty, and root-count state.",
            McpToolRegistry.ObjectSchema(),
            _ => JsonText(McpJson.Object(
                "activeScene", SceneManager.GetActiveScene().path,
                "scenes", GetOpenScenesArray())));

        public static readonly McpTool GetHierarchy = new McpTool(
            "unity.get_hierarchy",
            "Read the open scene hierarchy with bounded depth and result count.",
            McpToolRegistry.ObjectSchema(
                "scenePath", McpToolRegistry.StringProperty("Optional open scene path. Defaults to all open scenes."),
                "maxDepth", McpToolRegistry.IntegerProperty("Maximum transform depth to include.", 0, 16),
                "limit", McpToolRegistry.IntegerProperty("Maximum GameObjects to include.", 1, 1000),
                "includeInactive", McpToolRegistry.BooleanProperty("Include inactive GameObjects.")),
            GetHierarchyImpl);

        public static readonly McpTool OpenScene = new McpTool(
            "unity.open_scene",
            "Open a Unity scene asset. Single-scene opens refuse to discard dirty scenes unless explicitly told to save or discard.",
            McpToolRegistry.ObjectSchema(
                "path", McpToolRegistry.StringProperty("Project-relative .unity scene path under Assets or Packages."),
                "mode", McpToolRegistry.StringProperty("Open mode: Single or Additive. Defaults to Single."),
                "setActive", McpToolRegistry.BooleanProperty("Set the opened scene as the active scene."),
                "saveDirtyScenes", McpToolRegistry.BooleanProperty("Save dirty open scenes before a Single open."),
                "discardUnsavedChanges", McpToolRegistry.BooleanProperty("Allow a Single open to discard dirty open scene changes."),
                new[] { "path" }),
            OpenSceneImpl);

        public static readonly McpTool SaveScene = new McpTool(
            "unity.save_scene",
            "Save an open Unity scene, optionally to a new .unity asset path.",
            McpToolRegistry.ObjectSchema(
                "scenePath", McpToolRegistry.StringProperty("Optional currently open scene path. Defaults to the active scene."),
                "saveAsPath", McpToolRegistry.StringProperty("Optional destination .unity path under Assets or Packages."),
                "createDirectories", McpToolRegistry.BooleanProperty("Create missing destination directories when saveAsPath is set.")),
            SaveSceneImpl);

        public static readonly McpTool SaveAllScenes = new McpTool(
            "unity.save_all_scenes",
            "Save all open Unity scenes that already have scene asset paths.",
            McpToolRegistry.ObjectSchema(),
            _ => SaveAllScenesImpl());

        public static readonly McpTool CloseScene = new McpTool(
            "unity.close_scene",
            "Close an open Unity scene. Dirty scenes are protected unless explicitly saved or discarded.",
            McpToolRegistry.ObjectSchema(
                "scenePath", McpToolRegistry.StringProperty("Currently open scene path."),
                "saveDirtyScene", McpToolRegistry.BooleanProperty("Save the scene before closing when it is dirty."),
                "discardUnsavedChanges", McpToolRegistry.BooleanProperty("Close the scene even if it has unsaved changes."),
                new[] { "scenePath" }),
            CloseSceneImpl);

        public static readonly McpTool SelectObject = new McpTool(
            "unity.select_object",
            "Select an object by Unity objectId or by asset path.",
            McpToolRegistry.ObjectSchema(
                "objectId", McpToolRegistry.StringProperty("Unity objectId from another tool result."),
                "path", McpToolRegistry.StringProperty("Project-relative asset path under Assets or Packages."),
                "ping", McpToolRegistry.BooleanProperty("Ping the selected object in the editor.")),
            SelectObjectImpl);

        public static readonly McpTool CreateGameObject = new McpTool(
            "unity.create_game_object",
            "Create an empty GameObject or primitive in the active scene.",
            McpToolRegistry.ObjectSchema(
                "name", McpToolRegistry.StringProperty("New GameObject name."),
                "primitiveType", McpToolRegistry.StringProperty("Optional Unity primitive type: Cube, Sphere, Capsule, Cylinder, Plane, Quad."),
                "parentObjectId", McpToolRegistry.StringProperty("Optional parent GameObject or Transform objectId."),
                "select", McpToolRegistry.BooleanProperty("Select the created GameObject.")),
            CreateGameObjectImpl);

        private static List<object> GetOpenScenesArray()
        {
            var scenes = new List<object>();
            for (var i = 0; i < SceneManager.sceneCount; i++)
            {
                scenes.Add(DescribeScene(SceneManager.GetSceneAt(i)));
            }

            return scenes;
        }

        private static Dictionary<string, object> GetHierarchyImpl(Dictionary<string, object> args)
        {
            var scenePath = McpJson.AsString(args, "scenePath");
            var maxDepth = Math.Max(0, Math.Min(16, McpJson.AsInt(args, "maxDepth", 4)));
            var limit = Math.Max(1, Math.Min(1000, McpJson.AsInt(args, "limit", 200)));
            var includeInactive = McpJson.AsBool(args, "includeInactive", true);
            var remaining = limit;
            var scenes = new List<object>();

            for (var i = 0; i < SceneManager.sceneCount; i++)
            {
                var scene = SceneManager.GetSceneAt(i);
                if (!string.IsNullOrEmpty(scenePath) && !string.Equals(scene.path, scenePath, StringComparison.Ordinal))
                {
                    continue;
                }

                var roots = new List<object>();
                foreach (var root in scene.GetRootGameObjects())
                {
                    if (remaining <= 0)
                    {
                        break;
                    }

                    if (!includeInactive && !root.activeInHierarchy)
                    {
                        continue;
                    }

                    roots.Add(DescribeTransform(root.transform, 0, maxDepth, includeInactive, ref remaining));
                }

                scenes.Add(McpJson.Object(
                    "scene", DescribeScene(scene),
                    "roots", roots));
            }

            return JsonText(McpJson.Object(
                "maxDepth", maxDepth,
                "limit", limit,
                "truncated", remaining <= 0,
                "scenes", scenes));
        }

        private static Dictionary<string, object> OpenSceneImpl(Dictionary<string, object> args)
        {
            var path = ResolveSceneAssetPath(RequireString(args, "path"), true);
            var mode = ParseOpenSceneMode(McpJson.AsString(args, "mode", "Single"));
            if (mode == OpenSceneMode.Single && !TryPrepareForSingleSceneChange(args, out var error))
            {
                return McpToolRegistry.ToolText(error, true);
            }

            var scene = EditorSceneManager.OpenScene(path, mode);
            if (!scene.IsValid())
            {
                return McpToolRegistry.ToolText("Unity failed to open scene: " + path, true);
            }

            if (McpJson.AsBool(args, "setActive", mode == OpenSceneMode.Single))
            {
                SceneManager.SetActiveScene(scene);
            }

            return JsonText(McpJson.Object(
                "opened", DescribeScene(scene),
                "mode", mode.ToString(),
                "openScenes", GetOpenScenesArray()));
        }

        private static Dictionary<string, object> SaveSceneImpl(Dictionary<string, object> args)
        {
            var scene = ResolveOpenScene(McpJson.AsString(args, "scenePath"));
            if (!scene.IsValid())
            {
                return McpToolRegistry.ToolText("Open scene not found.", true);
            }

            var saveAsPath = McpJson.AsString(args, "saveAsPath");
            bool saved;
            if (!string.IsNullOrEmpty(saveAsPath))
            {
                saveAsPath = ResolveSceneAssetPath(saveAsPath, false);
                var directory = Path.GetDirectoryName(Path.Combine(UnityMcpPathUtility.ProjectRoot, saveAsPath));
                if (!Directory.Exists(directory))
                {
                    if (!McpJson.AsBool(args, "createDirectories", true))
                    {
                        return McpToolRegistry.ToolText("Parent directory does not exist: " + directory, true);
                    }

                    Directory.CreateDirectory(directory);
                }

                saved = EditorSceneManager.SaveScene(scene, saveAsPath);
            }
            else
            {
                if (string.IsNullOrEmpty(scene.path))
                {
                    return McpToolRegistry.ToolText("Scene has no asset path. Provide saveAsPath.", true);
                }

                saved = EditorSceneManager.SaveScene(scene);
            }

            AssetDatabase.Refresh();
            return JsonText(McpJson.Object(
                "saved", saved,
                "scene", DescribeScene(scene)));
        }

        private static Dictionary<string, object> SaveAllScenesImpl()
        {
            foreach (var scene in EnumerateOpenScenes())
            {
                if (scene.isDirty && string.IsNullOrEmpty(scene.path))
                {
                    return McpToolRegistry.ToolText("Cannot save all scenes because an unsaved dirty scene has no asset path.", true);
                }
            }

            var saved = EditorSceneManager.SaveOpenScenes();
            AssetDatabase.Refresh();
            return JsonText(McpJson.Object(
                "saved", saved,
                "scenes", GetOpenScenesArray()));
        }

        private static Dictionary<string, object> CloseSceneImpl(Dictionary<string, object> args)
        {
            var scene = ResolveOpenScene(RequireString(args, "scenePath"));
            if (!scene.IsValid())
            {
                return McpToolRegistry.ToolText("Open scene not found.", true);
            }

            if (scene.isDirty)
            {
                if (McpJson.AsBool(args, "saveDirtyScene", false))
                {
                    if (string.IsNullOrEmpty(scene.path))
                    {
                        return McpToolRegistry.ToolText("Cannot save dirty scene because it has no asset path.", true);
                    }

                    if (!EditorSceneManager.SaveScene(scene))
                    {
                        return McpToolRegistry.ToolText("Unity failed to save scene before closing: " + scene.path, true);
                    }
                }
                else if (!McpJson.AsBool(args, "discardUnsavedChanges", false))
                {
                    return McpToolRegistry.ToolText("Scene has unsaved changes. Set saveDirtyScene or discardUnsavedChanges.", true);
                }
            }

            var scenePath = scene.path;
            var closed = EditorSceneManager.CloseScene(scene, true);
            return JsonText(McpJson.Object(
                "closed", closed,
                "scenePath", scenePath,
                "openScenes", GetOpenScenesArray()));
        }

        private static Dictionary<string, object> SelectObjectImpl(Dictionary<string, object> args)
        {
            UnityEngine.Object selected = null;
            var objectId = McpJson.AsString(args, "objectId");
            var path = McpJson.AsString(args, "path");

            if (!string.IsNullOrEmpty(objectId))
            {
                selected = UnityMcpObjectUtility.ResolveObjectById(objectId);
            }
            else if (!string.IsNullOrEmpty(path))
            {
                path = UnityMcpPathUtility.ResolveAssetOrPackageRelativePath(path);
                selected = AssetDatabase.LoadMainAssetAtPath(path);
            }

            if (selected == null)
            {
                return McpToolRegistry.ToolText("No object found for selection request.", true);
            }

            Selection.activeObject = selected;
            if (McpJson.AsBool(args, "ping", false))
            {
                EditorGUIUtility.PingObject(selected);
            }

            return JsonText(McpJson.Object("selected", UnityMcpEditorTools.DescribeObject(selected)));
        }

        private static Dictionary<string, object> CreateGameObjectImpl(Dictionary<string, object> args)
        {
            var name = McpJson.AsString(args, "name", "GameObject");
            var primitiveType = McpJson.AsString(args, "primitiveType");
            var parentObjectId = McpJson.AsString(args, "parentObjectId");
            var select = McpJson.AsBool(args, "select", true);

            var gameObject = CreateObject(name, primitiveType);
            Undo.RegisterCreatedObjectUndo(gameObject, "Create MCP GameObject");

            if (!string.IsNullOrEmpty(parentObjectId))
            {
                var parent = UnityMcpObjectUtility.ResolveObjectById(parentObjectId);
                var parentGameObject = parent as GameObject;
                var parentComponent = parent as Component;
                var parentTransform = parentGameObject != null ? parentGameObject.transform : parentComponent != null ? parentComponent.transform : parent as Transform;
                if (parentTransform == null)
                {
                    UnityEngine.Object.DestroyImmediate(gameObject);
                    return McpToolRegistry.ToolText("Parent objectId is not a GameObject or Transform.", true);
                }

                Undo.SetTransformParent(gameObject.transform, parentTransform, "Parent MCP GameObject");
            }

            gameObject.name = name;
            if (select)
            {
                Selection.activeObject = gameObject;
            }

            return JsonText(McpJson.Object("created", UnityMcpEditorTools.DescribeObject(gameObject)));
        }

        internal static string GetHierarchyPath(Transform transform)
        {
            if (transform == null)
            {
                return string.Empty;
            }

            var names = new Stack<string>();
            var current = transform;
            while (current != null)
            {
                names.Push(current.name);
                current = current.parent;
            }

            return string.Join("/", names.ToArray());
        }

        private static Dictionary<string, object> DescribeScene(Scene scene)
        {
            return McpJson.Object(
                "name", scene.name,
                "path", scene.path,
                "buildIndex", scene.buildIndex,
                "isLoaded", scene.isLoaded,
                "isDirty", scene.isDirty,
                "isActive", scene == SceneManager.GetActiveScene(),
                "rootCount", scene.rootCount);
        }

        private static Dictionary<string, object> DescribeTransform(Transform transform, int depth, int maxDepth, bool includeInactive, ref int remaining)
        {
            remaining--;
            var children = new List<object>();
            if (depth < maxDepth)
            {
                for (var i = 0; i < transform.childCount && remaining > 0; i++)
                {
                    var child = transform.GetChild(i);
                    if (!includeInactive && !child.gameObject.activeInHierarchy)
                    {
                        continue;
                    }

                    children.Add(DescribeTransform(child, depth + 1, maxDepth, includeInactive, ref remaining));
                }
            }

            var payload = McpJson.Object(
                "name", transform.name,
                "activeSelf", transform.gameObject.activeSelf,
                "activeInHierarchy", transform.gameObject.activeInHierarchy,
                "tag", transform.gameObject.tag,
                "layer", transform.gameObject.layer,
                "path", GetHierarchyPath(transform),
                "componentTypes", GetComponentTypes(transform.gameObject),
                "children", children);
            UnityMcpObjectUtility.AddObjectId(payload, transform.gameObject);
            return payload;
        }

        private static List<object> GetComponentTypes(GameObject gameObject)
        {
            var componentTypes = new List<object>();
            foreach (var component in gameObject.GetComponents<Component>())
            {
                componentTypes.Add(component != null ? component.GetType().FullName : "MissingComponent");
            }

            return componentTypes;
        }

        private static GameObject CreateObject(string name, string primitiveType)
        {
            if (string.IsNullOrEmpty(primitiveType))
            {
                return new GameObject(name);
            }

            if (!Enum.TryParse(primitiveType, true, out PrimitiveType parsed))
            {
                throw new ArgumentException("Unsupported primitiveType: " + primitiveType);
            }

            return GameObject.CreatePrimitive(parsed);
        }

        private static IEnumerable<Scene> EnumerateOpenScenes()
        {
            for (var i = 0; i < SceneManager.sceneCount; i++)
            {
                yield return SceneManager.GetSceneAt(i);
            }
        }

        private static Scene ResolveOpenScene(string scenePath)
        {
            if (string.IsNullOrEmpty(scenePath))
            {
                return SceneManager.GetActiveScene();
            }

            scenePath = UnityMcpPathUtility.NormalizeProjectRelativePath(scenePath);
            foreach (var scene in EnumerateOpenScenes())
            {
                if (string.Equals(scene.path, scenePath, StringComparison.Ordinal))
                {
                    return scene;
                }
            }

            return default;
        }

        private static string ResolveSceneAssetPath(string path, bool mustExist)
        {
            var relativePath = UnityMcpPathUtility.ResolveAssetOrPackageRelativePath(path);
            if (!relativePath.EndsWith(".unity", StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException("Scene path must end with .unity.");
            }

            if (mustExist && !File.Exists(Path.Combine(UnityMcpPathUtility.ProjectRoot, relativePath)))
            {
                throw new ArgumentException("Scene asset not found: " + relativePath);
            }

            return relativePath;
        }

        private static OpenSceneMode ParseOpenSceneMode(string mode)
        {
            try
            {
                return (OpenSceneMode)Enum.Parse(typeof(OpenSceneMode), mode, true);
            }
            catch (ArgumentException)
            {
                throw new ArgumentException("Unsupported scene open mode: " + mode);
            }
        }

        private static bool TryPrepareForSingleSceneChange(Dictionary<string, object> args, out string error)
        {
            error = null;
            var dirtyScenes = new List<Scene>();
            foreach (var scene in EnumerateOpenScenes())
            {
                if (scene.isDirty)
                {
                    dirtyScenes.Add(scene);
                }
            }

            if (dirtyScenes.Count == 0 || McpJson.AsBool(args, "discardUnsavedChanges", false))
            {
                return true;
            }

            if (!McpJson.AsBool(args, "saveDirtyScenes", false))
            {
                error = "Open scenes contain unsaved changes. Set saveDirtyScenes or discardUnsavedChanges.";
                return false;
            }

            foreach (var scene in dirtyScenes)
            {
                if (string.IsNullOrEmpty(scene.path))
                {
                    error = "Cannot save dirty scene because it has no asset path.";
                    return false;
                }
            }

            if (!EditorSceneManager.SaveOpenScenes())
            {
                error = "Unity failed to save open scenes.";
                return false;
            }

            return true;
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
