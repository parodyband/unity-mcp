using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace StrangeApe.OpenUnityMcp
{
    internal static class UnityMcpWorkflowTools
    {
        public static readonly McpTool DiscoverTools = new McpTool(
            "unity.discover_tools",
            "Find enabled tools by name/description. Returns summaries by default; pass name for one complete input schema. Call discovered tools through unity.call_tool or batchable tools through unity.batch.",
            McpToolRegistry.ObjectSchema(
                "query", McpToolRegistry.StringProperty("Case-insensitive name/description substring; empty lists all summaries."),
                "name", McpToolRegistry.StringProperty("Exact tool name to retrieve its input schema.")),
            DiscoverImpl);

        public static readonly McpTool CallTool = new McpTool(
            "unity.call_tool",
            "Execute an enabled tool discovered with unity.discover_tools. May mutate assets, run unrestricted C#, compile, or build; inspect the selected tool schema first. Cannot invoke call_tool or batch recursively.",
            McpToolRegistry.ObjectSchema(
                "name", McpToolRegistry.StringProperty("Exact discovered tool name."),
                "arguments", McpJson.Object("type", "object", "description", "Arguments matching the discovered input schema."),
                new[] { "name", "arguments" }),
            CallImpl, runOnCallerThread: true);

        public static readonly McpTool Batch = new McpTool(
            "unity.batch",
            "Run up to 16 ordered, batchable tool calls in one main-thread turn. Arguments may reference earlier results using {\"$ref\":\"stepId/path/to/value\"}. Stops on error; earlier changes remain (NOT atomic, never blindly retry). No compilation, builds, C#, scene lifecycle, or recursive calls. Optional select returns only chosen JSON pointers per step.",
            McpToolRegistry.ObjectSchema(
                "operations", McpJson.Object("type", "array", "minItems", 1, "maxItems", 16,
                    "items", McpToolRegistry.ObjectSchema(
                        "id", McpToolRegistry.StringProperty("Unique step ID without slashes."),
                        "name", McpToolRegistry.StringProperty("Tool whose discovery metadata says batchable=true."),
                        "arguments", McpJson.Object("type", "object"),
                        "select", McpJson.Object("type", "array", "maxItems", 32, "items", McpJson.Object("type", "string"),
                            "description", "JSON pointers into the step payload, e.g. /created/objectId. Empty array suppresses output."),
                        new[] { "id", "name", "arguments" })),
                new[] { "operations" }),
            BatchImpl);

        public static readonly McpTool QueryScene = new McpTool(
            "unity.query_scene",
            "Find loaded scene GameObjects by name and/or component type without dumping the hierarchy. Returns compact object and matching component IDs, with offset pagination. Pages reflect live scene state; restart pagination after mutations.",
            McpToolRegistry.ObjectSchema(
                "name", McpToolRegistry.StringProperty("Case-insensitive name substring."),
                "componentType", McpToolRegistry.StringProperty("Optional Component type, e.g. UnityEngine.Light."),
                "scenePath", McpToolRegistry.StringProperty("Optional exact loaded scene path."),
                "rootObjectId", McpToolRegistry.StringProperty("Optional root GameObject/Component; searches its subtree including itself."),
                "includeInactive", McpToolRegistry.BooleanProperty("Include inactive objects; default true."),
                "offset", McpToolRegistry.IntegerProperty("Matching objects to skip; default 0.", 0, 1000000),
                "limit", McpToolRegistry.IntegerProperty("Page size; default 25.", 1, 200)),
            QueryImpl);

        private static Dictionary<string, object> DiscoverImpl(Dictionary<string, object> args)
        {
            var name = McpJson.AsString(args, "name");
            var query = McpJson.AsString(args, "query", string.Empty);
            var results = new List<object>();
            foreach (var tool in McpToolRegistry.AllTools)
            {
                if (!OpenUnityMcpSettings.IsToolEnabled(tool.Name)) continue;
                if (!string.IsNullOrEmpty(name))
                {
                    if (tool.Name == name) return McpToolRegistry.ToolJson(McpToolRegistry.DescribeTool(tool));
                    continue;
                }
                if ((tool.Name + " " + tool.Description).IndexOf(query, StringComparison.OrdinalIgnoreCase) < 0) continue;
                results.Add(McpJson.Object("name", tool.Name, "description", tool.Description,
                    "readOnly", McpToolRegistry.IsReadOnly(tool.Name), "batchable", McpToolRegistry.CanBatch(tool.Name)));
            }
            if (!string.IsNullOrEmpty(name)) return McpToolRegistry.ToolText("Unknown or disabled tool: " + name, true);
            return McpToolRegistry.ToolJson(McpJson.Object("tools", results, "count", results.Count));
        }

        private static Dictionary<string, object> CallImpl(Dictionary<string, object> args)
        {
            var name = McpJson.AsString(args, "name");
            if (name == "unity.call_tool" || name == "unity.batch") throw new ArgumentException("Call workflow tools directly; recursive dispatch is not supported.");
            if (!args.TryGetValue("arguments", out var raw) || !(raw is Dictionary<string, object> arguments))
                throw new ArgumentException("arguments must be an object.");
            return McpToolRegistry.Call(name, arguments);
        }

        private static Dictionary<string, object> BatchImpl(Dictionary<string, object> args)
        {
            if (!args.TryGetValue("operations", out var raw) || !(raw is List<object> operations) || operations.Count < 1 || operations.Count > 16)
                throw new ArgumentException("operations must contain 1 to 16 steps.");

            // Validate the whole plan before the first mutation. Runtime references and Unity
            // validation can still fail, so successful steps are always reported individually.
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var item in operations)
            {
                if (!(item is Dictionary<string, object> step)) throw new ArgumentException("Each step must be an object.");
                var id = McpJson.AsString(step, "id");
                var name = McpJson.AsString(step, "name");
                if (string.IsNullOrEmpty(id) || id.Length > 64 || id.Contains("/") || ids.Contains(id)) throw new ArgumentException("Step IDs must be unique, 1-64 characters, and contain no slash.");
                if (!McpToolRegistry.CanBatch(name) || !OpenUnityMcpSettings.IsToolEnabled(name)) throw new ArgumentException("Tool is disabled or not batchable: " + name);
                if (!step.TryGetValue("arguments", out var arguments) || !(arguments is Dictionary<string, object>)) throw new ArgumentException("Step arguments must be an object.");
                ValidateReferences(arguments, ids);
                if (step.TryGetValue("select", out var selection))
                {
                    if (!(selection is List<object> pointers) || pointers.Count > 32) throw new ArgumentException("select must be an array of at most 32 JSON pointers.");
                    foreach (var pointer in pointers)
                        if (!(pointer is string text) || (text.Length > 0 && !text.StartsWith("/", StringComparison.Ordinal))) throw new ArgumentException("select entries must be JSON pointers.");
                }
                ids.Add(id);
            }

            var results = new List<object>();
            var values = new Dictionary<string, object>(StringComparer.Ordinal);
            var clock = Stopwatch.StartNew();
            var failed = false;
            var outputChars = 0;
            var stoppedReason = string.Empty;
            var executed = 0;
            foreach (Dictionary<string, object> step in operations)
            {
                if (clock.Elapsed.TotalSeconds >= 10) { stoppedReason = "timeBudget"; break; }
                var id = McpJson.AsString(step, "id");
                Dictionary<string, object> result;
                try
                {
                    var arguments = (Dictionary<string, object>)ResolveReferences(step["arguments"], values);
                    executed++;
                    result = McpToolRegistry.Call(McpJson.AsString(step, "name"), arguments);
                }
                catch (Exception ex) { result = McpToolRegistry.ToolText(ex.Message, true); }
                failed = McpJson.AsBool(result, "isError", false);
                object payload = result.TryGetValue("structuredContent", out var structured) ? structured : result;
                var entry = McpJson.Object("id", id, "isError", failed);
                if (!failed)
                {
                    values[id] = payload;
                    if (step.TryGetValue("select", out var selection))
                    {
                        var projected = new Dictionary<string, object>(StringComparer.Ordinal);
                        try
                        {
                            foreach (string pointer in (List<object>)selection) projected[pointer] = ReadPointer(payload, pointer);
                            payload = projected;
                        }
                        catch (Exception ex)
                        {
                            // The operation already succeeded; a projection error must not imply
                            // that it is safe to repeat the mutation.
                            entry["projectionError"] = ex.Message;
                        }
                    }
                }
                var size = McpJson.Stringify(payload).Length;
                if (outputChars + size > 262144)
                {
                    entry["outputOmitted"] = true;
                    entry["hint"] = "Operation ran; inspect current state or use select to request smaller output. Do not repeat mutations.";
                    stoppedReason = "outputBudget";
                }
                else { entry["result"] = payload; outputChars += size; }
                results.Add(entry);
                if (failed) { stoppedReason = "error"; break; }
                if (stoppedReason.Length > 0) break;
            }
            return McpToolRegistry.ToolJson(McpJson.Object("results", results, "executed", executed, "attempted", results.Count,
                "remaining", operations.Count - results.Count, "complete", results.Count == operations.Count && !failed,
                "stoppedReason", stoppedReason, "atomic", false), failed || results.Count < operations.Count);
        }

        private static void ValidateReferences(object value, HashSet<string> ids)
        {
            if (value is Dictionary<string, object> map)
            {
                if (map.TryGetValue("$ref", out var reference))
                {
                    if (map.Count != 1 || !(reference is string text) || !ids.Contains(text.Split('/')[0]))
                        throw new ArgumentException("$ref must be a single-key object referencing an earlier step.");
                }
                else foreach (var child in map.Values) ValidateReferences(child, ids);
            }
            else if (value is List<object> list) foreach (var child in list) ValidateReferences(child, ids);
        }

        private static object ResolveReferences(object value, Dictionary<string, object> values)
        {
            if (value is Dictionary<string, object> map)
            {
                if (map.TryGetValue("$ref", out var reference))
                {
                    var text = (string)reference;
                    var slash = text.IndexOf('/');
                    return ReadPointer(values[slash < 0 ? text : text.Substring(0, slash)], slash < 0 ? string.Empty : text.Substring(slash));
                }
                var resolved = new Dictionary<string, object>(StringComparer.Ordinal);
                foreach (var pair in map) resolved[pair.Key] = ResolveReferences(pair.Value, values);
                return resolved;
            }
            if (value is List<object> list)
            {
                var resolved = new List<object>();
                foreach (var child in list) resolved.Add(ResolveReferences(child, values));
                return resolved;
            }
            return value;
        }

        private static object ReadPointer(object value, string pointer)
        {
            if (pointer.Length == 0) return value;
            foreach (var part in pointer.Substring(1).Split('/'))
            {
                var key = part.Replace("~1", "/").Replace("~0", "~");
                if (value is Dictionary<string, object> map && map.TryGetValue(key, out var child)) value = child;
                else if (value is List<object> list && int.TryParse(key, NumberStyles.None, CultureInfo.InvariantCulture, out var index) && index < list.Count) value = list[index];
                else throw new ArgumentException("Result pointer not found: " + pointer);
            }
            return value;
        }

        private static Dictionary<string, object> QueryImpl(Dictionary<string, object> args)
        {
            var name = McpJson.AsString(args, "name", string.Empty);
            var typeName = McpJson.AsString(args, "componentType");
            var type = string.IsNullOrEmpty(typeName) ? null : UnityMcpObjectUtility.ResolveType(typeName, typeof(Component), allowAbstract: true);
            var scenePath = McpJson.AsString(args, "scenePath");
            var rootId = McpJson.AsString(args, "rootObjectId");
            var limit = Math.Max(1, Math.Min(200, McpJson.AsInt(args, "limit", 25)));
            var offset = Math.Max(0, Math.Min(1000000, McpJson.AsInt(args, "offset", 0)));
            var includeInactive = McpJson.AsBool(args, "includeInactive", true);
            var pending = new Stack<Transform>();
            if (!string.IsNullOrEmpty(rootId))
            {
                var root = UnityMcpObjectUtility.ResolveGameObject(rootId, null);
                if (root == null || !root.scene.IsValid() || !root.scene.isLoaded) throw new ArgumentException("rootObjectId must identify a loaded scene object.");
                pending.Push(root.transform);
            }
            else
            {
                for (var i = SceneManager.sceneCount - 1; i >= 0; i--)
                {
                    var scene = SceneManager.GetSceneAt(i);
                    if (!scene.isLoaded || (!string.IsNullOrEmpty(scenePath) && scene.path != scenePath)) continue;
                    var roots = scene.GetRootGameObjects();
                    for (var j = roots.Length - 1; j >= 0; j--) pending.Push(roots[j].transform);
                }
            }
            var results = new List<object>();
            var skipped = 0;
            var hasMore = false;
            while (pending.Count > 0)
            {
                var transform = pending.Pop();
                var go = transform.gameObject;
                for (var i = transform.childCount - 1; i >= 0; i--) pending.Push(transform.GetChild(i));
                if ((!includeInactive && !go.activeInHierarchy) || (!string.IsNullOrEmpty(scenePath) && go.scene.path != scenePath) || go.name.IndexOf(name, StringComparison.OrdinalIgnoreCase) < 0) continue;
                var components = type == null ? null : go.GetComponents(type);
                if (components != null && components.Length == 0) continue;
                if (skipped++ < offset) continue;
                if (results.Count == limit) { hasMore = true; break; }
                var matches = new List<object>();
                if (components != null) foreach (var component in components) matches.Add(UnityMcpComponentTools.DescribeComponent(component));
                results.Add(McpJson.Object("objectId", UnityMcpObjectUtility.GetObjectId(go), "name", go.name,
                    "scenePath", go.scene.path, "hierarchyPath", UnityMcpSceneTools.GetHierarchyPath(transform),
                    "activeSelf", go.activeSelf, "components", matches));
            }
            return McpToolRegistry.ToolJson(McpJson.Object("objects", results, "count", results.Count,
                "hasMore", hasMore, "nextOffset", hasMore ? (object)(offset + results.Count) : null));
        }
    }
}
