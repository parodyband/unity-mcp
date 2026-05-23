using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace StrangeApe.OpenUnityMcp
{
    internal static class UnityMcpSceneViewTools
    {
        public static readonly McpTool SetSceneViewCamera = new McpTool(
            "unity.set_scene_view_camera",
            "Set the active Scene View camera by pivot, rotation, size, orthographic mode, optional camera position, or optional target object.",
            McpToolRegistry.ObjectSchema(
                "objectId", McpToolRegistry.StringProperty("Optional GameObject, Component, or Transform objectId to use as the view target."),
                "pivot", McpToolRegistry.Vector3Property("Optional Scene View pivot or look-at point."),
                "position", McpToolRegistry.Vector3Property("Optional desired camera world position. Requires pivot; derives rotation and size from position to pivot."),
                "rotationEuler", McpToolRegistry.Vector3Property("Optional Scene View rotation in degrees."),
                "size", McpToolRegistry.NumberProperty("Optional Scene View size or distance from pivot."),
                "orthographic", McpToolRegistry.BooleanProperty("Set orthographic Scene View mode.")),
            SetSceneViewCameraImpl);

        public static readonly McpTool FrameSceneView = new McpTool(
            "unity.frame_scene_view",
            "Frame a scene object, or the current selection, in the active Scene View.",
            McpToolRegistry.ObjectSchema(
                "objectId", McpToolRegistry.StringProperty("Optional GameObject, Component, or Transform objectId to frame. Defaults to the current selection."),
                "padding", McpToolRegistry.NumberProperty("Optional framing multiplier. Defaults to 1.25."),
                "rotationEuler", McpToolRegistry.Vector3Property("Optional Scene View rotation in degrees."),
                "orthographic", McpToolRegistry.BooleanProperty("Set orthographic Scene View mode.")),
            FrameSceneViewImpl);

        public static readonly McpTool CaptureSceneView = new McpTool(
            "unity.capture_scene_view",
            "Capture the active Scene View as a PNG and return MCP image content for vision-capable clients.",
            McpToolRegistry.ObjectSchema(
                "width", McpToolRegistry.IntegerProperty("Capture width in pixels. Defaults to 1024.", 64, 2048),
                "height", McpToolRegistry.IntegerProperty("Capture height in pixels. Defaults to 768.", 64, 2048),
                "saveToTemp", McpToolRegistry.BooleanProperty("Also save the PNG under Temp/OpenUnityMcp and return the project-relative path.")),
            CaptureSceneViewImpl);

        private static Dictionary<string, object> SetSceneViewCameraImpl(Dictionary<string, object> args)
        {
            var sceneView = GetSceneView();
            var target = ResolveTarget(args);
            var targetBounds = target != null ? CalculateBounds(target) : (Bounds?)null;

            var pivot = targetBounds.HasValue ? targetBounds.Value.center : sceneView.pivot;
            if (TryReadVector3(args, "pivot", out var explicitPivot))
            {
                pivot = explicitPivot;
            }

            var rotation = sceneView.rotation;
            if (TryReadVector3(args, "rotationEuler", out var rotationEuler))
            {
                rotation = Quaternion.Euler(rotationEuler);
            }

            var size = targetBounds.HasValue ? SizeForBounds(targetBounds.Value, 1.25f) : Mathf.Max(0.1f, sceneView.size);
            size = AsFloat(args, "size", size);

            if (TryReadVector3(args, "position", out var position))
            {
                if (!HasVector3(args, "pivot") && target == null)
                {
                    throw new ArgumentException("position requires pivot or objectId.");
                }

                var direction = pivot - position;
                if (direction.sqrMagnitude <= 0.0001f)
                {
                    throw new ArgumentException("position must be different from pivot.");
                }

                rotation = Quaternion.LookRotation(direction.normalized, Vector3.up);
                if (!args.ContainsKey("size"))
                {
                    size = Mathf.Max(0.1f, direction.magnitude);
                }
            }

            sceneView.pivot = pivot;
            sceneView.rotation = rotation;
            sceneView.size = Mathf.Max(0.1f, size);
            sceneView.orthographic = McpJson.AsBool(args, "orthographic", sceneView.orthographic);
            sceneView.Repaint();
            SceneView.RepaintAll();

            return JsonText(McpJson.Object("sceneView", DescribeSceneView(sceneView)));
        }

        private static Dictionary<string, object> FrameSceneViewImpl(Dictionary<string, object> args)
        {
            var sceneView = GetSceneView();
            var target = ResolveTarget(args) ?? Selection.activeGameObject;
            if (target == null)
            {
                return McpToolRegistry.ToolText("No objectId was provided and no GameObject is selected.", true);
            }

            var bounds = CalculateBounds(target);
            sceneView.pivot = bounds.center;
            sceneView.size = SizeForBounds(bounds, AsFloat(args, "padding", 1.25f));

            if (TryReadVector3(args, "rotationEuler", out var rotationEuler))
            {
                sceneView.rotation = Quaternion.Euler(rotationEuler);
            }

            sceneView.orthographic = McpJson.AsBool(args, "orthographic", sceneView.orthographic);
            sceneView.Repaint();
            SceneView.RepaintAll();

            return JsonText(McpJson.Object(
                "framed", UnityMcpSceneTools.DescribeGameObject(target),
                "bounds", DescribeBounds(bounds),
                "sceneView", DescribeSceneView(sceneView)));
        }

        private static Dictionary<string, object> CaptureSceneViewImpl(Dictionary<string, object> args)
        {
            var sceneView = GetSceneView();
            var width = Math.Max(64, Math.Min(2048, McpJson.AsInt(args, "width", 1024)));
            var height = Math.Max(64, Math.Min(2048, McpJson.AsInt(args, "height", 768)));
            var bytes = CaptureCameraPng(sceneView, width, height);
            var base64 = Convert.ToBase64String(bytes);
            var savedPath = string.Empty;

            if (McpJson.AsBool(args, "saveToTemp", false))
            {
                savedPath = SavePngToTemp(bytes);
            }

            var metadata = McpJson.Object(
                "width", width,
                "height", height,
                "mimeType", "image/png",
                "byteLength", bytes.Length,
                "savedPath", savedPath,
                "sceneView", DescribeSceneView(sceneView));

            return McpToolRegistry.ToolContent(new List<object>
            {
                McpJson.Object(
                    "type", "text",
                    "text", McpJson.Stringify(metadata)),
                McpJson.Object(
                    "type", "image",
                    "data", base64,
                    "mimeType", "image/png")
            });
        }

        private static SceneView GetSceneView()
        {
            var sceneView = SceneView.lastActiveSceneView;
            if (sceneView == null)
            {
                sceneView = EditorWindow.GetWindow<SceneView>();
            }

            if (sceneView == null)
            {
                throw new InvalidOperationException("Could not create or find a Scene View.");
            }

            return sceneView;
        }

        private static GameObject ResolveTarget(Dictionary<string, object> args)
        {
            var objectId = McpJson.AsString(args, "objectId");
            if (string.IsNullOrEmpty(objectId))
            {
                return null;
            }

            var target = UnityMcpObjectUtility.ResolveGameObject(objectId, null);
            if (target == null)
            {
                throw new ArgumentException("objectId is not a GameObject, Component, or Transform.");
            }

            return target;
        }

        private static byte[] CaptureCameraPng(SceneView sceneView, int width, int height)
        {
            var camera = sceneView.camera;
            if (camera == null)
            {
                throw new InvalidOperationException("Scene View camera is not available.");
            }

            var renderTexture = RenderTexture.GetTemporary(width, height, 24, RenderTextureFormat.ARGB32);
            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false);
            var previousTarget = camera.targetTexture;
            var previousActive = RenderTexture.active;

            try
            {
                camera.targetTexture = renderTexture;
                camera.Render();
                RenderTexture.active = renderTexture;
                texture.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                texture.Apply();
                return texture.EncodeToPNG();
            }
            finally
            {
                camera.targetTexture = previousTarget;
                RenderTexture.active = previousActive;
                RenderTexture.ReleaseTemporary(renderTexture);
                UnityEngine.Object.DestroyImmediate(texture);
            }
        }

        private static string SavePngToTemp(byte[] bytes)
        {
            var directory = Path.Combine(UnityMcpPathUtility.ProjectRoot, "Temp", "OpenUnityMcp");
            Directory.CreateDirectory(directory);
            var fileName = "scene-view-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + ".png";
            var fullPath = Path.Combine(directory, fileName);
            File.WriteAllBytes(fullPath, bytes);
            return UnityMcpPathUtility.NormalizeProjectRelativePath(Path.Combine("Temp", "OpenUnityMcp", fileName));
        }

        private static Bounds CalculateBounds(GameObject root)
        {
            var hasBounds = false;
            var bounds = new Bounds(root.transform.position, Vector3.one);

            foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                if (!hasBounds)
                {
                    bounds = renderer.bounds;
                    hasBounds = true;
                }
                else
                {
                    bounds.Encapsulate(renderer.bounds);
                }
            }

            foreach (var collider in root.GetComponentsInChildren<Collider>(true))
            {
                if (!hasBounds)
                {
                    bounds = collider.bounds;
                    hasBounds = true;
                }
                else
                {
                    bounds.Encapsulate(collider.bounds);
                }
            }

            if (!hasBounds)
            {
                foreach (var transform in root.GetComponentsInChildren<Transform>(true))
                {
                    bounds.Encapsulate(transform.position);
                }
            }

            return bounds;
        }

        private static float SizeForBounds(Bounds bounds, float padding)
        {
            return Mathf.Max(0.1f, bounds.extents.magnitude * Mathf.Max(0.1f, padding));
        }

        private static Dictionary<string, object> DescribeSceneView(SceneView sceneView)
        {
            var payload = McpJson.Object(
                "pivot", Vector3Payload(sceneView.pivot),
                "rotationEuler", Vector3Payload(sceneView.rotation.eulerAngles),
                "size", sceneView.size,
                "orthographic", sceneView.orthographic);

            var camera = sceneView.camera;
            if (camera != null)
            {
                payload["camera"] = McpJson.Object(
                    "position", Vector3Payload(camera.transform.position),
                    "rotationEuler", Vector3Payload(camera.transform.rotation.eulerAngles),
                    "fieldOfView", camera.fieldOfView,
                    "nearClipPlane", camera.nearClipPlane,
                    "farClipPlane", camera.farClipPlane);
            }

            return payload;
        }

        private static Dictionary<string, object> DescribeBounds(Bounds bounds)
        {
            return McpJson.Object(
                "center", Vector3Payload(bounds.center),
                "size", Vector3Payload(bounds.size),
                "extents", Vector3Payload(bounds.extents));
        }

        private static Dictionary<string, object> Vector3Payload(Vector3 value)
        {
            return McpJson.Object(
                "x", value.x,
                "y", value.y,
                "z", value.z);
        }

        private static bool HasVector3(Dictionary<string, object> args, string key)
        {
            return args != null && args.TryGetValue(key, out var rawValue) && rawValue != null;
        }

        private static bool TryReadVector3(Dictionary<string, object> args, string key, out Vector3 value)
        {
            value = default;
            if (!HasVector3(args, key))
            {
                return false;
            }

            var rawValue = args[key];
            if (rawValue is Dictionary<string, object> map)
            {
                value = new Vector3(
                    ReadFloat(map, "x"),
                    ReadFloat(map, "y"),
                    ReadFloat(map, "z"));
                return true;
            }

            if (rawValue is List<object> array)
            {
                if (array.Count != 3)
                {
                    throw new ArgumentException(key + " array must have exactly three numeric values.");
                }

                value = new Vector3(
                    Convert.ToSingle(array[0], CultureInfo.InvariantCulture),
                    Convert.ToSingle(array[1], CultureInfo.InvariantCulture),
                    Convert.ToSingle(array[2], CultureInfo.InvariantCulture));
                return true;
            }

            throw new ArgumentException(key + " must be an object with x, y, z or an array of three numbers.");
        }

        private static float ReadFloat(Dictionary<string, object> map, string key)
        {
            if (!map.TryGetValue(key, out var value) || value == null)
            {
                throw new ArgumentException("Vector3 is missing component: " + key);
            }

            return Convert.ToSingle(value, CultureInfo.InvariantCulture);
        }

        private static float AsFloat(Dictionary<string, object> args, string key, float fallback)
        {
            if (args == null || !args.TryGetValue(key, out var value) || value == null)
            {
                return fallback;
            }

            return Convert.ToSingle(value, CultureInfo.InvariantCulture);
        }

        private static Dictionary<string, object> JsonText(Dictionary<string, object> payload)
        {
            return McpToolRegistry.ToolText(McpJson.Stringify(payload));
        }
    }
}
