using System;
using System.Collections.Generic;
using System.Globalization;
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
            "Read the open scene hierarchy or a target GameObject/prefab hierarchy with bounded depth and result count.",
            McpToolRegistry.ObjectSchema(
                "scenePath", McpToolRegistry.StringProperty("Optional open scene path. Defaults to all open scenes."),
                "objectId", McpToolRegistry.StringProperty("Optional root GameObject or Component objectId."),
                "path", McpToolRegistry.StringProperty("Optional root prefab asset path under Assets or Packages."),
                "childPath", McpToolRegistry.StringProperty("Optional child Transform path below the target root."),
                "childName", McpToolRegistry.StringProperty("Optional child Transform name below the target root."),
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
            "Create an empty GameObject or primitive in the active scene with an optional explicit transform.",
            McpToolRegistry.ObjectSchema(
                "name", McpToolRegistry.StringProperty("New GameObject name."),
                "primitiveType", McpToolRegistry.StringProperty("Optional Unity primitive type: Cube, Sphere, Capsule, Cylinder, Plane, Quad."),
                "parentObjectId", McpToolRegistry.StringProperty("Optional parent GameObject or Transform objectId."),
                "position", McpToolRegistry.Vector3Property("Optional world position."),
                "localPosition", McpToolRegistry.Vector3Property("Optional local position. Use this when parentObjectId is set."),
                "rotationEuler", McpToolRegistry.Vector3Property("Optional world rotation in degrees."),
                "localRotationEuler", McpToolRegistry.Vector3Property("Optional local rotation in degrees. Use this when parentObjectId is set."),
                "scale", McpToolRegistry.Vector3Property("Optional local scale."),
                "localScale", McpToolRegistry.Vector3Property("Optional local scale."),
                "select", McpToolRegistry.BooleanProperty("Select the created GameObject.")),
            CreateGameObjectImpl);

        public static readonly McpTool CreateGameObjects = new McpTool(
            "unity.create_game_objects",
            "Create multiple empty GameObjects or primitives in one scene mutation. Supports explicit transforms and parenting by objectId or prior batch index.",
            McpToolRegistry.ObjectSchema(
                "objects", McpJson.Object(
                    "type", "array",
                    "description", "Objects to create. Each item supports name, primitiveType, parentObjectId, parentIndex, position/localPosition, rotationEuler/localRotationEuler, and scale/localScale.",
                    "minItems", 1,
                    "maxItems", 100,
                    "items", McpJson.Object(
                        "type", "object",
                        "properties", McpJson.Object(
                            "name", McpToolRegistry.StringProperty("New GameObject name."),
                            "primitiveType", McpToolRegistry.StringProperty("Optional Unity primitive type: Cube, Sphere, Capsule, Cylinder, Plane, Quad."),
                            "parentObjectId", McpToolRegistry.StringProperty("Optional existing parent GameObject or Transform objectId."),
                            "parentIndex", McpToolRegistry.IntegerProperty("Optional index of a previously created object in this batch to use as parent.", 0, 99),
                            "position", McpToolRegistry.Vector3Property("Optional world position."),
                            "localPosition", McpToolRegistry.Vector3Property("Optional local position. Use this when parentObjectId or parentIndex is set."),
                            "rotationEuler", McpToolRegistry.Vector3Property("Optional world rotation in degrees."),
                            "localRotationEuler", McpToolRegistry.Vector3Property("Optional local rotation in degrees. Use this when parentObjectId or parentIndex is set."),
                            "scale", McpToolRegistry.Vector3Property("Optional local scale."),
                            "localScale", McpToolRegistry.Vector3Property("Optional local scale.")),
                        "additionalProperties", false)),
                "selectLast", McpToolRegistry.BooleanProperty("Select the last created GameObject."),
                new[] { "objects" }),
            CreateGameObjectsImpl);

        public static readonly McpTool SetTransform = new McpTool(
            "unity.set_transform",
            "Set transform values on an existing scene GameObject. Returns the final transform read back from Unity.",
            McpToolRegistry.ObjectSchema(
                "objectId", McpToolRegistry.StringProperty("Target GameObject, Component, or Transform objectId."),
                "position", McpToolRegistry.Vector3Property("Optional world position."),
                "localPosition", McpToolRegistry.Vector3Property("Optional local position."),
                "rotationEuler", McpToolRegistry.Vector3Property("Optional world rotation in degrees."),
                "localRotationEuler", McpToolRegistry.Vector3Property("Optional local rotation in degrees."),
                "scale", McpToolRegistry.Vector3Property("Optional local scale."),
                "localScale", McpToolRegistry.Vector3Property("Optional local scale."),
                new[] { "objectId" }),
            SetTransformImpl);

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

            if (!string.IsNullOrEmpty(McpJson.AsString(args, "objectId")) || !string.IsNullOrEmpty(McpJson.AsString(args, "path")))
            {
                var target = UnityMcpObjectUtility.ResolveGameObject(
                    McpJson.AsString(args, "objectId"),
                    McpJson.AsString(args, "path"),
                    McpJson.AsString(args, "childPath"),
                    McpJson.AsString(args, "childName"));
                if (target == null)
                {
                    return McpToolRegistry.ToolText("Target is not a GameObject, Component, or prefab asset.", true);
                }

                var rootPayload = DescribeTransform(target.transform, 0, maxDepth, includeInactive, ref remaining);
                return JsonText(McpJson.Object(
                    "maxDepth", maxDepth,
                    "limit", limit,
                    "truncated", remaining <= 0,
                    "target", DescribeGameObject(target),
                    "root", rootPayload));
            }

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
            var select = McpJson.AsBool(args, "select", true);

            var gameObject = CreateGameObjectFromArgs(args, null, name);

            if (select)
            {
                Selection.activeObject = gameObject;
            }

            return JsonText(McpJson.Object("created", DescribeGameObject(gameObject)));
        }

        private static Dictionary<string, object> CreateGameObjectsImpl(Dictionary<string, object> args)
        {
            if (!args.TryGetValue("objects", out var rawObjects))
            {
                throw new ArgumentException("Missing required argument: objects");
            }

            var objectArgs = McpJson.AsArray(rawObjects);
            if (objectArgs == null)
            {
                throw new ArgumentException("objects must be an array.");
            }

            if (objectArgs.Count < 1 || objectArgs.Count > 100)
            {
                throw new ArgumentException("objects must contain between 1 and 100 items.");
            }

            var createdObjects = new List<GameObject>();
            var describedObjects = new List<object>();
            try
            {
                for (var i = 0; i < objectArgs.Count; i++)
                {
                    var item = McpJson.AsObject(objectArgs[i]);
                    if (item == null)
                    {
                        throw new ArgumentException("objects[" + i + "] must be an object.");
                    }

                    var gameObject = CreateGameObjectFromArgs(item, createdObjects, McpJson.AsString(item, "name", "GameObject"));
                    createdObjects.Add(gameObject);
                    describedObjects.Add(McpJson.Object(
                        "index", i,
                        "object", DescribeGameObject(gameObject)));
                }
            }
            catch
            {
                foreach (var createdObject in createdObjects)
                {
                    if (createdObject != null)
                    {
                        UnityEngine.Object.DestroyImmediate(createdObject);
                    }
                }

                throw;
            }

            if (McpJson.AsBool(args, "selectLast", false) && createdObjects.Count > 0)
            {
                Selection.activeObject = createdObjects[createdObjects.Count - 1];
            }

            return JsonText(McpJson.Object(
                "count", createdObjects.Count,
                "created", describedObjects));
        }

        private static Dictionary<string, object> SetTransformImpl(Dictionary<string, object> args)
        {
            var objectId = RequireString(args, "objectId");
            var resolved = UnityMcpObjectUtility.ResolveObjectById(objectId);
            var transform = ResolveTransform(resolved);
            if (transform == null)
            {
                return McpToolRegistry.ToolText("objectId is not a GameObject, Component, or Transform.", true);
            }

            ApplyTransform(transform, args);
            return JsonText(McpJson.Object(
                "changed", true,
                "object", DescribeGameObject(transform.gameObject)));
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
                "transform", DescribeTransformValues(transform),
                "componentTypes", GetComponentTypes(transform.gameObject),
                "children", children);
            UnityMcpObjectUtility.AddObjectId(payload, transform.gameObject);
            return payload;
        }

        internal static Dictionary<string, object> DescribeGameObject(GameObject gameObject)
        {
            var payload = McpJson.Object(
                "name", gameObject.name,
                "type", typeof(GameObject).FullName,
                "scenePath", gameObject.scene.path,
                "hierarchyPath", GetHierarchyPath(gameObject.transform),
                "activeSelf", gameObject.activeSelf,
                "activeInHierarchy", gameObject.activeInHierarchy,
                "tag", gameObject.tag,
                "layer", gameObject.layer,
                "transform", DescribeTransformValues(gameObject.transform),
                "componentTypes", GetComponentTypes(gameObject));
            UnityMcpObjectUtility.AddObjectId(payload, gameObject);
            return payload;
        }

        private static Dictionary<string, object> DescribeTransformValues(Transform transform)
        {
            return McpJson.Object(
                "position", Vector3Payload(transform.position),
                "localPosition", Vector3Payload(transform.localPosition),
                "rotationEuler", Vector3Payload(transform.rotation.eulerAngles),
                "localRotationEuler", Vector3Payload(transform.localRotation.eulerAngles),
                "localScale", Vector3Payload(transform.localScale));
        }

        private static Dictionary<string, object> Vector3Payload(Vector3 value)
        {
            return McpJson.Object(
                "x", value.x,
                "y", value.y,
                "z", value.z);
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

        private static GameObject CreateGameObjectFromArgs(Dictionary<string, object> args, List<GameObject> batchObjects, string fallbackName)
        {
            var name = McpJson.AsString(args, "name", fallbackName);
            var primitiveType = McpJson.AsString(args, "primitiveType");
            GameObject gameObject = null;
            try
            {
                gameObject = CreateObject(name, primitiveType);
                Undo.RegisterCreatedObjectUndo(gameObject, "Create MCP GameObject");
                gameObject.name = name;

                var parentTransform = ResolveParentTransform(args, batchObjects);
                if (parentTransform != null)
                {
                    Undo.SetTransformParent(gameObject.transform, parentTransform, "Parent MCP GameObject");
                    ApplyDefaultLocalTransformForParentedObject(gameObject.transform, args);
                }

                ApplyTransform(gameObject.transform, args);
                return gameObject;
            }
            catch
            {
                if (gameObject != null)
                {
                    UnityEngine.Object.DestroyImmediate(gameObject);
                }

                throw;
            }
        }

        private static Transform ResolveParentTransform(Dictionary<string, object> args, List<GameObject> batchObjects)
        {
            if (args.TryGetValue("parentIndex", out var parentIndexValue))
            {
                if (batchObjects == null)
                {
                    throw new ArgumentException("parentIndex can only be used with unity.create_game_objects.");
                }

                var parentIndex = Convert.ToInt32(parentIndexValue, CultureInfo.InvariantCulture);
                if (parentIndex < 0 || parentIndex >= batchObjects.Count)
                {
                    throw new ArgumentException("parentIndex must reference a previously created object.");
                }

                return batchObjects[parentIndex].transform;
            }

            var parentObjectId = McpJson.AsString(args, "parentObjectId");
            if (string.IsNullOrEmpty(parentObjectId))
            {
                return null;
            }

            var parent = UnityMcpObjectUtility.ResolveObjectById(parentObjectId);
            var parentTransform = ResolveTransform(parent);
            if (parentTransform == null)
            {
                throw new ArgumentException("Parent objectId is not a GameObject, Component, or Transform.");
            }

            return parentTransform;
        }

        private static Transform ResolveTransform(UnityEngine.Object obj)
        {
            if (obj is GameObject gameObject)
            {
                return gameObject.transform;
            }

            if (obj is Component component)
            {
                return component.transform;
            }

            return obj as Transform;
        }

        private static void ApplyDefaultLocalTransformForParentedObject(Transform transform, Dictionary<string, object> args)
        {
            if (!HasVector3(args, "localPosition") && !HasVector3(args, "position"))
            {
                transform.localPosition = Vector3.zero;
            }

            if (!HasVector3(args, "localRotationEuler") && !HasVector3(args, "rotationEuler"))
            {
                transform.localRotation = Quaternion.identity;
            }
        }

        private static void ApplyTransform(Transform transform, Dictionary<string, object> args)
        {
            Undo.RecordObject(transform, "Set MCP Transform");

            if (TryReadVector3(args, "localPosition", out var localPosition))
            {
                transform.localPosition = localPosition;
            }
            else if (TryReadVector3(args, "position", out var position))
            {
                transform.position = position;
            }

            if (TryReadVector3(args, "localRotationEuler", out var localRotationEuler))
            {
                transform.localRotation = Quaternion.Euler(localRotationEuler);
            }
            else if (TryReadVector3(args, "rotationEuler", out var rotationEuler))
            {
                transform.rotation = Quaternion.Euler(rotationEuler);
            }

            if (TryReadVector3(args, "localScale", out var localScale))
            {
                transform.localScale = localScale;
            }
            else if (TryReadVector3(args, "scale", out var scale))
            {
                transform.localScale = scale;
            }
        }

        private static bool HasVector3(Dictionary<string, object> args, string key)
        {
            return args != null && args.TryGetValue(key, out var rawValue) && rawValue != null;
        }

        private static bool TryReadVector3(Dictionary<string, object> args, string key, out Vector3 value)
        {
            value = default;
            if (args == null || !args.TryGetValue(key, out var rawValue) || rawValue == null)
            {
                return false;
            }

            var map = rawValue as Dictionary<string, object>;
            if (map != null)
            {
                value = new Vector3(
                    ReadFloat(map, "x"),
                    ReadFloat(map, "y"),
                    ReadFloat(map, "z"));
                return true;
            }

            var array = rawValue as List<object>;
            if (array != null)
            {
                if (array.Count != 3)
                {
                    throw new ArgumentException(key + " array must have exactly three numeric values.");
                }

                value = new Vector3(
                    Convert.ToSingle(array[0], CultureInfo.InvariantCulture),
                    Convert.ToSingle(array[1], CultureInfo.InvariantCulture),
                    Convert.ToSingle(array[2], CultureInfo.InvariantCulture));
                return true;
            }

            throw new ArgumentException(key + " must be an object with x, y, z or an array of three numbers.");
        }

        private static float ReadFloat(Dictionary<string, object> map, string key)
        {
            if (!map.TryGetValue(key, out var value) || value == null)
            {
                throw new ArgumentException("Vector3 is missing component: " + key);
            }

            return Convert.ToSingle(value, CultureInfo.InvariantCulture);
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
