using System;
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

        [Test]
        public void ParseRejectsExcessiveNestingWithFormatException()
        {
            // Without the depth guard this input overflows the parser stack and kills the editor.
            var json = new string('[', 20000) + new string(']', 20000);

            Assert.Throws<FormatException>(() => McpJson.Parse(json));
        }

        [Test]
        public void ParseAcceptsNestingWithinDepthLimit()
        {
            var json = new string('[', 32) + "1" + new string(']', 32);

            Assert.NotNull(McpJson.Parse(json));
        }

        [Test]
        public void StringifyEmitsNonFiniteNumbersAsStrings()
        {
            var json = McpJson.Stringify(McpJson.Object(
                "nan", double.NaN,
                "positive", double.PositiveInfinity,
                "negative", float.NegativeInfinity));

            Assert.AreEqual("{\"nan\":\"NaN\",\"positive\":\"Infinity\",\"negative\":\"-Infinity\"}", json);
        }

        [Test]
        public void StringifyEmitsFiniteNumbersBare()
        {
            var json = McpJson.Stringify(McpJson.Object("value", 1.5d));

            Assert.AreEqual("{\"value\":1.5}", json);
        }

        [Test]
        public void ParseIntegerBeyondLongRangeFallsBackToDouble()
        {
            var parsed = McpJson.Parse("{\"big\":99999999999999999999}") as Dictionary<string, object>;

            Assert.NotNull(parsed);
            Assert.IsInstanceOf<double>(parsed["big"]);
        }

        [Test]
        public void ParseRejectsNumbersBeyondDoubleRange()
        {
            Assert.Throws<FormatException>(() => McpJson.Parse("[1e999]"));
        }
    }
}

