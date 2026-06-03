using System.IO;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace StrangeApe.OpenUnityMcp.Tests
{
    public sealed class OpenUnityMcpGeneratedSettings : ScriptableObject
    {
    }

    public sealed class UnityToolMutationTests
    {
        private const string GeneratedFolder = "Assets/OpenUnityMcpGenerated";

        [TearDown]
        public void TearDown()
        {
            for (var i = SceneManager.sceneCount - 1; i >= 0; i--)
            {
                var scene = SceneManager.GetSceneAt(i);
                if (scene.path.StartsWith(GeneratedFolder + "/", System.StringComparison.Ordinal))
                {
                    if (SceneManager.sceneCount == 1)
                    {
                        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                    }
                    else
                    {
                        EditorSceneManager.CloseScene(scene, true);
                    }
                }
            }

            foreach (var gameObject in Object.FindObjectsByType<GameObject>(FindObjectsSortMode.None))
            {
                if (gameObject == null)
                {
                    continue;
                }

                if (gameObject.name.StartsWith("OpenUnityMcp", System.StringComparison.Ordinal))
                {
                    Object.DestroyImmediate(gameObject);
                }
            }

            if (AssetDatabase.IsValidFolder(GeneratedFolder))
            {
                AssetDatabase.DeleteAsset(GeneratedFolder);
            }

            var generatedFullPath = Path.Combine(UnityMcpPathUtility.ProjectRoot, GeneratedFolder);
            if (Directory.Exists(generatedFullPath))
            {
                Directory.Delete(generatedFullPath, true);
            }
        }

        [Test]
        public void ComponentToolsAddComponentAndSetSerializedProperty()
        {
            var gameObject = new GameObject("OpenUnityMcpComponentTarget");
            var objectId = UnityMcpObjectUtility.GetObjectId(gameObject);
            var addResponse = CallTool("unity.add_component", "\"objectId\":\"" + objectId + "\",\"componentType\":\"UnityEngine.BoxCollider\"");

            StringAssert.Contains("UnityEngine.BoxCollider", ExtractToolText(addResponse));
            Assert.NotNull(gameObject.GetComponent<BoxCollider>());

            var setResponse = CallTool("unity.set_serialized_property", "\"objectId\":\"" + objectId + "\",\"propertyPath\":\"m_Name\",\"value\":\"OpenUnityMcpRenamed\"");

            StringAssert.Contains("\"changed\":true", ExtractToolText(setResponse));
            Assert.AreEqual("OpenUnityMcpRenamed", gameObject.name);

            var propertiesResponse = CallTool("unity.get_serialized_properties", "\"objectId\":\"" + objectId + "\",\"limit\":20");
            StringAssert.Contains("\"properties\"", ExtractToolText(propertiesResponse));
        }

        [Test]
        public void PrefabToolsSaveInspectAndInstantiatePrefab()
        {
            var source = new GameObject("OpenUnityMcpPrefabSource");
            source.AddComponent<BoxCollider>();
            var prefabPath = GeneratedFolder + "/Generated.prefab";

            var saveResponse = CallTool("unity.save_as_prefab_asset", "\"objectId\":\"" + UnityMcpObjectUtility.GetObjectId(source) + "\",\"path\":\"" + prefabPath + "\",\"createDirectories\":true");
            StringAssert.Contains("\"saved\":true", ExtractToolText(saveResponse));
            Assert.IsTrue(File.Exists(Path.Combine(UnityMcpPathUtility.ProjectRoot, prefabPath)));

            var infoResponse = CallTool("unity.get_prefab_info", "\"path\":\"" + prefabPath + "\"");
            StringAssert.Contains(prefabPath, ExtractToolText(infoResponse));

            var instantiateResponse = CallTool("unity.instantiate_prefab", "\"prefabPath\":\"" + prefabPath + "\",\"name\":\"OpenUnityMcpPrefabInstance\",\"select\":false");
            StringAssert.Contains("OpenUnityMcpPrefabInstance", ExtractToolText(instantiateResponse));
            Assert.NotNull(GameObject.Find("OpenUnityMcpPrefabInstance"));
        }

        [Test]
        public void PrefabToolsFindChildAddComponentAndSavePrefabAsset()
        {
            var source = new GameObject("OpenUnityMcpPrefabRoot");
            var child = new GameObject("Hit Collider");
            child.transform.SetParent(source.transform);
            var prefabPath = GeneratedFolder + "/ChildTarget.prefab";

            var saveAsResponse = CallTool("unity.save_as_prefab_asset", "\"objectId\":\"" + UnityMcpObjectUtility.GetObjectId(source) + "\",\"path\":\"" + prefabPath + "\",\"createDirectories\":true");
            StringAssert.Contains("\"saved\":true", ExtractToolText(saveAsResponse));

            var infoResponse = CallTool("unity.get_prefab_info", "\"path\":\"" + prefabPath + "\"");
            StringAssert.Contains("\"rootComponents\"", ExtractToolText(infoResponse));

            var hierarchyResponse = CallTool("unity.get_hierarchy", "\"path\":\"" + prefabPath + "\",\"maxDepth\":4");
            StringAssert.Contains("Hit Collider", ExtractToolText(hierarchyResponse));

            var findResponse = CallTool("unity.find_child", "\"path\":\"" + prefabPath + "\",\"childPath\":\"Hit Collider\"");
            var findPayload = ExtractToolJson(findResponse);
            var childPayload = findPayload["child"] as System.Collections.Generic.Dictionary<string, object>;
            Assert.IsNotNull(childPayload);
            Assert.IsFalse(string.IsNullOrEmpty((string)childPayload["objectId"]));

            var addResponse = CallTool("unity.add_component", "\"path\":\"" + prefabPath + "\",\"childPath\":\"Hit Collider\",\"componentType\":\"UnityEngine.Rigidbody\"");
            StringAssert.Contains("\"saved\":true", ExtractToolText(addResponse));

            var componentsResponse = CallTool("unity.get_components", "\"path\":\"" + prefabPath + "\",\"childPath\":\"Hit Collider\"");
            StringAssert.Contains("UnityEngine.Rigidbody", ExtractToolText(componentsResponse));

            var saveResponse = CallTool("unity.save_prefab_asset", "\"path\":\"" + prefabPath + "\"");
            StringAssert.Contains("\"saved\":true", ExtractToolText(saveResponse));

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            var savedChild = prefab.transform.Find("Hit Collider");
            Assert.NotNull(savedChild);
            Assert.NotNull(savedChild.GetComponent<Rigidbody>());
        }

        [Test]
        public void AssetToolsCreateScriptableObjectAsset()
        {
            var assetPath = GeneratedFolder + "/BoarHerdSettings.asset";
            var response = CallTool(
                "unity.create_scriptable_object",
                "\"typeName\":\"" + typeof(OpenUnityMcpGeneratedSettings).FullName + "\",\"path\":\"" + assetPath + "\",\"createDirectories\":true");

            StringAssert.Contains("\"created\":true", ExtractToolText(response));
            Assert.NotNull(AssetDatabase.LoadAssetAtPath<OpenUnityMcpGeneratedSettings>(assetPath));
        }

        [Test]
        public void AssetLifecycleToolsCreateCopyMoveAndDeleteAssets()
        {
            var folderPath = GeneratedFolder + "/Lifecycle";
            var sourcePath = folderPath + "/Source.txt";
            var copiedPath = folderPath + "/Copied.txt";
            var movedPath = folderPath + "/Moved.txt";

            var createFolderResponse = CallTool("unity.create_folder", "\"path\":\"" + folderPath + "\"");
            StringAssert.Contains("\"created\":true", ExtractToolText(createFolderResponse));
            Assert.IsTrue(AssetDatabase.IsValidFolder(folderPath));

            var writeResponse = CallTool("unity.write_asset_text", "\"path\":\"" + sourcePath + "\",\"text\":\"lifecycle\",\"createDirectories\":true");
            StringAssert.Contains(sourcePath, ExtractToolText(writeResponse));
            StringAssert.Contains("\"refreshed\":true", ExtractToolText(writeResponse));

            var copyResponse = CallTool("unity.copy_asset", "\"sourcePath\":\"" + sourcePath + "\",\"destinationPath\":\"" + copiedPath + "\"");
            StringAssert.Contains("\"copied\":true", ExtractToolText(copyResponse));
            Assert.IsTrue(File.Exists(Path.Combine(UnityMcpPathUtility.ProjectRoot, copiedPath)));

            var moveResponse = CallTool("unity.move_asset", "\"sourcePath\":\"" + copiedPath + "\",\"destinationPath\":\"" + movedPath + "\"");
            StringAssert.Contains("\"moved\":true", ExtractToolText(moveResponse));
            Assert.IsFalse(File.Exists(Path.Combine(UnityMcpPathUtility.ProjectRoot, copiedPath)));
            Assert.IsTrue(File.Exists(Path.Combine(UnityMcpPathUtility.ProjectRoot, movedPath)));

            var deleteFileResponse = CallTool("unity.delete_asset", "\"path\":\"" + sourcePath + "\"");
            StringAssert.Contains("\"deleted\":true", ExtractToolText(deleteFileResponse));
            Assert.IsFalse(File.Exists(Path.Combine(UnityMcpPathUtility.ProjectRoot, sourcePath)));

            var deleteFolderResponse = CallTool("unity.delete_asset", "\"path\":\"" + folderPath + "\",\"recursive\":true");
            StringAssert.Contains("\"deleted\":true", ExtractToolText(deleteFolderResponse));
            Assert.IsFalse(AssetDatabase.IsValidFolder(folderPath));
        }

        [Test]
        public void WriteAssetTextDefersRefreshForScriptAssetsByDefault()
        {
            var scriptPath = GeneratedFolder + "/DeferredCompile.cs";
            var writeResponse = CallTool(
                "unity.write_asset_text",
                "\"path\":\"" + scriptPath + "\",\"text\":\"public sealed class OpenUnityMcpDeferredCompile {}\",\"createDirectories\":true");
            var payload = ExtractToolJson(writeResponse);

            Assert.AreEqual(scriptPath, payload["path"]);
            Assert.IsFalse((bool)payload["refreshed"]);
            Assert.IsTrue((bool)payload["requiresRefresh"]);
            Assert.IsTrue((bool)payload["codeRelatedAsset"]);
            Assert.AreEqual("unity.refresh_assets", payload["nextTool"]);
            Assert.IsTrue(File.Exists(Path.Combine(UnityMcpPathUtility.ProjectRoot, scriptPath)));
        }

        [Test]
        public void SceneToolsSaveOpenSaveAllAndCloseScene()
        {
            var baseScene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var baseScenePath = GeneratedFolder + "/BaseScene.unity";
            Directory.CreateDirectory(Path.Combine(UnityMcpPathUtility.ProjectRoot, GeneratedFolder));
            Assert.IsTrue(EditorSceneManager.SaveScene(baseScene, baseScenePath));

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            SceneManager.SetActiveScene(scene);
            new GameObject("OpenUnityMcpSceneToolObject");

            var scenePath = GeneratedFolder + "/GeneratedScene.unity";
            var saveResponse = CallTool("unity.save_scene", "\"saveAsPath\":\"" + scenePath + "\",\"createDirectories\":true");
            StringAssert.Contains("\"saved\":true", ExtractToolText(saveResponse));
            Assert.IsTrue(File.Exists(Path.Combine(UnityMcpPathUtility.ProjectRoot, scenePath)));

            EditorSceneManager.CloseScene(scene, true);

            var openResponse = CallTool("unity.open_scene", "\"path\":\"" + scenePath + "\",\"mode\":\"Additive\",\"setActive\":true");
            StringAssert.Contains(scenePath, ExtractToolText(openResponse));
            Assert.AreEqual(scenePath, SceneManager.GetActiveScene().path);

            var saveAllResponse = CallTool("unity.save_all_scenes", string.Empty);
            StringAssert.Contains("\"saved\":true", ExtractToolText(saveAllResponse));

            var closeResponse = CallTool("unity.close_scene", "\"scenePath\":\"" + scenePath + "\",\"discardUnsavedChanges\":true");
            StringAssert.Contains("\"closed\":true", ExtractToolText(closeResponse));
        }

        [Test]
        public void SceneToolsCreateAndSetTransforms()
        {
            var createResponse = CallTool(
                "unity.create_game_object",
                "\"name\":\"OpenUnityMcpTransformSphere\",\"primitiveType\":\"Sphere\",\"position\":{\"x\":1,\"y\":2,\"z\":3},\"scale\":{\"x\":2,\"y\":2,\"z\":2},\"select\":false");
            var sphere = GameObject.Find("OpenUnityMcpTransformSphere");

            Assert.NotNull(sphere);
            Assert.AreEqual(new Vector3(1f, 2f, 3f), sphere.transform.position);
            Assert.AreEqual(new Vector3(2f, 2f, 2f), sphere.transform.localScale);
            StringAssert.Contains("\"position\"", ExtractToolText(createResponse));

            var setResponse = CallTool(
                "unity.set_transform",
                "\"objectId\":\"" + UnityMcpObjectUtility.GetObjectId(sphere) + "\",\"localPosition\":{\"x\":4,\"y\":5,\"z\":6},\"localScale\":{\"x\":1,\"y\":2,\"z\":3}");

            Assert.AreEqual(new Vector3(4f, 5f, 6f), sphere.transform.localPosition);
            Assert.AreEqual(new Vector3(1f, 2f, 3f), sphere.transform.localScale);
            StringAssert.Contains("\"changed\":true", ExtractToolText(setResponse));
        }

        [Test]
        public void SceneToolsBatchCreateObjectsWithParentIndex()
        {
            var response = CallTool(
                "unity.create_game_objects",
                "\"objects\":[" +
                "{\"name\":\"OpenUnityMcpSnowmanRoot\"}," +
                "{\"name\":\"OpenUnityMcpSnowmanBody\",\"primitiveType\":\"Sphere\",\"parentIndex\":0,\"localPosition\":{\"x\":0,\"y\":1,\"z\":0},\"scale\":{\"x\":2,\"y\":2,\"z\":2}}," +
                "{\"name\":\"OpenUnityMcpSnowmanHead\",\"primitiveType\":\"Sphere\",\"parentIndex\":0,\"localPosition\":{\"x\":0,\"y\":2.8,\"z\":0},\"scale\":{\"x\":1,\"y\":1,\"z\":1}}" +
                "],\"selectLast\":false");

            var root = GameObject.Find("OpenUnityMcpSnowmanRoot");
            var body = GameObject.Find("OpenUnityMcpSnowmanBody");
            var head = GameObject.Find("OpenUnityMcpSnowmanHead");

            Assert.NotNull(root);
            Assert.NotNull(body);
            Assert.NotNull(head);
            Assert.AreEqual(root.transform, body.transform.parent);
            Assert.AreEqual(root.transform, head.transform.parent);
            Assert.AreEqual(new Vector3(0f, 1f, 0f), body.transform.localPosition);
            Assert.That(head.transform.localPosition.x, Is.EqualTo(0f).Within(0.0001f));
            Assert.That(head.transform.localPosition.y, Is.EqualTo(2.8f).Within(0.0001f));
            Assert.That(head.transform.localPosition.z, Is.EqualTo(0f).Within(0.0001f));
            StringAssert.Contains("\"count\":3", ExtractToolText(response));
        }

        private static McpProtocolResponse CallTool(string toolName, string argumentsJson)
        {
            return McpProtocol.Handle("{\"jsonrpc\":\"2.0\",\"id\":\"call\",\"method\":\"tools/call\",\"params\":{\"name\":\"" + toolName + "\",\"arguments\":{" + argumentsJson + "}}}");
        }

        private static string ExtractToolText(McpProtocolResponse response)
        {
            var body = McpJson.Parse(response.Body) as System.Collections.Generic.Dictionary<string, object>;
            var result = body["result"] as System.Collections.Generic.Dictionary<string, object>;
            var content = result["content"] as System.Collections.Generic.List<object>;
            var first = content[0] as System.Collections.Generic.Dictionary<string, object>;
            return (string)first["text"];
        }

        private static System.Collections.Generic.Dictionary<string, object> ExtractToolJson(McpProtocolResponse response)
        {
            return McpJson.Parse(ExtractToolText(response)) as System.Collections.Generic.Dictionary<string, object>;
        }
    }
}
