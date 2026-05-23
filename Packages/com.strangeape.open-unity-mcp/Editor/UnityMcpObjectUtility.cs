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

        private static bool IsValidType(Type type, Type requiredBaseType)
        {
            return type != null &&
                   !type.IsAbstract &&
                   requiredBaseType.IsAssignableFrom(type);
        }
    }
}
