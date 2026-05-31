using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace StrangeApe.OpenUnityMcp
{
    internal static class UnityMcpComponentTools
    {
        public static readonly McpTool AddComponent = new McpTool(
            "unity.add_component",
            "Add a Component to a GameObject by type name.",
            McpToolRegistry.ObjectSchema(
                "objectId", McpToolRegistry.StringProperty("Target GameObject or Component objectId from another tool result."),
                "path", McpToolRegistry.StringProperty("Optional target asset path for prefab assets."),
                "childPath", McpToolRegistry.StringProperty("Optional child Transform path below the target root."),
                "childName", McpToolRegistry.StringProperty("Optional child Transform name below the target root."),
                "componentType", McpToolRegistry.StringProperty("Component type name, such as Rigidbody, UnityEngine.BoxCollider, or a script class name."),
                new[] { "componentType" }),
            AddComponentImpl);

        public static readonly McpTool GetComponents = new McpTool(
            "unity.get_components",
            "List Components on a GameObject selected by objectId, asset path, or current selection.",
            McpToolRegistry.ObjectSchema(
                "objectId", McpToolRegistry.StringProperty("Target GameObject or Component objectId from another tool result."),
                "path", McpToolRegistry.StringProperty("Optional target asset path."),
                "childPath", McpToolRegistry.StringProperty("Optional child Transform path below the target root."),
                "childName", McpToolRegistry.StringProperty("Optional child Transform name below the target root.")),
            GetComponentsImpl);

        public static readonly McpTool GetSerializedProperties = new McpTool(
            "unity.get_serialized_properties",
            "Read serialized properties from a Unity object with a bounded result count.",
            McpToolRegistry.ObjectSchema(
                "objectId", McpToolRegistry.StringProperty("Target objectId from another tool result."),
                "path", McpToolRegistry.StringProperty("Optional target asset path."),
                "limit", McpToolRegistry.IntegerProperty("Maximum properties to return.", 1, 500),
                "includeChildren", McpToolRegistry.BooleanProperty("Include child properties.")),
            GetSerializedPropertiesImpl);

        public static readonly McpTool SetSerializedProperty = new McpTool(
            "unity.set_serialized_property",
            "Set a basic serialized property on a Unity object and mark it dirty.",
            McpToolRegistry.ObjectSchema(
                "objectId", McpToolRegistry.StringProperty("Target objectId from another tool result."),
                "path", McpToolRegistry.StringProperty("Optional target asset path."),
                "propertyPath", McpToolRegistry.StringProperty("SerializedProperty path, such as m_Name or m_LocalPosition.x."),
                "value", McpJson.Object("description", "New primitive, enum, vector, or color value."),
                "objectReferenceObjectId", McpToolRegistry.StringProperty("Optional objectId for object reference properties."),
                "objectReferencePath", McpToolRegistry.StringProperty("Optional asset path for object reference properties."),
                new[] { "propertyPath" }),
            SetSerializedPropertyImpl);

        private static Dictionary<string, object> AddComponentImpl(Dictionary<string, object> args)
        {
            var gameObject = ResolveGameObject(args);
            if (gameObject == null)
            {
                return McpToolRegistry.ToolText("Target is not a GameObject or Component.", true);
            }

            var componentType = UnityMcpObjectUtility.ResolveType(RequireString(args, "componentType"), typeof(Component));
            if (PrefabUtility.IsPartOfPrefabAsset(gameObject))
            {
                return AddComponentToPrefabAsset(gameObject, componentType);
            }

            var component = Undo.AddComponent(gameObject, componentType);
            EditorUtility.SetDirty(gameObject);

            return JsonText(McpJson.Object(
                "gameObject", UnityMcpEditorTools.DescribeObject(gameObject),
                "component", DescribeComponent(component)));
        }

        private static Dictionary<string, object> GetComponentsImpl(Dictionary<string, object> args)
        {
            var gameObject = ResolveGameObject(args);
            if (gameObject == null)
            {
                return McpToolRegistry.ToolText("Target is not a GameObject or Component.", true);
            }

            var components = new List<object>();
            foreach (var component in gameObject.GetComponents<Component>())
            {
                components.Add(DescribeComponent(component));
            }

            return JsonText(McpJson.Object(
                "gameObject", UnityMcpEditorTools.DescribeObject(gameObject),
                "components", components));
        }

        private static Dictionary<string, object> GetSerializedPropertiesImpl(Dictionary<string, object> args)
        {
            var target = ResolveObject(args);
            if (target == null)
            {
                return McpToolRegistry.ToolText("Target object not found.", true);
            }

            var limit = Math.Max(1, Math.Min(500, McpJson.AsInt(args, "limit", 100)));
            var includeChildren = McpJson.AsBool(args, "includeChildren", true);
            var serializedObject = new SerializedObject(target);
            var iterator = serializedObject.GetIterator();
            var enterChildren = true;
            var properties = new List<object>();

            while (iterator.NextVisible(enterChildren) && properties.Count < limit)
            {
                properties.Add(DescribeProperty(iterator));
                enterChildren = includeChildren;
            }

            var truncated = false;
            if (properties.Count >= limit)
            {
                try
                {
                    truncated = iterator.NextVisible(false);
                }
                catch
                {
                    truncated = false;
                }
            }

            return JsonText(McpJson.Object(
                "target", UnityMcpEditorTools.DescribeObject(target),
                "count", properties.Count,
                "truncated", truncated,
                "properties", properties));
        }

        private static Dictionary<string, object> SetSerializedPropertyImpl(Dictionary<string, object> args)
        {
            var target = ResolveObject(args);
            if (target == null)
            {
                return McpToolRegistry.ToolText("Target object not found.", true);
            }

            var propertyPath = RequireString(args, "propertyPath");
            var serializedObject = new SerializedObject(target);
            var property = serializedObject.FindProperty(propertyPath);
            if (property == null)
            {
                return McpToolRegistry.ToolText("Serialized property not found: " + propertyPath, true);
            }

            if (!TrySetProperty(property, args, out var error))
            {
                return McpToolRegistry.ToolText(error, true);
            }

            serializedObject.ApplyModifiedProperties();
            EditorUtility.SetDirty(target);
            if (!string.IsNullOrEmpty(AssetDatabase.GetAssetPath(target)))
            {
                AssetDatabase.SaveAssetIfDirty(target);
            }

            return JsonText(McpJson.Object(
                "target", UnityMcpEditorTools.DescribeObject(target),
                "property", DescribeProperty(property),
                "changed", true));
        }

        internal static Dictionary<string, object> DescribeComponent(Component component)
        {
            if (component == null)
            {
                var missing = McpJson.Object(
                    "type", "MissingComponent",
                    "enabled", false);
                UnityMcpObjectUtility.AddObjectId(missing, null);
                return missing;
            }

            var behaviour = component as Behaviour;
            var renderer = component as Renderer;

            var payload = McpJson.Object(
                "name", component.name,
                "type", component.GetType().FullName,
                "enabled", behaviour != null ? behaviour.enabled : renderer == null || renderer.enabled);
            UnityMcpObjectUtility.AddObjectId(payload, component);
            UnityMcpObjectUtility.AddObjectId(payload, component.gameObject, "gameObject");
            return payload;
        }

        private static Dictionary<string, object> AddComponentToPrefabAsset(GameObject assetObject, Type componentType)
        {
            var prefabPath = AssetDatabase.GetAssetPath(assetObject);
            if (string.IsNullOrEmpty(prefabPath) || !prefabPath.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase))
            {
                return McpToolRegistry.ToolText("Target is part of a prefab asset but its asset path could not be resolved.", true);
            }

            var assetRoot = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (assetRoot == null)
            {
                return McpToolRegistry.ToolText("Prefab asset root not found: " + prefabPath, true);
            }

            var relativePath = UnityMcpObjectUtility.GetRelativeHierarchyPath(assetRoot.transform, assetObject.transform);
            var componentIndex = assetObject.GetComponents(componentType).Length;
            GameObject contentsRoot = null;
            try
            {
                contentsRoot = PrefabUtility.LoadPrefabContents(prefabPath);
                var target = string.IsNullOrEmpty(relativePath)
                    ? contentsRoot
                    : UnityMcpObjectUtility.FindChildByPath(contentsRoot.transform, relativePath)?.gameObject;

                if (target == null)
                {
                    return McpToolRegistry.ToolText("Child path no longer exists in prefab contents: " + relativePath, true);
                }

                target.AddComponent(componentType);
                bool saved;
                PrefabUtility.SaveAsPrefabAsset(contentsRoot, prefabPath, out saved);
                if (!saved)
                {
                    return McpToolRegistry.ToolText("Unity failed to save prefab asset: " + prefabPath, true);
                }
            }
            finally
            {
                if (contentsRoot != null)
                {
                    PrefabUtility.UnloadPrefabContents(contentsRoot);
                }
            }

            AssetDatabase.ImportAsset(prefabPath);
            var savedRoot = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            var savedTarget = savedRoot == null
                ? null
                : string.IsNullOrEmpty(relativePath)
                    ? savedRoot
                    : UnityMcpObjectUtility.FindChildByPath(savedRoot.transform, relativePath)?.gameObject;
            var savedComponents = savedTarget != null ? savedTarget.GetComponents(componentType) : Array.Empty<Component>();
            var savedComponent = savedComponents.Length > componentIndex ? savedComponents[componentIndex] : null;

            return JsonText(McpJson.Object(
                "gameObject", savedTarget != null ? UnityMcpEditorTools.DescribeObject(savedTarget) : McpJson.Object(),
                "component", DescribeComponent(savedComponent),
                "prefabPath", prefabPath,
                "saved", true));
        }

        private static bool TrySetProperty(SerializedProperty property, Dictionary<string, object> args, out string error)
        {
            error = null;
            args.TryGetValue("value", out var value);

            try
            {
                switch (property.propertyType)
                {
                    case SerializedPropertyType.Integer:
                        property.intValue = Convert.ToInt32(value);
                        return true;
                    case SerializedPropertyType.Boolean:
                        property.boolValue = Convert.ToBoolean(value);
                        return true;
                    case SerializedPropertyType.Float:
                        property.floatValue = Convert.ToSingle(value);
                        return true;
                    case SerializedPropertyType.String:
                        property.stringValue = Convert.ToString(value);
                        return true;
                    case SerializedPropertyType.Enum:
                        return TrySetEnum(property, value, out error);
                    case SerializedPropertyType.ObjectReference:
                        return TrySetObjectReference(property, args, out error);
                    case SerializedPropertyType.Color:
                        property.colorValue = ReadColor(value);
                        return true;
                    case SerializedPropertyType.Vector2:
                        property.vector2Value = ReadVector2(value);
                        return true;
                    case SerializedPropertyType.Vector3:
                        property.vector3Value = ReadVector3(value);
                        return true;
                    case SerializedPropertyType.Vector4:
                        property.vector4Value = ReadVector4(value);
                        return true;
                    case SerializedPropertyType.Rect:
                        property.rectValue = ReadRect(value);
                        return true;
                    case SerializedPropertyType.Bounds:
                        property.boundsValue = ReadBounds(value);
                        return true;
                    default:
                        error = "Unsupported property type for setting: " + property.propertyType;
                        return false;
                }
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        private static bool TrySetEnum(SerializedProperty property, object value, out string error)
        {
            error = null;
            if (value is string text)
            {
                for (var i = 0; i < property.enumNames.Length; i++)
                {
                    if (string.Equals(property.enumNames[i], text, StringComparison.Ordinal) ||
                        string.Equals(property.enumDisplayNames[i], text, StringComparison.Ordinal))
                    {
                        property.enumValueIndex = i;
                        return true;
                    }
                }

                error = "Enum value not found: " + text;
                return false;
            }

            property.enumValueIndex = Convert.ToInt32(value);
            return true;
        }

        private static bool TrySetObjectReference(SerializedProperty property, Dictionary<string, object> args, out string error)
        {
            error = null;
            var objectId = McpJson.AsString(args, "objectReferenceObjectId");
            var path = McpJson.AsString(args, "objectReferencePath");
            if (string.IsNullOrEmpty(objectId) && string.IsNullOrEmpty(path) && args.TryGetValue("value", out var value) && value == null)
            {
                property.objectReferenceValue = null;
                return true;
            }

            var reference = UnityMcpObjectUtility.ResolveObject(objectId, path);
            if (reference == null)
            {
                error = "Object reference target not found.";
                return false;
            }

            property.objectReferenceValue = reference;
            return true;
        }

        private static Dictionary<string, object> DescribeProperty(SerializedProperty property)
        {
            return McpJson.Object(
                "path", property.propertyPath,
                "displayName", property.displayName,
                "type", property.propertyType.ToString(),
                "depth", property.depth,
                "editable", property.editable,
                "isArray", property.isArray,
                "arraySize", property.isArray ? property.arraySize : 0,
                "value", ReadPropertyValue(property));
        }

        private static object ReadPropertyValue(SerializedProperty property)
        {
            switch (property.propertyType)
            {
                case SerializedPropertyType.Integer:
                    return property.intValue;
                case SerializedPropertyType.Boolean:
                    return property.boolValue;
                case SerializedPropertyType.Float:
                    return property.floatValue;
                case SerializedPropertyType.String:
                    return property.stringValue;
                case SerializedPropertyType.Color:
                    return DescribeColor(property.colorValue);
                case SerializedPropertyType.ObjectReference:
                    return property.objectReferenceValue != null ? UnityMcpEditorTools.DescribeObject(property.objectReferenceValue) : null;
                case SerializedPropertyType.LayerMask:
                    return property.intValue;
                case SerializedPropertyType.Enum:
                    return McpJson.Object(
                        "index", property.enumValueIndex,
                        "name", property.enumValueIndex >= 0 && property.enumValueIndex < property.enumNames.Length ? property.enumNames[property.enumValueIndex] : string.Empty,
                        "displayName", property.enumValueIndex >= 0 && property.enumValueIndex < property.enumDisplayNames.Length ? property.enumDisplayNames[property.enumValueIndex] : string.Empty);
                case SerializedPropertyType.Vector2:
                    return DescribeVector2(property.vector2Value);
                case SerializedPropertyType.Vector3:
                    return DescribeVector3(property.vector3Value);
                case SerializedPropertyType.Vector4:
                    return DescribeVector4(property.vector4Value);
                case SerializedPropertyType.Rect:
                    return DescribeRect(property.rectValue);
                case SerializedPropertyType.Bounds:
                    return DescribeBounds(property.boundsValue);
                default:
                    return null;
            }
        }

        private static UnityEngine.Object ResolveObject(Dictionary<string, object> args)
        {
            return UnityMcpObjectUtility.ResolveObject(McpJson.AsString(args, "objectId"), McpJson.AsString(args, "path"));
        }

        private static GameObject ResolveGameObject(Dictionary<string, object> args)
        {
            return UnityMcpObjectUtility.ResolveGameObject(
                McpJson.AsString(args, "objectId"),
                McpJson.AsString(args, "path"),
                McpJson.AsString(args, "childPath"),
                McpJson.AsString(args, "childName"));
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

        private static Vector2 ReadVector2(object value)
        {
            var map = McpJson.AsObject(value);
            return new Vector2(ReadFloat(map, "x"), ReadFloat(map, "y"));
        }

        private static Vector3 ReadVector3(object value)
        {
            var map = McpJson.AsObject(value);
            return new Vector3(ReadFloat(map, "x"), ReadFloat(map, "y"), ReadFloat(map, "z"));
        }

        private static Vector4 ReadVector4(object value)
        {
            var map = McpJson.AsObject(value);
            return new Vector4(ReadFloat(map, "x"), ReadFloat(map, "y"), ReadFloat(map, "z"), ReadFloat(map, "w"));
        }

        private static Color ReadColor(object value)
        {
            var map = McpJson.AsObject(value);
            return new Color(ReadFloat(map, "r"), ReadFloat(map, "g"), ReadFloat(map, "b"), ReadFloat(map, "a", 1f));
        }

        private static Rect ReadRect(object value)
        {
            var map = McpJson.AsObject(value);
            return new Rect(ReadFloat(map, "x"), ReadFloat(map, "y"), ReadFloat(map, "width"), ReadFloat(map, "height"));
        }

        private static Bounds ReadBounds(object value)
        {
            var map = McpJson.AsObject(value);
            return new Bounds(ReadVector3(map != null && map.TryGetValue("center", out var center) ? center : null), ReadVector3(map != null && map.TryGetValue("size", out var size) ? size : null));
        }

        private static float ReadFloat(Dictionary<string, object> map, string key, float fallback = 0f)
        {
            if (map == null || !map.TryGetValue(key, out var value) || value == null)
            {
                return fallback;
            }

            return Convert.ToSingle(value);
        }

        private static Dictionary<string, object> DescribeVector2(Vector2 value)
        {
            return McpJson.Object("x", value.x, "y", value.y);
        }

        private static Dictionary<string, object> DescribeVector3(Vector3 value)
        {
            return McpJson.Object("x", value.x, "y", value.y, "z", value.z);
        }

        private static Dictionary<string, object> DescribeVector4(Vector4 value)
        {
            return McpJson.Object("x", value.x, "y", value.y, "z", value.z, "w", value.w);
        }

        private static Dictionary<string, object> DescribeColor(Color value)
        {
            return McpJson.Object("r", value.r, "g", value.g, "b", value.b, "a", value.a);
        }

        private static Dictionary<string, object> DescribeRect(Rect value)
        {
            return McpJson.Object("x", value.x, "y", value.y, "width", value.width, "height", value.height);
        }

        private static Dictionary<string, object> DescribeBounds(Bounds value)
        {
            return McpJson.Object("center", DescribeVector3(value.center), "size", DescribeVector3(value.size));
        }

        private static Dictionary<string, object> JsonText(Dictionary<string, object> payload)
        {
            return McpToolRegistry.ToolText(McpJson.Stringify(payload));
        }
    }
}
