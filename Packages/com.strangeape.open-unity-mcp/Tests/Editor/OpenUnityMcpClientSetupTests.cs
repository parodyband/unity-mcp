using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;

namespace StrangeApe.OpenUnityMcp.Tests
{
    public sealed class OpenUnityMcpClientSetupTests
    {
        [Test]
        public void ClaudeCodeConfigAddsHttpServerAndPreservesExistingServers()
        {
            var directory = CreateTempDirectory();
            try
            {
                File.WriteAllText(
                    Path.Combine(directory, ".mcp.json"),
                    "{\"mcpServers\":{\"filesystem\":{\"command\":\"npx\",\"args\":[\"server\"]}}}");

                var path = OpenUnityMcpClientSetup.InstallClaudeCodeProjectConfig(directory, "http://127.0.0.1:9123/mcp");
                var root = ReadJsonObject(path);
                var servers = root["mcpServers"] as Dictionary<string, object>;
                var server = servers["open-unity-mcp"] as Dictionary<string, object>;

                Assert.NotNull(servers);
                Assert.NotNull(servers["filesystem"]);
                Assert.AreEqual("http", server["type"]);
                Assert.AreEqual("http://127.0.0.1:9123/mcp", server["url"]);
            }
            finally
            {
                DeleteTempDirectory(directory);
            }
        }

        [Test]
        public void ClaudeDesktopConfigUsesStdioBridgeForHttpEndpoint()
        {
            var directory = CreateTempDirectory();
            try
            {
                var path = Path.Combine(directory, "claude_desktop_config.json");
                OpenUnityMcpClientSetup.InstallClaudeDesktopConfig(path, "http://127.0.0.1:8080/mcp");

                var root = ReadJsonObject(path);
                var servers = root["mcpServers"] as Dictionary<string, object>;
                var server = servers["open-unity-mcp"] as Dictionary<string, object>;
                var args = server["args"] as List<object>;

                Assert.AreEqual("npx", server["command"]);
                Assert.IsFalse(server.ContainsKey("type"));
                CollectionAssert.AreEqual(
                    new object[] { "-y", "mcp-remote@latest", "--http", "http://127.0.0.1:8080/mcp", "--allow-http" },
                    args);
            }
            finally
            {
                DeleteTempDirectory(directory);
            }
        }

        [Test]
        public void CodexConfigReplacesOpenUnityMcpSectionOnly()
        {
            var existing = string.Join("\n", new[]
            {
                "[profiles.default]",
                "model = \"gpt-5\"",
                "",
                "[mcp_servers.open-unity-mcp]",
                "command = \"old\"",
                "",
                "[mcp_servers.open-unity-mcp.env]",
                "OLD = \"1\"",
                "",
                "[mcp_servers.docs]",
                "url = \"https://example.test/mcp\"",
            });

            var updated = OpenUnityMcpClientSetup.UpsertCodexConfigText(existing, "http://127.0.0.1:8080/mcp");

            StringAssert.Contains("[profiles.default]\r\nmodel = \"gpt-5\"", updated);
            StringAssert.Contains("[mcp_servers.docs]\r\nurl = \"https://example.test/mcp\"", updated);
            StringAssert.Contains("[mcp_servers.open-unity-mcp]\r\nurl = \"http://127.0.0.1:8080/mcp\"", updated);
            Assert.IsFalse(updated.Contains("command = \"old\""));
            Assert.IsFalse(updated.Contains("[mcp_servers.open-unity-mcp.env]"));
        }

        [Test]
        public void InvalidJsonConfigIsNotOverwritten()
        {
            var directory = CreateTempDirectory();
            try
            {
                var path = Path.Combine(directory, "claude_desktop_config.json");
                File.WriteAllText(path, "{ invalid json");

                Assert.Throws<InvalidOperationException>(() => OpenUnityMcpClientSetup.InstallClaudeDesktopConfig(path, "http://127.0.0.1:8080/mcp"));
                Assert.AreEqual("{ invalid json", File.ReadAllText(path));
            }
            finally
            {
                DeleteTempDirectory(directory);
            }
        }

        private static Dictionary<string, object> ReadJsonObject(string path)
        {
            return McpJson.Parse(File.ReadAllText(path)) as Dictionary<string, object>;
        }

        private static string CreateTempDirectory()
        {
            var directory = Path.Combine(Path.GetTempPath(), "OpenUnityMcpTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            return directory;
        }

        private static void DeleteTempDirectory(string directory)
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, true);
            }
        }
    }
}
