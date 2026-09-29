using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;

namespace StrangeApe.OpenUnityMcp.Tests
{
    public sealed class OpenUnityMcpSidecarInstallerTests
    {
        private const string StaleScript = "C:/proj/Library/PackageCache/com.strangeape.open-unity-mcp@0123456789ab/Server~/open-unity-mcp-sidecar.js";

        private string _directory;
        private string _source;
        private string _target;
        private string _stableScript;
        private string _projectRoot;

        [SetUp]
        public void SetUp()
        {
            _directory = Path.Combine(Path.GetTempPath(), "OpenUnityMcpTests", Guid.NewGuid().ToString("N"));
            _source = Path.Combine(_directory, "Server~");
            _target = Path.Combine(_directory, "stable", "sidecar");
            _projectRoot = Path.Combine(_directory, "project");
            Directory.CreateDirectory(_source);
            Directory.CreateDirectory(Path.Combine(_source, "test"));
            Directory.CreateDirectory(_projectRoot);
            File.WriteAllText(Path.Combine(_source, "open-unity-mcp-sidecar.js"), "// sidecar v1");
            File.WriteAllText(Path.Combine(_source, "unity-session.js"), "// session v1");
            File.WriteAllText(Path.Combine(_source, "README.md"), "docs");
            File.WriteAllText(Path.Combine(_source, "test", "sidecar-e2e.mjs"), "test");
            _stableScript = Path.Combine(_target, "open-unity-mcp-sidecar.js").Replace('\\', '/');
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_directory))
            {
                Directory.Delete(_directory, true);
            }
        }

        [Test]
        public void InstallCopiesTopLevelScriptsOnly()
        {
            var outcome = OpenUnityMcpSidecarInstaller.Install(_source, "0.17.0", _target);

            Assert.AreEqual(OpenUnityMcpSidecarInstaller.InstallOutcome.Installed, outcome);
            Assert.AreEqual("// sidecar v1", File.ReadAllText(Path.Combine(_target, "open-unity-mcp-sidecar.js")));
            Assert.AreEqual("// session v1", File.ReadAllText(Path.Combine(_target, "unity-session.js")));
            Assert.IsFalse(File.Exists(Path.Combine(_target, "README.md")));
            Assert.IsFalse(Directory.Exists(Path.Combine(_target, "test")));
        }

        [Test]
        public void InstallIsUpToDateWhenNothingChanged()
        {
            OpenUnityMcpSidecarInstaller.Install(_source, "0.17.0", _target);

            Assert.AreEqual(OpenUnityMcpSidecarInstaller.InstallOutcome.UpToDate, OpenUnityMcpSidecarInstaller.Install(_source, "0.17.0", _target));
        }

        [Test]
        public void InstallRefreshesSameVersionWhenContentDiffers()
        {
            OpenUnityMcpSidecarInstaller.Install(_source, "0.17.0", _target);
            File.WriteAllText(Path.Combine(_source, "open-unity-mcp-sidecar.js"), "// sidecar v2");

            Assert.AreEqual(OpenUnityMcpSidecarInstaller.InstallOutcome.Updated, OpenUnityMcpSidecarInstaller.Install(_source, "0.17.0", _target));
            Assert.AreEqual("// sidecar v2", File.ReadAllText(Path.Combine(_target, "open-unity-mcp-sidecar.js")));
            Assert.AreEqual(0, Directory.GetFiles(_target, "*.tmp").Length, "Atomic writes must not leave temp files behind.");
        }

        [Test]
        public void InstallKeepsNewerVersionFromAnotherProject()
        {
            OpenUnityMcpSidecarInstaller.Install(_source, "0.18.0", _target);
            File.WriteAllText(Path.Combine(_source, "open-unity-mcp-sidecar.js"), "// older sidecar");

            Assert.AreEqual(OpenUnityMcpSidecarInstaller.InstallOutcome.KeptNewer, OpenUnityMcpSidecarInstaller.Install(_source, "0.17.0", _target));
            Assert.AreEqual("// sidecar v1", File.ReadAllText(Path.Combine(_target, "open-unity-mcp-sidecar.js")));
        }

        [Test]
        public void InstallRequiresTheSidecarScript()
        {
            File.Delete(Path.Combine(_source, "open-unity-mcp-sidecar.js"));

            Assert.Throws<FileNotFoundException>(() => OpenUnityMcpSidecarInstaller.Install(_source, "0.17.0", _target));
        }

        [Test]
        public void CompareVersionsOrdersDottedVersionsAndIgnoresSuffixes()
        {
            Assert.Less(OpenUnityMcpSidecarInstaller.CompareVersions("0.16.1", "0.17.0"), 0);
            Assert.Greater(OpenUnityMcpSidecarInstaller.CompareVersions("0.17.0", "0.16.10"), 0);
            Assert.AreEqual(0, OpenUnityMcpSidecarInstaller.CompareVersions("0.17.0-preview.1", "0.17.0"));
            Assert.Less(OpenUnityMcpSidecarInstaller.CompareVersions(null, "0.1.0"), 0);
            Assert.Greater(OpenUnityMcpSidecarInstaller.CompareVersions("0.1.0", "garbage"), 0);
        }

        [Test]
        public void RepairRepointsPackageCacheScriptAndKeepsOtherServers()
        {
            var configPath = WriteDesktopConfig(StaleScript, _projectRoot.Replace('\\', '/'));

            Assert.IsTrue(OpenUnityMcpClientSetup.RepairJsonSidecarConfig(configPath, _stableScript, _projectRoot, false));

            var servers = ReadServers(configPath);
            var args = (List<object>)((Dictionary<string, object>)servers["open-unity-mcp"])["args"];
            CollectionAssert.AreEqual(new object[] { _stableScript, "--port", "8080", "--project", _projectRoot.Replace('\\', '/') }, args);
            Assert.IsTrue(servers.ContainsKey("filesystem"));
        }

        [Test]
        public void RepairReplacesMissingScriptOutsidePackageCache()
        {
            var configPath = WriteDesktopConfig("D:/Moved/Packages/com.strangeape.open-unity-mcp/Server~/open-unity-mcp-sidecar.js", _projectRoot);

            Assert.IsTrue(OpenUnityMcpClientSetup.RepairJsonSidecarConfig(configPath, _stableScript, _projectRoot, false));
            StringAssert.Contains(_stableScript, File.ReadAllText(configPath));
        }

        [Test]
        public void RepairLeavesExistingScriptOutsidePackageCacheAlone()
        {
            var custom = Path.Combine(_directory, "custom", "open-unity-mcp-sidecar.js");
            Directory.CreateDirectory(Path.GetDirectoryName(custom));
            File.WriteAllText(custom, "// custom");
            var configPath = WriteDesktopConfig(custom.Replace('\\', '/'), _projectRoot.Replace('\\', '/'));
            var before = File.ReadAllText(configPath);

            Assert.IsFalse(OpenUnityMcpClientSetup.RepairJsonSidecarConfig(configPath, _stableScript, _projectRoot, false));
            Assert.AreEqual(before, File.ReadAllText(configPath));
        }

        [Test]
        public void RepairFixesProjectInOwnConfigButOnlyMissingProjectInSharedConfig()
        {
            var otherExistingProject = Path.Combine(_directory, "other").Replace('\\', '/');
            Directory.CreateDirectory(otherExistingProject);

            var ownConfig = WriteDesktopConfig(_stableScript, otherExistingProject, ".mcp.json");
            Assert.IsTrue(OpenUnityMcpClientSetup.RepairJsonSidecarConfig(ownConfig, _stableScript, _projectRoot, true));
            StringAssert.Contains("\"" + _projectRoot.Replace('\\', '/') + "\"", File.ReadAllText(ownConfig));

            var sharedConfig = WriteDesktopConfig(_stableScript, otherExistingProject);
            Assert.IsFalse(OpenUnityMcpClientSetup.RepairJsonSidecarConfig(sharedConfig, _stableScript, _projectRoot, false));

            var movedConfig = WriteDesktopConfig(_stableScript, "D:/Moved/Project", "moved.json");
            Assert.IsTrue(OpenUnityMcpClientSetup.RepairJsonSidecarConfig(movedConfig, _stableScript, _projectRoot, false));
            StringAssert.Contains("\"" + _projectRoot.Replace('\\', '/') + "\"", File.ReadAllText(movedConfig));
        }

        [Test]
        public void RepairLeavesHttpEntriesAndInvalidJsonAlone()
        {
            var httpConfig = Path.Combine(_directory, "http.json");
            File.WriteAllText(httpConfig, "{\"mcpServers\":{\"open-unity-mcp\":{\"type\":\"http\",\"url\":\"http://127.0.0.1:8080/mcp\"}}}");
            Assert.IsFalse(OpenUnityMcpClientSetup.RepairJsonSidecarConfig(httpConfig, _stableScript, _projectRoot, true));

            var invalidConfig = Path.Combine(_directory, "invalid.json");
            File.WriteAllText(invalidConfig, "{ \"mcpServers\": " + StaleScript);
            Assert.IsFalse(OpenUnityMcpClientSetup.RepairJsonSidecarConfig(invalidConfig, _stableScript, _projectRoot, true));
            Assert.AreEqual("{ \"mcpServers\": " + StaleScript, File.ReadAllText(invalidConfig));
        }

        [Test]
        public void CodexRepairRewritesOnlyTheArgsLine()
        {
            var configPath = Path.Combine(_directory, "config.toml");
            var original = string.Join("\n", new[]
            {
                "[mcp_servers.open-unity-mcp]",
                "command = \"node\"",
                "args = [\"" + StaleScript + "\", \"--port\", \"8080\", \"--project\", \"" + _projectRoot.Replace('\\', '/') + "\"]",
                "",
                "[mcp_servers.open-unity-mcp.tools.\"unity.get_project_info\"]",
                "approval_mode = \"approve\"",
                ""
            });
            File.WriteAllText(configPath, original);

            Assert.IsTrue(OpenUnityMcpClientSetup.RepairCodexSidecarConfig(configPath, _stableScript, _projectRoot));

            var updated = File.ReadAllText(configPath);
            StringAssert.Contains("args = [\"" + _stableScript + "\", \"--port\", \"8080\"", updated);
            StringAssert.Contains("[mcp_servers.open-unity-mcp.tools.\"unity.get_project_info\"]\napproval_mode = \"approve\"", updated);
            Assert.IsFalse(updated.Contains("\r\n"), "Line endings must be preserved.");
            Assert.IsFalse(updated.Contains("PackageCache"));
        }

        [Test]
        public void CodexRepairLeavesUrlEntriesAlone()
        {
            var configPath = Path.Combine(_directory, "config.toml");
            File.WriteAllText(configPath, "[mcp_servers.open-unity-mcp]\nurl = \"http://127.0.0.1:8080/mcp\"\n");

            Assert.IsFalse(OpenUnityMcpClientSetup.RepairCodexSidecarConfig(configPath, _stableScript, _projectRoot));
        }

        private string WriteDesktopConfig(string scriptPath, string project, string fileName = "claude_desktop_config.json")
        {
            var configPath = Path.Combine(_directory, fileName);
            File.WriteAllText(configPath, McpJson.Stringify(McpJson.Object(
                "mcpServers", McpJson.Object(
                    "filesystem", McpJson.Object("command", "npx", "args", McpJson.Array("server")),
                    "open-unity-mcp", McpJson.Object(
                        "command", "node",
                        "args", McpJson.Array(scriptPath, "--port", "8080", "--project", project))))));
            return configPath;
        }

        private static Dictionary<string, object> ReadServers(string configPath)
        {
            var root = (Dictionary<string, object>)McpJson.Parse(File.ReadAllText(configPath));
            return (Dictionary<string, object>)root["mcpServers"];
        }
    }
}
