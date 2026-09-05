using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace StrangeApe.OpenUnityMcp.Tests
{
    public abstract class UnityWorkflowAbstractComponent : MonoBehaviour { }
    public sealed class UnityWorkflowConcreteComponent : UnityWorkflowAbstractComponent { }

    public sealed class UnityWorkflowTests
    {
        private readonly List<GameObject> _objects = new List<GameObject>();

        [TearDown]
        public void Cleanup()
        {
            foreach (var obj in _objects) if (obj != null) Object.DestroyImmediate(obj);
            _objects.Clear();
        }

        private GameObject Create(string name)
        {
            var obj = new GameObject(name);
            _objects.Add(obj);
            return obj;
        }

        private static Dictionary<string, object> Call(string name, Dictionary<string, object> args)
        {
            return McpToolRegistry.Call(name, args);
        }

        private static Dictionary<string, object> Payload(Dictionary<string, object> result)
        {
            Assert.IsFalse(McpJson.AsBool(result, "isError", false), McpJson.Stringify(result));
            var payload = (Dictionary<string, object>)result["structuredContent"];
            Assert.AreEqual(McpJson.Stringify(payload), ((Dictionary<string, object>)((List<object>)result["content"])[0])["text"]);
            return payload;
        }

        private static Dictionary<string, object> Step(string id, string name, Dictionary<string, object> args)
        {
            return McpJson.Object("id", id, "name", name, "arguments", args);
        }

        [Test]
        public void CompactCatalogDiscoversAndDispatchesHiddenTools()
        {
            var original = OpenUnityMcpSettings.CompactToolList;
            try
            {
                OpenUnityMcpSettings.CompactToolList = true;
                var compact = McpToolRegistry.ListTools();
                Assert.AreEqual(7, ((List<object>)compact["tools"]).Count);
                StringAssert.DoesNotContain("unity.create_game_objects", McpJson.Stringify(compact));
                var schema = Payload(Call("unity.discover_tools", McpJson.Object("name", "unity.create_game_objects")));
                Assert.IsTrue(schema.ContainsKey("inputSchema"));
                Payload(Call("unity.call_tool", McpJson.Object("name", "unity.get_open_scenes", "arguments", McpJson.Object())));
                OpenUnityMcpSettings.CompactToolList = false;
                var full = McpToolRegistry.ListTools();
                Assert.Less(McpJson.Stringify(compact).Length, McpJson.Stringify(full).Length / 2);
                TestContext.WriteLine("Catalog characters: compact=" + McpJson.Stringify(compact).Length + ", full=" + McpJson.Stringify(full).Length);
            }
            finally { OpenUnityMcpSettings.CompactToolList = original; }
        }

        [Test]
        public void BatchUsesComponentResultReferencesAndProjectsReadback()
        {
            var obj = Create("OpenUnityMcpWorkflow");
            var id = UnityMcpObjectUtility.GetObjectId(obj);
            var read = Step("read", "unity.get_serialized_properties", McpJson.Object(
                "objectId", McpJson.Object("$ref", "add/component/objectId"), "propertyPaths", McpJson.Array("m_Intensity")));
            read["select"] = McpJson.Array("/properties/0/value");
            var result = Payload(Call("unity.batch", McpJson.Object("operations", McpJson.Array(
                Step("add", "unity.add_component", McpJson.Object("objectId", id, "componentType", "UnityEngine.Light")),
                Step("set", "unity.set_serialized_property", McpJson.Object("objectId", McpJson.Object("$ref", "add/component/objectId"), "propertyPath", "m_Intensity", "value", 3.5)),
                read))));
            Assert.AreEqual(3, result["executed"]);
            Assert.AreEqual(3.5f, obj.GetComponent<Light>().intensity);
            var entry = (Dictionary<string, object>)((List<object>)result["results"])[2];
            Assert.AreEqual(3.5f, ((Dictionary<string, object>)entry["result"])["/properties/0/value"]);
        }

        [Test]
        public void BatchReportsPartialFailureAndDoesNotRunLaterMutations()
        {
            var obj = Create("OpenUnityMcpPartial");
            var id = UnityMcpObjectUtility.GetObjectId(obj.transform);
            var result = Call("unity.batch", McpJson.Object("operations", McpJson.Array(
                Step("first", "unity.set_transform", McpJson.Object("objectId", id, "position", McpJson.Array(1, 2, 3))),
                Step("bad", "unity.set_serialized_property", McpJson.Object("objectId", id, "propertyPath", "doesNotExist", "value", 3)),
                Step("last", "unity.set_transform", McpJson.Object("objectId", id, "position", McpJson.Array(9, 9, 9))))));
            Assert.IsTrue((bool)result["isError"]);
            var payload = (Dictionary<string, object>)result["structuredContent"];
            Assert.AreEqual(2, payload["executed"]);
            Assert.AreEqual(1, payload["remaining"]);
            Assert.AreEqual(new Vector3(1, 2, 3), obj.transform.position);
        }

        [TestCase("unity.execute_csharp")]
        [TestCase("unity.refresh_assets")]
        [TestCase("unity.build_player")]
        [TestCase("unity.batch")]
        [TestCase("unity.call_tool")]
        public void BatchRejectsUnsafePlanBeforeFirstMutation(string tool)
        {
            var obj = Create("OpenUnityMcpPreflight");
            var result = Call("unity.batch", McpJson.Object("operations", McpJson.Array(
                Step("first", "unity.set_transform", McpJson.Object("objectId", UnityMcpObjectUtility.GetObjectId(obj), "position", McpJson.Array(1, 2, 3))),
                Step("bad", tool, McpJson.Object()))));
            Assert.IsTrue((bool)result["isError"]);
            Assert.AreEqual(Vector3.zero, obj.transform.position);
        }

        [Test]
        public void DisabledToolsCannotBeDiscoveredOrInvokedIndirectly()
        {
            var enabled = OpenUnityMcpSettings.IsToolEnabled("unity.get_open_scenes");
            try
            {
                OpenUnityMcpSettings.SetToolEnabled("unity.get_open_scenes", false);
                Assert.IsTrue((bool)Call("unity.discover_tools", McpJson.Object("name", "unity.get_open_scenes"))["isError"]);
                Assert.IsTrue((bool)Call("unity.call_tool", McpJson.Object("name", "unity.get_open_scenes", "arguments", McpJson.Object()))["isError"]);
                Assert.IsTrue((bool)Call("unity.batch", McpJson.Object("operations", McpJson.Array(Step("read", "unity.get_open_scenes", McpJson.Object()))))["isError"]);
            }
            finally { OpenUnityMcpSettings.SetToolEnabled("unity.get_open_scenes", enabled); }
        }

        [Test]
        public void BatchRejectsForwardReferencesAndExcessiveOperations()
        {
            Assert.IsTrue((bool)Call("unity.batch", McpJson.Object("operations", McpJson.Array(
                Step("read", "unity.get_components", McpJson.Object("objectId", McpJson.Object("$ref", "future/id"))))))["isError"]);
            var operations = new List<object>();
            for (var i = 0; i < 17; i++) operations.Add(Step("r" + i, "unity.get_open_scenes", McpJson.Object()));
            Assert.IsTrue((bool)Call("unity.batch", McpJson.Object("operations", operations))["isError"]);
        }

        [Test]
        public void QueryFiltersComponentsAndPaginatesInactiveChildren()
        {
            var root = Create("OpenUnityMcpQueryRoot");
            var first = Create("OpenUnityMcpQueryA");
            first.transform.SetParent(root.transform);
            first.AddComponent<Light>();
            var second = Create("OpenUnityMcpQueryB");
            second.transform.SetParent(root.transform);
            second.AddComponent<Light>();
            second.SetActive(false);
            var args = McpJson.Object("rootObjectId", UnityMcpObjectUtility.GetObjectId(root), "componentType", "UnityEngine.Light", "limit", 1);
            var page = Payload(Call("unity.query_scene", args));
            Assert.AreEqual(true, page["hasMore"]);
            args["offset"] = page["nextOffset"];
            var last = Payload(Call("unity.query_scene", args));
            Assert.AreEqual(false, last["hasMore"]);
            Assert.AreEqual("OpenUnityMcpQueryB", ((Dictionary<string, object>)((List<object>)last["objects"])[0])["name"]);
            args["offset"] = 0;
            args["includeInactive"] = false;
            Assert.AreEqual(false, Payload(Call("unity.query_scene", args))["hasMore"]);
        }

        [Test]
        public void PropertyReadsSupportExactPathsFilteringAndOneRemainingItem()
        {
            var obj = Create("OpenUnityMcpProperties");
            var id = UnityMcpObjectUtility.GetObjectId(obj.transform);
            var args = McpJson.Object("objectId", id, "includeChildren", false);
            var all = Payload(Call("unity.get_serialized_properties", args));
            var count = (int)all["count"];
            Assert.Greater(count, 1);
            args["limit"] = count - 1;
            var page = Payload(Call("unity.get_serialized_properties", args));
            Assert.AreEqual(true, page["truncated"]);
            args["offset"] = page["nextOffset"];
            var last = Payload(Call("unity.get_serialized_properties", args));
            Assert.AreEqual(1, last["count"]);
            Assert.AreEqual(false, last["truncated"]);
            var exact = Payload(Call("unity.get_serialized_properties", McpJson.Object("objectId", id, "propertyPaths", McpJson.Array("m_LocalPosition.x", "absent"))));
            Assert.AreEqual(1, exact["count"]);
            CollectionAssert.AreEqual(new[] { "absent" }, (List<object>)exact["missingPaths"]);
            var filtered = Payload(Call("unity.get_serialized_properties", McpJson.Object("objectId", id, "filter", "localposition")));
            Assert.Greater((int)filtered["count"], 0);
            foreach (Dictionary<string, object> property in (List<object>)filtered["properties"])
                StringAssert.Contains("localposition", ((string)property["path"]).ToLowerInvariant());
        }

        [Test]
        public void QueryAcceptsAbstractComponentTypesButCreationDoesNot()
        {
            var root = Create("OpenUnityMcpAbstractQuery");
            var collider = root.AddComponent<UnityWorkflowConcreteComponent>();
            var result = Payload(Call("unity.query_scene", McpJson.Object("rootObjectId", UnityMcpObjectUtility.GetObjectId(root), "componentType", typeof(UnityWorkflowAbstractComponent).FullName)));
            var entry = (Dictionary<string, object>)((List<object>)result["objects"])[0];
            var component = (Dictionary<string, object>)((List<object>)entry["components"])[0];
            Assert.AreEqual(UnityMcpObjectUtility.GetObjectId(collider), component["objectId"]);
            Assert.Throws<System.ArgumentException>(() => UnityMcpObjectUtility.ResolveType(typeof(UnityWorkflowAbstractComponent).FullName, typeof(Component)));
        }

        [Test]
        public void ProjectionFailurePreservesSuccessfulMutationAndOriginalReferences()
        {
            var obj = Create("OpenUnityMcpProjection");
            var add = Step("add", "unity.add_component", McpJson.Object("objectId", UnityMcpObjectUtility.GetObjectId(obj), "componentType", "UnityEngine.Light"));
            add["select"] = McpJson.Array("/absent");
            var result = Payload(Call("unity.batch", McpJson.Object("operations", McpJson.Array(add,
                Step("set", "unity.set_serialized_property", McpJson.Object("objectId", McpJson.Object("$ref", "add/component/objectId"), "propertyPath", "m_Intensity", "value", 8))))));
            var entry = (Dictionary<string, object>)((List<object>)result["results"])[0];
            Assert.IsFalse((bool)entry["isError"]);
            Assert.IsTrue(entry.ContainsKey("projectionError"));
            Assert.AreEqual(8, obj.GetComponent<Light>().intensity);
        }

        [Test]
        public void MissingResultPathStopsBatchWithoutRepeatingEarlierMutation()
        {
            var obj = Create("OpenUnityMcpReferenceFailure");
            var id = UnityMcpObjectUtility.GetObjectId(obj);
            var result = Call("unity.batch", McpJson.Object("operations", McpJson.Array(
                Step("move", "unity.set_transform", McpJson.Object("objectId", id, "position", McpJson.Array(2, 3, 4))),
                Step("read", "unity.get_components", McpJson.Object("objectId", McpJson.Object("$ref", "move/absent"))),
                Step("later", "unity.set_transform", McpJson.Object("objectId", id, "position", McpJson.Array(9, 9, 9))))));
            Assert.IsTrue((bool)result["isError"]);
            var payload = (Dictionary<string, object>)result["structuredContent"];
            Assert.AreEqual(1, payload["executed"]);
            Assert.AreEqual(2, payload["attempted"]);
            Assert.AreEqual(1, payload["remaining"]);
            Assert.AreEqual(new Vector3(2, 3, 4), obj.transform.position);
        }

        [TestCase(true)]
        [TestCase(false)]
        public void MalformedReferencesAndDuplicateIdsRejectEntirePlan(bool duplicate)
        {
            var obj = Create("OpenUnityMcpInvalidPlan");
            var id = UnityMcpObjectUtility.GetObjectId(obj);
            var bad = duplicate ? Step("move", "unity.get_components", McpJson.Object("objectId", id)) :
                Step("read", "unity.get_components", McpJson.Object("objectId", McpJson.Object("$ref", "move/object/objectId", "extra", true)));
            var result = Call("unity.batch", McpJson.Object("operations", McpJson.Array(
                Step("move", "unity.set_transform", McpJson.Object("objectId", id, "position", McpJson.Array(2, 3, 4))), bad)));
            Assert.IsTrue((bool)result["isError"]);
            Assert.AreEqual(Vector3.zero, obj.transform.position);
        }

        [Test]
        public void OutputBudgetReportsExecutedMutationAndStopsBeforeNextStep()
        {
            var obj = Create("OpenUnityMcpOutputBudget");
            var id = UnityMcpObjectUtility.GetObjectId(obj);
            var result = Call("unity.batch", McpJson.Object("operations", McpJson.Array(
                Step("rename", "unity.set_serialized_property", McpJson.Object("objectId", id, "propertyPath", "m_Name", "value", new string('x', 270000))),
                Step("later", "unity.set_transform", McpJson.Object("objectId", id, "position", McpJson.Array(9, 9, 9))))));
            Assert.IsTrue((bool)result["isError"]);
            var payload = (Dictionary<string, object>)result["structuredContent"];
            Assert.AreEqual("outputBudget", payload["stoppedReason"]);
            var entry = (Dictionary<string, object>)((List<object>)payload["results"])[0];
            Assert.IsTrue((bool)entry["outputOmitted"]);
            Assert.IsFalse((bool)entry["isError"]);
            Assert.AreEqual(270000, obj.name.Length);
            Assert.AreEqual(Vector3.zero, obj.transform.position);
        }

        [Test]
        public void MissingPrimitiveValueDoesNotOverwriteExistingState()
        {
            var obj = Create("OpenUnityMcpMissingValue");
            obj.transform.localPosition = new Vector3(7, 0, 0);
            var result = Call("unity.set_serialized_property", McpJson.Object("objectId", UnityMcpObjectUtility.GetObjectId(obj.transform), "propertyPath", "m_LocalPosition.x"));
            Assert.IsTrue((bool)result["isError"]);
            Assert.AreEqual(7, obj.transform.localPosition.x);
        }
    }
}
