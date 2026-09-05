using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace StrangeApe.OpenUnityMcp
{
    internal static class UnityMcpBulkEditTools
    {
        public static readonly McpTool Edit = new McpTool(
            "unity.edit_objects",
            "Set serialized properties on up to 100 scene objects/components and return final values in one main-thread turn. Requires editorEpoch from query_scene. Validates all targets before applying; not atomic if Unity fails during apply. Does not save scenes or modify prefab assets.",
            McpToolRegistry.ObjectSchema(
                "editorEpoch", McpToolRegistry.StringProperty("Reload marker from query_scene; stale markers are rejected."),
                "targets", McpJson.Object("type", "array", "minItems", 1, "maxItems", 100, "items", McpJson.Object("type", "string")),
                "set", McpJson.Object("type", "object", "minProperties", 1, "maxProperties", 16,
                    "description", "Serialized property paths mapped to primitive, vector, color, enum values or null references. E.g. m_Intensity: 2.5."),
                "label", McpToolRegistry.StringProperty("Undo label; default MCP bulk edit."),
                new[] { "editorEpoch", "targets", "set" }),
            EditImpl);

        private static Dictionary<string, object> EditImpl(Dictionary<string, object> args)
        {
            if (!OpenUnityMcpSettings.IsToolEnabled("unity.set_serialized_property"))
                throw new InvalidOperationException("unity.set_serialized_property is disabled.");
            if (McpJson.AsString(args, "editorEpoch") != UnityMcpWorkflowTools.EditorEpoch)
                throw new ArgumentException("Stale editorEpoch. Query targets again after an editor reload.");
            if (!args.TryGetValue("targets", out var rawTargets) || !(rawTargets is List<object> targets) || targets.Count < 1 || targets.Count > 100)
                throw new ArgumentException("targets must contain 1 to 100 object IDs.");
            if (!args.TryGetValue("set", out var rawSet) || !(rawSet is Dictionary<string, object> changes) || changes.Count < 1 || changes.Count > 16)
                throw new ArgumentException("set must contain 1 to 16 property paths.");

            var staged = new List<SerializedObject>();
            var ids = new HashSet<string>(StringComparer.Ordinal);
            var results = new List<object>();
            var error = string.Empty;
            try
            {
                // Staging SerializedObjects doesn't apply changes. A bad final target must not
                // leave the first target modified during preflight.
                foreach (var rawId in targets)
                {
                    if (!(rawId is string id) || !ids.Add(id)) throw new ArgumentException("Target IDs must be unique strings.");
                    var target = UnityMcpObjectUtility.ResolveObjectById(id);
                    var go = target as GameObject ?? (target as Component)?.gameObject;
                    if (go == null || EditorUtility.IsPersistent(target) || !go.scene.IsValid() || !go.scene.isLoaded)
                        throw new ArgumentException("Target is missing or is not a loaded scene object: " + id);
                    var serialized = new SerializedObject(target);
                    staged.Add(serialized);
                    foreach (var change in changes)
                    {
                        using (var property = serialized.FindProperty(change.Key))
                        {
                            if (property == null || !property.editable) throw new ArgumentException("Missing or read-only property: " + change.Key);
                            // Changing script identity invalidates the staged object itself.
                            if (change.Key == "m_Script") throw new ArgumentException("Bulk edits cannot replace m_Script.");
                            if (!UnityMcpComponentTools.TrySetProperty(property, McpJson.Object("value", change.Value), out var reason))
                                throw new ArgumentException(change.Key + ": " + reason);
                        }
                    }
                }

                Undo.IncrementCurrentGroup();
                var group = Undo.GetCurrentGroup();
                Undo.SetCurrentGroupName(McpJson.AsString(args, "label", "MCP bulk edit"));
                try
                {
                    foreach (var serialized in staged)
                    {
                        var target = serialized.targetObject;
                        var entry = McpJson.Object("objectId", UnityMcpObjectUtility.GetObjectId(target), "isError", false);
                        results.Add(entry);
                        try
                        {
                            entry["changed"] = serialized.ApplyModifiedProperties();
                            PrefabUtility.RecordPrefabInstancePropertyModifications(target);
                            var go = target as GameObject ?? ((Component)target).gameObject;
                            if (!Application.isPlaying) EditorSceneManager.MarkSceneDirty(go.scene);
                            serialized.Update();
                            var values = new Dictionary<string, object>(StringComparer.Ordinal);
                            foreach (var change in changes)
                            {
                                using (var property = serialized.FindProperty(change.Key))
                                    values[change.Key] = UnityMcpComponentTools.DescribeProperty(property)["value"];
                            }
                            entry["values"] = values;
                        }
                        catch (Exception ex)
                        {
                            entry["isError"] = true;
                            entry["error"] = error = ex.Message;
                            break;
                        }
                    }
                }
                finally { Undo.CollapseUndoOperations(group); }
                return McpToolRegistry.ToolJson(McpJson.Object("editorEpoch", UnityMcpWorkflowTools.EditorEpoch,
                    "results", results, "attempted", results.Count, "remaining", targets.Count - results.Count,
                    "complete", error.Length == 0, "atomic", false, "error", error), error.Length > 0);
            }
            finally { foreach (var serialized in staged) serialized.Dispose(); }
        }
    }
}
