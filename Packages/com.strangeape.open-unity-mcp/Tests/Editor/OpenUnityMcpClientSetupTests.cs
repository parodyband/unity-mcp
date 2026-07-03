using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;

namespace StrangeApe.OpenUnityMcp.Tests
{
    public sealed class OpenUnityMcpClientSetupTests
    {
        private const string SidecarScriptPath = "C:/dev/pkg/Server~/open-unity-mcp-sidecar.js";
        private const string ProjectRoot = "C:/dev/project";
        private const int Port = 9123;

        private static OpenUnityMcpClientSetup.SidecarLaunch Launch()
        {
            return OpenUnityMcpClientSetup.SidecarLaunch.Create(SidecarScriptPath, Port, ProjectRoot);
        }

        [Test]
        public void ClaudeCodeConfigAddsSidecarServerAndPreservesExistingServers()
        {
            var directory = CreateTempDirectory();
            try
            {
                File.WriteAllText(
                    Path.Combine(directory, ".mcp.json"),
                    "{\"mcpServers\":{\"filesystem\":{\"command\":\"npx\",\"args\":[\"server\"]}}}");

                var path = OpenUnityMcpClientSetup.InstallClaudeCodeProjectConfig(directory, Launch());
                var root = ReadJsonObject(path);
                var servers = root["mcpServers"] as Dictionary<string, object>;
                var server = servers["open-unity-mcp"] as Dictionary<string, object>;
                var args = server["args"] as List<object>;

                Assert.NotNull(servers);
                Assert.NotNull(servers["filesystem"]);
                Assert.AreEqual("node", server["command"]);
                Assert.IsFalse(server.ContainsKey("type"), "The stdio sidecar entry must not carry an http type.");
                CollectionAssert.AreEqual(
                    new object[]
                    {
                        SidecarScriptPath,
                        "--port", "9123",
                        "--project", ProjectRoot
                    },
                    args);
            }
            finally
            {
                DeleteTempDirectory(directory);
            }
        }

        [Test]
        public void ClaudeCodeConfigKeepsNamedHttpFallbackEntry()
        {
            var directory = CreateTempDirectory();
            try
            {
                var path = OpenUnityMcpClientSetup.InstallClaudeCodeProjectConfig(directory, Launch());
                var root = ReadJsonObject(path);
                var servers = root["mcpServers"] as Dictionary<string, object>;
                var fallback = servers["open-unity-mcp-http"] as Dictionary<string, object>;

                Assert.NotNull(fallback, "A named HTTP fallback entry should be written alongside the sidecar.");
                Assert.AreEqual("http", fallback["type"]);
                Assert.AreEqual("http://127.0.0.1:9123/mcp", fallback["url"]);
            }
            finally
            {
                DeleteTempDirectory(directory);
            }
        }

        [Test]
        public void ClaudeDesktopConfigUsesSidecarStdioCommand()
        {
            var directory = CreateTempDirectory();
            try
            {
                var path = Path.Combine(directory, "claude_desktop_config.json");
                OpenUnityMcpClientSetup.InstallClaudeDesktopConfig(path, Launch());

                var root = ReadJsonObject(path);
                var servers = root["mcpServers"] as Dictionary<string, object>;
                var server = servers["open-unity-mcp"] as Dictionary<string, object>;
                var args = server["args"] as List<object>;

                Assert.AreEqual("node", server["command"]);
                Assert.IsFalse(server.ContainsKey("type"));
                CollectionAssert.AreEqual(
                    new object[]
                    {
                        SidecarScriptPath,
                        "--port", "9123",
                        "--project", ProjectRoot
                    },
                    args);
            }
            finally
            {
                DeleteTempDirectory(directory);
            }
        }

        [Test]
        public void CodexConfigReplacesOpenUnityMcpSectionWithSidecarCommand()
        {
            var existing = string.Join("\n", new[]
            {
                "[profiles.default]",
                "model = \"gpt-5\"",
                "",
                "[mcp_servers.open-unity-mcp]",
                "url = \"http://127.0.0.1:8080/mcp\"",
                "",
                "[mcp_servers.open-unity-mcp.env]",
                "OLD = \"1\"",
                "",
                "[mcp_servers.docs]",
                "url = \"https://example.test/mcp\"",
            });

            var updated = OpenUnityMcpClientSetup.UpsertCodexConfigText(existing, Launch());

            StringAssert.Contains("[profiles.default]\r\nmodel = \"gpt-5\"", updated);
            StringAssert.Contains("[mcp_servers.docs]\r\nurl = \"https://example.test/mcp\"", updated);
            StringAssert.Contains("[mcp_servers.open-unity-mcp]\r\ncommand = \"node\"", updated);
            StringAssert.Contains(
                "args = [\"" + SidecarScriptPath + "\", \"--port\", \"9123\", \"--project\", \"" + ProjectRoot + "\"]",
                updated);
            Assert.IsFalse(updated.Contains("url = \"http://127.0.0.1:8080/mcp\""), "The stale url entry must be removed.");
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

                Assert.Throws<InvalidOperationException>(() => OpenUnityMcpClientSetup.InstallClaudeDesktopConfig(path, Launch()));
                Assert.AreEqual("{ invalid json", File.ReadAllText(path));
            }
            finally
            {
                DeleteTempDirectory(directory);
            }
        }

        [Test]
        public void SidecarLaunchNormalizesWindowsPathsAndBuildsCommandLine()
        {
            var launch = OpenUnityMcpClientSetup.SidecarLaunch.Create(
                "C:\\dev\\pkg\\Server~\\open-unity-mcp-sidecar.js",
                8080,
                "C:\\dev\\project");

            Assert.AreEqual("node", launch.Command);
            CollectionAssert.AreEqual(
                new[]
                {
                    "C:/dev/pkg/Server~/open-unity-mcp-sidecar.js",
                    "--port", "8080",
                    "--project", "C:/dev/project"
                },
                launch.Args);
            Assert.AreEqual("http://127.0.0.1:8080/mcp", launch.Endpoint);
            StringAssert.Contains("node C:/dev/pkg/Server~/open-unity-mcp-sidecar.js --port 8080 --project C:/dev/project", launch.CommandLine);
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
