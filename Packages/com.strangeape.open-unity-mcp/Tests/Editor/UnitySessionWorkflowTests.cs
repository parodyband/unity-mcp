using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace StrangeApe.OpenUnityMcp.Tests
{
    public sealed class UnitySessionWorkflowTests
    {
        [UnityTest]
        public IEnumerator SidecarSessionEditsAndVerifiesFiveLightsInOneEditorRequest()
        {
            var root = new GameObject("OpenUnityMcpLiveSession");
            var wasRunning = OpenUnityMcpServer.IsRunning;
            var previousPort = OpenUnityMcpServer.Port;
            System.Diagnostics.Process process = null;
            try
            {
                for (var i = 0; i < 5; i++)
                {
                    var child = new GameObject("Light " + i);
                    child.transform.SetParent(root.transform);
                    child.AddComponent<Light>();
                }
                var probe = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
                probe.Start();
                var port = ((System.Net.IPEndPoint)probe.LocalEndpoint).Port;
                probe.Stop();
                OpenUnityMcpServer.Stop();
                OpenUnityMcpServer.Start(port);
                var package = UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(OpenUnityMcpServer).Assembly);
                var script = Path.Combine(package.resolvedPath, "Server~", "test", "live-session.mjs");
                var info = new System.Diagnostics.ProcessStartInfo("node",
                    "\"" + script + "\" " + port + " \"" + OpenUnityMcpClientSetup.ProjectRoot + "\" " + UnityMcpObjectUtility.GetObjectId(root))
                {
                    UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true
                };
                try { process = System.Diagnostics.Process.Start(info); }
                catch (System.ComponentModel.Win32Exception) { Assert.Ignore("Node.js is not installed; sidecar integration requires Node 18+."); }
                var output = process.StandardOutput.ReadToEndAsync();
                var errors = process.StandardError.ReadToEndAsync();
                var deadline = DateTime.UtcNow.AddSeconds(25);
                while (!process.HasExited && DateTime.UtcNow < deadline) yield return null;
                Assert.IsTrue(process.HasExited, "Live SDK test timed out.");
                while (!output.IsCompleted || !errors.IsCompleted) yield return null;
                Assert.AreEqual(0, process.ExitCode, errors.Result + output.Result);
                TestContext.WriteLine(output.Result);
                foreach (var light in root.GetComponentsInChildren<Light>()) Assert.AreEqual(4.25f, light.intensity);
            }
            finally
            {
                if (process != null)
                {
                    if (!process.HasExited) process.Kill();
                    process.Dispose();
                }
                OpenUnityMcpServer.Stop();
                if (wasRunning && previousPort > 0) OpenUnityMcpServer.Start(previousPort);
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void BulkEditRejectsStaleEpochAndPrevalidatesEveryTarget()
        {
            var a = new GameObject("Bulk A");
            var b = new GameObject("Bulk B");
            try
            {
                var light = a.AddComponent<Light>();
                light.intensity = 1;
                var args = McpJson.Object("editorEpoch", "stale", "targets", McpJson.Array(UnityMcpObjectUtility.GetObjectId(light)), "set", McpJson.Object("m_Intensity", 3));
                Assert.IsTrue((bool)McpToolRegistry.Call("unity.edit_objects", args)["isError"]);
                args["editorEpoch"] = UnityMcpWorkflowTools.EditorEpoch;
                args["targets"] = McpJson.Array(UnityMcpObjectUtility.GetObjectId(light), UnityMcpObjectUtility.GetObjectId(b.transform));
                Assert.IsTrue((bool)McpToolRegistry.Call("unity.edit_objects", args)["isError"]);
                Assert.AreEqual(1, light.intensity);
            }
            finally { UnityEngine.Object.DestroyImmediate(a); UnityEngine.Object.DestroyImmediate(b); }
        }

        [Test]
        public void BulkEditReturnsVerifiedValuesAndRespectsDisabledSetter()
        {
            var a = new GameObject("Bulk light");
            var enabled = OpenUnityMcpSettings.IsToolEnabled("unity.set_serialized_property");
            try
            {
                var light = a.AddComponent<Light>();
                var args = McpJson.Object("editorEpoch", UnityMcpWorkflowTools.EditorEpoch,
                    "targets", McpJson.Array(UnityMcpObjectUtility.GetObjectId(light)), "set", McpJson.Object("m_Intensity", 3.5));
                OpenUnityMcpSettings.SetToolEnabled("unity.set_serialized_property", false);
                Assert.IsTrue((bool)McpToolRegistry.Call("unity.edit_objects", args)["isError"]);
                OpenUnityMcpSettings.SetToolEnabled("unity.set_serialized_property", true);
                var result = McpToolRegistry.Call("unity.edit_objects", args);
                Assert.IsFalse((bool)result["isError"], McpJson.Stringify(result));
                var payload = (Dictionary<string, object>)result["structuredContent"];
                var entry = (Dictionary<string, object>)((List<object>)payload["results"])[0];
                Assert.AreEqual(3.5f, ((Dictionary<string, object>)entry["values"])["m_Intensity"]);
                Assert.AreEqual(3.5f, light.intensity);
            }
            finally
            {
                OpenUnityMcpSettings.SetToolEnabled("unity.set_serialized_property", enabled);
                UnityEngine.Object.DestroyImmediate(a);
            }
        }

        [TestCase("codex", ".agents")]
        [TestCase("claude-code", ".claude")]
        public void SkillSetupUpdatesManagedFilesAndPreservesCustomizations(string client, string directory)
        {
            var root = Path.Combine(Path.GetTempPath(), "UnityMcpSkillTest-" + Guid.NewGuid().ToString("N"));
            var source = Path.Combine(root, "source");
            Directory.CreateDirectory(Path.Combine(source, "references"));
            File.WriteAllText(Path.Combine(source, "SKILL.md"), "original skill");
            File.WriteAllText(Path.Combine(source, "references/sdk.md"), "original docs");
            try
            {
                var target = OpenUnityMcpSkillSetup.Install(root, client, source);
                Assert.AreEqual(Path.Combine(root, directory, "skills", "open-unity-mcp", "SKILL.md"), target);
                Assert.AreEqual("original skill", File.ReadAllText(target));
                File.WriteAllText(Path.Combine(source, "SKILL.md"), "updated skill");
                OpenUnityMcpSkillSetup.Install(root, client, source);
                Assert.AreEqual("updated skill", File.ReadAllText(target));
                File.WriteAllText(target, "user customization");
                File.WriteAllText(Path.Combine(source, "references/sdk.md"), "new docs");
                Assert.Throws<InvalidOperationException>(() => OpenUnityMcpSkillSetup.Install(root, client, source));
                Assert.AreEqual("user customization", File.ReadAllText(target));
                Assert.AreEqual("original docs", File.ReadAllText(Path.Combine(Path.GetDirectoryName(target), "references/sdk.md")));
            }
            finally { Directory.Delete(root, true); }
        }
    }
}
