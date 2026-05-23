using System.Collections.Generic;
using NUnit.Framework;

namespace StrangeApe.OpenUnityMcp.Tests
{
    public sealed class McpJsonTests
    {
        [Test]
        public void ParseObjectReadsNestedValues()
        {
            var parsed = McpJson.Parse("{\"method\":\"tools/call\",\"params\":{\"name\":\"unity.get_project_info\",\"arguments\":{\"limit\":5}}}") as Dictionary<string, object>;

            Assert.NotNull(parsed);
            Assert.AreEqual("tools/call", parsed["method"]);
            var parameters = parsed["params"] as Dictionary<string, object>;
            Assert.NotNull(parameters);
            Assert.AreEqual("unity.get_project_info", parameters["name"]);
        }

        [Test]
        public void StringifyEscapesStrings()
        {
            var json = McpJson.Stringify(McpJson.Object("text", "line1\n\"line2\""));

            Assert.AreEqual("{\"text\":\"line1\\n\\\"line2\\\"\"}", json);
        }
    }
}

