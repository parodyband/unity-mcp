using System.Collections.Generic;
using NUnit.Framework;

namespace StrangeApe.OpenUnityMcp.Tests
{
    public sealed class McpProtocolTests
    {
        [Test]
        public void InitializeReturnsServerInfoAndToolsCapability()
        {
            var response = McpProtocol.Handle("{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"initialize\",\"params\":{\"protocolVersion\":\"2025-06-18\"}}");

            Assert.AreEqual(200, response.HttpStatus);
            Assert.IsFalse(response.AcceptedNotification);
            StringAssert.Contains("\"protocolVersion\":\"2025-06-18\"", response.Body);
            StringAssert.Contains("\"tools\"", response.Body);
            StringAssert.Contains("\"resources\"", response.Body);
            StringAssert.Contains("\"prompts\"", response.Body);
            StringAssert.Contains("\"open-unity-mcp\"", response.Body);
        }

        [Test]
        public void InitializedNotificationIsAcceptedWithoutBody()
        {
            var response = McpProtocol.Handle("{\"jsonrpc\":\"2.0\",\"method\":\"notifications/initialized\"}");

            Assert.AreEqual(202, response.HttpStatus);
            Assert.IsTrue(response.AcceptedNotification);
            Assert.AreEqual(string.Empty, response.Body);
        }

        [Test]
        public void ToolsListIsDeterministic()
        {
            var response = McpProtocol.Handle("{\"jsonrpc\":\"2.0\",\"id\":\"tools\",\"method\":\"tools/list\"}");
            var body = McpJson.Parse(response.Body) as Dictionary<string, object>;
            var result = body["result"] as Dictionary<string, object>;
            var tools = result["tools"] as List<object>;

            Assert.GreaterOrEqual(tools.Count, 5);
            var previous = string.Empty;
            foreach (Dictionary<string, object> tool in tools)
            {
                var name = (string)tool["name"];
                Assert.GreaterOrEqual(string.CompareOrdinal(name, previous), 0);
                previous = name;
            }
        }

        [Test]
        public void ToolsListContainsCoreUnityToolGroups()
        {
            var response = McpProtocol.Handle("{\"jsonrpc\":\"2.0\",\"id\":\"tools\",\"method\":\"tools/list\"}");

            StringAssert.Contains("\"unity.get_project_info\"", response.Body);
            StringAssert.Contains("\"unity.get_asset_metadata\"", response.Body);
            StringAssert.Contains("\"unity.create_asset\"", response.Body);
            StringAssert.Contains("\"unity.create_folder\"", response.Body);
            StringAssert.Contains("\"unity.create_scriptable_object\"", response.Body);
            StringAssert.Contains("\"unity.copy_asset\"", response.Body);
            StringAssert.Contains("\"unity.move_asset\"", response.Body);
            StringAssert.Contains("\"unity.delete_asset\"", response.Body);
            StringAssert.Contains("\"unity.execute_csharp\"", response.Body);
            StringAssert.Contains("\"unity.list_packages\"", response.Body);
            StringAssert.Contains("\"unity.get_hierarchy\"", response.Body);
            StringAssert.Contains("\"unity.create_game_object\"", response.Body);
            StringAssert.Contains("\"unity.create_game_objects\"", response.Body);
            StringAssert.Contains("\"unity.set_transform\"", response.Body);
            StringAssert.Contains("\"unity.set_scene_view_camera\"", response.Body);
            StringAssert.Contains("\"unity.frame_scene_view\"", response.Body);
            StringAssert.Contains("\"unity.capture_scene_view\"", response.Body);
            StringAssert.Contains("\"unity.clear_console\"", response.Body);
            StringAssert.Contains("\"unity.add_component\"", response.Body);
            StringAssert.Contains("\"unity.get_serialized_properties\"", response.Body);
            StringAssert.Contains("\"unity.find_child\"", response.Body);
            StringAssert.Contains("\"unity.instantiate_prefab\"", response.Body);
            StringAssert.Contains("\"unity.save_as_prefab_asset\"", response.Body);
            StringAssert.Contains("\"unity.save_prefab_asset\"", response.Body);
            StringAssert.Contains("\"unity.apply_prefab_changes\"", response.Body);
            StringAssert.Contains("\"unity.get_compilation_status\"", response.Body);
            StringAssert.Contains("\"unity.validate_project\"", response.Body);
            StringAssert.Contains("\"unity.get_build_settings\"", response.Body);
            StringAssert.Contains("\"unity.build_player\"", response.Body);
            StringAssert.Contains("\"unity.open_scene\"", response.Body);
            StringAssert.Contains("\"unity.save_scene\"", response.Body);
            StringAssert.Contains("\"unity.save_all_scenes\"", response.Body);
            StringAssert.Contains("\"unity.close_scene\"", response.Body);
        }

