using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEditor;
using UnityEngine;

namespace StrangeApe.OpenUnityMcp
{
    internal static class UnityMcpObjectUtility
    {
        public static UnityEngine.Object ResolveObject(string objectId, string path)
        {
            if (!string.IsNullOrEmpty(objectId))
            {
                return ResolveObjectById(objectId);
            }

            if (!string.IsNullOrEmpty(path))
            {
                var relativePath = UnityMcpPathUtility.ResolveAssetOrPackageRelativePath(path);
                return AssetDatabase.LoadMainAssetAtPath(relativePath);
            }

            return Selection.activeObject;
        }

        public static GameObject ResolveGameObject(string objectId, string path)
        {
            var obj = ResolveObject(objectId, path);
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

        public static GameObject ResolveGameObject(string objectId, string path, string childPath, string childName)
        {
            var gameObject = ResolveGameObject(objectId, path);
            if (gameObject == null)
            {
                return null;
            }

            var child = ResolveChild(gameObject.transform, childPath, childName);
            return child != null ? child.gameObject : null;
        }

        public static Transform ResolveChild(Transform root, string childPath, string childName)
        {
            if (root == null)
            {
                return null;
            }

            if (!string.IsNullOrEmpty(childPath))
            {
                return FindChildByPath(root, childPath);
            }

            if (!string.IsNullOrEmpty(childName))
            {
                return FindChildByName(root, childName);
            }

            return root;
        }

        public static Transform FindChildByPath(Transform root, string childPath)
        {
            if (root == null)
            {
                return null;
            }

            childPath = (childPath ?? string.Empty).Replace('\\', '/').Trim('/');
            if (string.IsNullOrEmpty(childPath) || string.Equals(childPath, root.name, StringComparison.Ordinal))
            {
                return root;
            }

            var parts = childPath.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
            var index = parts.Length > 0 && string.Equals(parts[0], root.name, StringComparison.Ordinal) ? 1 : 0;
            var current = root;
            for (var i = index; i < parts.Length; i++)
            {
                current = FindDirectChild(current, parts[i]);
                if (current == null)
                {
                    return null;
                }
            }

            return current;
        }

        public static Transform FindChildByName(Transform root, string childName)
        {
            if (root == null || string.IsNullOrEmpty(childName))
            {
                return null;
            }

            if (string.Equals(root.name, childName, StringComparison.Ordinal))
            {
                return root;
            }

            for (var i = 0; i < root.childCount; i++)
            {
                var child = root.GetChild(i);
                var match = FindChildByName(child, childName);
                if (match != null)
                {
                    return match;
                }
            }

            return null;
        }

        public static List<Transform> FindChildren(Transform root, string childPath, string childName, bool includeInactive, int limit)
        {
            var matches = new List<Transform>();
            if (root == null || limit <= 0)
            {
                return matches;
            }

            if (!string.IsNullOrEmpty(childPath))
            {
                var match = FindChildByPath(root, childPath);
                if (match != null && ShouldInclude(match, includeInactive))
                {
                    matches.Add(match);
                }

                return matches;
            }

            CollectChildrenByName(root, childName, includeInactive, limit, matches);
            return matches;
        }

        public static string GetRelativeHierarchyPath(Transform root, Transform target)
        {
            if (root == null || target == null)
            {
                return string.Empty;
            }

            if (root == target)
            {
                return string.Empty;
            }

            var names = new Stack<string>();
            var current = target;
            while (current != null && current != root)
            {
                names.Push(current.name);
                current = current.parent;
            }

            return current == root ? string.Join("/", names.ToArray()) : string.Empty;
        }

        public static UnityEngine.Object ResolveObjectById(string objectId)
        {
            if (!ulong.TryParse(objectId, NumberStyles.Integer, CultureInfo.InvariantCulture, out var rawEntityId))
            {
                return null;
            }

            var entityId = EntityId.FromULong(rawEntityId);
            return EditorUtility.EntityIdToObject(entityId);
        }

        public static string GetObjectId(UnityEngine.Object obj)
        {
            if (obj == null)
            {
                return string.Empty;
            }

            return EntityId.ToULong(obj.GetEntityId()).ToString(CultureInfo.InvariantCulture);
        }

        public static void AddObjectId(Dictionary<string, object> payload, UnityEngine.Object obj, string prefix = null)
        {
            var idKey = string.IsNullOrEmpty(prefix) ? "objectId" : prefix + "Id";
            var typeKey = string.IsNullOrEmpty(prefix) ? "objectIdType" : prefix + "IdType";
            payload[idKey] = GetObjectId(obj);
            payload[typeKey] = "entityId";
        }

        public static Type ResolveType(string typeName, Type requiredBaseType)
        {
            if (string.IsNullOrEmpty(typeName))
            {
                throw new ArgumentException("Missing type name.");
            }

            var direct = Type.GetType(typeName);
            if (IsValidType(direct, requiredBaseType))
            {
                return direct;
            }

            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type[] types;
                try
                {
                    types = assembly.GetTypes();
                }
                catch
                {
                    continue;
                }

                foreach (var type in types)
                {
                    if (!IsValidType(type, requiredBaseType))
                    {
                        continue;
                    }

                    if (string.Equals(type.FullName, typeName, StringComparison.Ordinal) ||
                        string.Equals(type.Name, typeName, StringComparison.Ordinal))
                    {
                        return type;
                    }
                }
            }

            throw new ArgumentException("Could not resolve type: " + typeName);
        }

        private static Transform FindDirectChild(Transform parent, string childName)
        {
            for (var i = 0; i < parent.childCount; i++)
            {
                var child = parent.GetChild(i);
                if (string.Equals(child.name, childName, StringComparison.Ordinal))
                {
                    return child;
                }
            }

            return null;
        }

        private static void CollectChildrenByName(Transform root, string childName, bool includeInactive, int limit, List<Transform> matches)
        {
            if (matches.Count >= limit)
            {
                return;
            }

            if ((string.IsNullOrEmpty(childName) || string.Equals(root.name, childName, StringComparison.Ordinal)) &&
                ShouldInclude(root, includeInactive))
            {
                matches.Add(root);
            }

            for (var i = 0; i < root.childCount && matches.Count < limit; i++)
            {
                CollectChildrenByName(root.GetChild(i), childName, includeInactive, limit, matches);
            }
        }

        private static bool ShouldInclude(Transform transform, bool includeInactive)
        {
            return includeInactive ||
                   transform.gameObject.activeInHierarchy ||
                   (!transform.gameObject.scene.IsValid() && transform.gameObject.activeSelf);
        }

        private static bool IsValidType(Type type, Type requiredBaseType)
        {
            return type != null &&
                   !type.IsAbstract &&
                   requiredBaseType.IsAssignableFrom(type);
        }
    }
}
