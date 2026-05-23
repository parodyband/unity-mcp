using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;

namespace StrangeApe.OpenUnityMcp
{
    internal static class UnityMcpConsoleLogReader
    {
        public static List<object> Read(int limit)
        {
            var result = new List<object>();
            var editorAssembly = typeof(EditorApplication).Assembly;
            var logEntriesType = editorAssembly.GetType("UnityEditor.LogEntries");
            var logEntryType = editorAssembly.GetType("UnityEditor.LogEntry");
            if (logEntriesType == null || logEntryType == null)
            {
                result.Add(McpJson.Object("message", "Unity console reflection API was not found.", "type", "warning"));
                return result;
            }

            var getCount = logEntriesType.GetMethod("GetCount", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
            var startGettingEntries = logEntriesType.GetMethod("StartGettingEntries", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
            var endGettingEntries = logEntriesType.GetMethod("EndGettingEntries", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
            var getEntryInternal = logEntriesType.GetMethod("GetEntryInternal", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);

            if (getCount == null || getEntryInternal == null)
            {
                result.Add(McpJson.Object("message", "Unity console reflection methods were not found.", "type", "warning"));
                return result;
            }

            var count = Convert.ToInt32(getCount.Invoke(null, null));
            var start = Math.Max(0, count - limit);
            var entry = Activator.CreateInstance(logEntryType);

            startGettingEntries?.Invoke(null, null);
            try
            {
                for (var i = start; i < count; i++)
                {
                    if (!TryGetEntry(getEntryInternal, entry, i))
                    {
                        continue;
                    }

                    result.Add(McpJson.Object(
                        "index", i,
                        "type", ReadLogType(logEntryType, entry),
                        "message", ReadStringField(logEntryType, entry, "condition"),
                        "stackTrace", ReadStringField(logEntryType, entry, "stackTrace"),
                        "file", ReadStringField(logEntryType, entry, "file"),
                        "line", ReadIntField(logEntryType, entry, "line")));
                }
            }
            finally
            {
                endGettingEntries?.Invoke(null, null);
            }

            return result;
        }

        private static bool TryGetEntry(MethodInfo getEntryInternal, object entry, int index)
        {
            var parameters = getEntryInternal.GetParameters();
            object value;
            if (parameters.Length == 2 && parameters[0].ParameterType == typeof(int))
            {
                value = getEntryInternal.Invoke(null, new[] { (object)index, entry });
            }
            else if (parameters.Length == 2)
            {
                value = getEntryInternal.Invoke(null, new[] { entry, (object)index });
            }
            else
            {
                return false;
            }

            return !(value is bool boolValue) || boolValue;
        }

        private static string ReadLogType(Type entryType, object entry)
        {
            var mode = ReadIntField(entryType, entry, "mode");
            if ((mode & 1) != 0)
            {
                return "error";
            }

            if ((mode & 2) != 0)
            {
                return "assert";
            }

            if ((mode & 4) != 0)
            {
                return "warning";
            }

            return "log";
        }

        private static string ReadStringField(Type type, object target, string name)
        {
            var field = type.GetField(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            return field != null ? Convert.ToString(field.GetValue(target)) : string.Empty;
        }

        private static int ReadIntField(Type type, object target, string name)
        {
            var field = type.GetField(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (field == null)
            {
                return 0;
            }

            return Convert.ToInt32(field.GetValue(target));
        }
    }
}