        [Test]
        public void AssetTextToolsRejectTraversalOutsideAssetsAndPackages()
        {
            var response = McpProtocol.Handle("{\"jsonrpc\":\"2.0\",\"id\":3,\"method\":\"tools/call\",\"params\":{\"name\":\"unity.read_asset_text\",\"arguments\":{\"path\":\"Assets/../ProjectSettings/ProjectVersion.txt\"}}}");

            Assert.AreEqual(200, response.HttpStatus);
            StringAssert.Contains("\"isError\":true", response.Body);
            StringAssert.Contains("Path must resolve under Assets or Packages", response.Body);
        }

        [Test]
        public void ValidationToolsExposeEditorAndBuildState()
        {
            var compilationResponse = McpProtocol.Handle("{\"jsonrpc\":\"2.0\",\"id\":\"compile\",\"method\":\"tools/call\",\"params\":{\"name\":\"unity.get_compilation_status\",\"arguments\":{}}}");
            var buildSettingsResponse = McpProtocol.Handle("{\"jsonrpc\":\"2.0\",\"id\":\"build-settings\",\"method\":\"tools/call\",\"params\":{\"name\":\"unity.get_build_settings\",\"arguments\":{}}}");

            Assert.AreEqual(200, compilationResponse.HttpStatus);
            Assert.AreEqual(200, buildSettingsResponse.HttpStatus);
            StringAssert.Contains("activeBuildTarget", ExtractToolText(compilationResponse));
            StringAssert.Contains("isCompiling", ExtractToolText(compilationResponse));
            StringAssert.Contains("assemblyLoadSequence", ExtractToolText(compilationResponse));
            StringAssert.Contains("serverReloadedSinceLastScriptCompilationRequest", ExtractToolText(compilationResponse));
            StringAssert.Contains("serverWasRunningBeforeLastAssemblyReload", ExtractToolText(compilationResponse));
            StringAssert.Contains("scenes", ExtractToolText(buildSettingsResponse));
        }

        [Test]
        public void BuildPlayerRejectsOutputOutsideBuildsFolder()
        {
            var response = McpProtocol.Handle("{\"jsonrpc\":\"2.0\",\"id\":\"build\",\"method\":\"tools/call\",\"params\":{\"name\":\"unity.build_player\",\"arguments\":{\"outputPath\":\"ProjectSettings/Bad.exe\"}}}");

            Assert.AreEqual(200, response.HttpStatus);
            StringAssert.Contains("\"isError\":true", response.Body);
            StringAssert.Contains("Build output must resolve under the project Builds folder", response.Body);
        }

        [Test]
        public void AssetLifecycleRejectsProtectedRootMutation()
        {
            var response = McpProtocol.Handle("{\"jsonrpc\":\"2.0\",\"id\":\"delete-root\",\"method\":\"tools/call\",\"params\":{\"name\":\"unity.delete_asset\",\"arguments\":{\"path\":\"Assets\",\"recursive\":true}}}");

            Assert.AreEqual(200, response.HttpStatus);
            StringAssert.Contains("\"isError\":true", response.Body);
            StringAssert.Contains("Refusing to modify protected root folder", response.Body);
        }

        [Test]
        public void ResourcesListAndReadExposeProjectContext()
        {
            var listResponse = McpProtocol.Handle("{\"jsonrpc\":\"2.0\",\"id\":\"resources\",\"method\":\"resources/list\"}");
            StringAssert.Contains("unity://project/info", listResponse.Body);
            StringAssert.Contains("unity://docs/tools", listResponse.Body);

            var readResponse = McpProtocol.Handle("{\"jsonrpc\":\"2.0\",\"id\":\"read\",\"method\":\"resources/read\",\"params\":{\"uri\":\"unity://project/info\"}}");
            Assert.AreEqual(200, readResponse.HttpStatus);
            StringAssert.Contains("\"contents\"", readResponse.Body);
            StringAssert.Contains("unityVersion", readResponse.Body);
        }

        [Test]
        public void PromptsListAndGetExposeUnityGuidance()
        {
            var listResponse = McpProtocol.Handle("{\"jsonrpc\":\"2.0\",\"id\":\"prompts\",\"method\":\"prompts/list\"}");
            StringAssert.Contains("unity_editor_task", listResponse.Body);
            StringAssert.Contains("unity_code_review", listResponse.Body);

            var getResponse = McpProtocol.Handle("{\"jsonrpc\":\"2.0\",\"id\":\"prompt\",\"method\":\"prompts/get\",\"params\":{\"name\":\"unity_editor_task\",\"arguments\":{\"task\":\"Create a test prefab\"}}}");
            Assert.AreEqual(200, getResponse.HttpStatus);
            StringAssert.Contains("\"messages\"", getResponse.Body);
            StringAssert.Contains("Create a test prefab", getResponse.Body);
        }

        private static string ExtractToolText(McpProtocolResponse response)
        {
            var body = McpJson.Parse(response.Body) as Dictionary<string, object>;
            var result = body["result"] as Dictionary<string, object>;
            var content = result["content"] as List<object>;
            var first = content[0] as Dictionary<string, object>;
            return (string)first["text"];
        }
    }
}
