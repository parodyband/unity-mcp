using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace StrangeApe.OpenUnityMcp
{
    internal sealed class McpResource
    {
        public readonly string Uri;
        public readonly string Name;
        public readonly string Title;
        public readonly string Description;
        public readonly string MimeType;
        public readonly Func<string> ReadText;

        public McpResource(string uri, string name, string title, string description, string mimeType, Func<string> readText)
        {
            Uri = uri;
            Name = name;
            Title = title;
            Description = description;
            MimeType = mimeType;
            ReadText = readText;
        }
    }

    internal static class McpResourceRegistry
    {
        private static readonly McpResource[] Resources =
        {
            new McpResource(
                "unity://project/info",
                "project_info",
                "Unity Project Info",
                "Current Unity version, project paths, editor state, active scene, and selection count.",
                "application/json",
                ReadProjectInfo),
            new McpResource(
                "unity://project/manifest",
                "package_manifest",
                "Package Manifest",
                "The current Packages/manifest.json content.",
                "application/json",
                () => ReadProjectFile("Packages/manifest.json")),
            new McpResource(
                "unity://project/packages-lock",
                "packages_lock",
                "Package Lock",
                "The current Packages/packages-lock.json content.",
                "application/json",
                () => ReadProjectFile("Packages/packages-lock.json")),
            new McpResource(
                "unity://editor/selection",
                "selection",
                "Editor Selection",
                "Current editor selection as JSON.",
                "application/json",
                ReadSelection),
            new McpResource(
                "unity://scene/open-scenes",
                "open_scenes",
                "Open Scenes",
                "Open scene names, paths, active state, dirty state, and root counts.",
                "application/json",
                ReadOpenScenes),
            new McpResource(
                "unity://docs/tools",
                "tool_reference",
                "Open Unity MCP Tool Reference",
                "Markdown reference for Open Unity MCP tools.",
                "text/markdown",
                () => ReadPackageFile("Documentation~/tools.md")),
            new McpResource(
                "unity://docs/client-setup",
                "client_setup",
                "Open Unity MCP Client Setup",
                "Markdown setup instructions for MCP clients.",
                "text/markdown",
                () => ReadPackageFile("Documentation~/client-setup.md"))
        };

        static McpResourceRegistry()
        {
            Array.Sort(Resources, (left, right) => string.CompareOrdinal(left.Uri, right.Uri));
        }

        public static Dictionary<string, object> ListResources()
        {
            var resources = new List<object>();
            foreach (var resource in Resources)
            {
                resources.Add(McpJson.Object(
                    "uri", resource.Uri,
                    "name", resource.Name,
                    "title", resource.Title,
                    "description", resource.Description,
                    "mimeType", resource.MimeType));
            }

            return McpJson.Object("resources", resources);
        }

        public static Dictionary<string, object> ReadResource(string uri)
        {
            foreach (var resource in Resources)
            {
                if (!string.Equals(resource.Uri, uri, StringComparison.Ordinal))
                {
                    continue;
                }

                var text = UnityMainThread.Invoke(resource.ReadText);
                return McpJson.Object(
                    "contents", McpJson.Array(McpJson.Object(
                        "uri", resource.Uri,
                        "mimeType", resource.MimeType,
                        "text", text)));
            }

            throw new McpResourceNotFoundException(uri);
        }

        private static string ReadProjectInfo()
        {
            return McpJson.Stringify(McpJson.Object(
                "unityVersion", Application.unityVersion,
                "projectPath", UnityMcpPathUtility.ProjectRoot,
                "assetsPath", Application.dataPath,
                "packagePath", UnityMcpPathUtility.PackageRoot,
                "companyName", Application.companyName,
                "productName", Application.productName,
                "isPlaying", EditorApplication.isPlaying,
                "isPaused", EditorApplication.isPaused,
                "isCompiling", EditorApplication.isCompiling,
                "activeScene", SceneManager.GetActiveScene().path,
                "openSceneCount", SceneManager.sceneCount,
                "selectedObjectCount", Selection.objects.Length));
        }

        private static string ReadSelection()
        {
            return McpJson.Stringify(McpJson.Object(
                "count", Selection.objects.Length,
                "objects", UnityMcpEditorTools.DescribeObjects(Selection.objects)));
        }

        private static string ReadOpenScenes()
        {
            var scenes = new List<object>();
            for (var i = 0; i < SceneManager.sceneCount; i++)
            {
                var scene = SceneManager.GetSceneAt(i);
                scenes.Add(McpJson.Object(
                    "name", scene.name,
                    "path", scene.path,
                    "buildIndex", scene.buildIndex,
                    "isLoaded", scene.isLoaded,
                    "isDirty", scene.isDirty,
                    "isActive", scene == SceneManager.GetActiveScene(),
                    "rootCount", scene.rootCount));
            }

            return McpJson.Stringify(McpJson.Object(
                "activeScene", SceneManager.GetActiveScene().path,
                "scenes", scenes));
        }

        private static string ReadProjectFile(string relativePath)
        {
            var fullPath = Path.Combine(UnityMcpPathUtility.ProjectRoot, relativePath);
            return File.Exists(fullPath) ? File.ReadAllText(fullPath) : "{}";
        }

        private static string ReadPackageFile(string relativePath)
        {
            var packageInfo = UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(McpResourceRegistry).Assembly);
            if (packageInfo != null && !string.IsNullOrEmpty(packageInfo.resolvedPath))
            {
                var packagePath = Path.Combine(packageInfo.resolvedPath, relativePath.Replace('/', Path.DirectorySeparatorChar));
                if (File.Exists(packagePath))
                {
                    return File.ReadAllText(packagePath);
                }
            }

            var embeddedPath = Path.Combine(UnityMcpPathUtility.ProjectRoot, "Packages", "com.strangeape.open-unity-mcp", relativePath.Replace('/', Path.DirectorySeparatorChar));
            return File.Exists(embeddedPath) ? File.ReadAllText(embeddedPath) : string.Empty;
        }
    }

    internal sealed class McpResourceNotFoundException : Exception
    {
        public readonly string Uri;

        public McpResourceNotFoundException(string uri)
            : base("Resource not found: " + uri)
        {
            Uri = uri;
        }
    }
}
