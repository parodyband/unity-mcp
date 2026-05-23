using UnityEditor;
using UnityEditor.Overlays;
using UnityEditor.Toolbars;
using UnityEngine;
using UnityEngine.UIElements;

namespace StrangeApe.OpenUnityMcp
{
    [Overlay(typeof(SceneView), OverlayId, "Open Unity MCP", defaultDisplay = true)]
    [Icon("d_BuildSettings.WebGL.Small")]
    internal sealed class OpenUnityMcpOverlay : ToolbarOverlay
    {
        public const string OverlayId = "open-unity-mcp";

        private const string StatusElementId = "open-unity-mcp/status";
        private const string ToggleServerElementId = "open-unity-mcp/toggle";
        private const string OpenWindowElementId = "open-unity-mcp/open-window";

        private OpenUnityMcpOverlay()
            : base(StatusElementId, ToggleServerElementId, OpenWindowElementId)
        {
        }

        [EditorToolbarElement(StatusElementId, typeof(SceneView))]
        private sealed class StatusElement : VisualElement
        {
            private static readonly Color RunningColor = new Color(0.30f, 0.85f, 0.40f);
            private static readonly Color StoppedColor = new Color(0.55f, 0.55f, 0.55f);

            private readonly VisualElement _dot = new VisualElement();
            private readonly Label _label = new Label();

            public StatusElement()
            {
                AddToClassList("unity-editor-toolbar-element");
                style.flexDirection = FlexDirection.Row;
                style.alignItems = Align.Center;
                style.paddingLeft = 6;
                style.paddingRight = 6;

                _dot.style.width = 8;
                _dot.style.height = 8;
                _dot.style.borderTopLeftRadius = 4;
                _dot.style.borderTopRightRadius = 4;
                _dot.style.borderBottomLeftRadius = 4;
                _dot.style.borderBottomRightRadius = 4;
                _dot.style.marginRight = 6;
                Add(_dot);

                _label.style.unityFontStyleAndWeight = FontStyle.Bold;
                Add(_label);

                RegisterCallback<AttachToPanelEvent>(_ =>
                {
                    Refresh();
                    schedule.Execute(Refresh).Every(500);
                });
            }

            private void Refresh()
            {
                var running = OpenUnityMcpServer.IsRunning;
                _dot.style.backgroundColor = running ? RunningColor : StoppedColor;
                _label.text = running ? "MCP " + OpenUnityMcpSettings.Port : "MCP off";
                tooltip = running
                    ? "Open Unity MCP running at " + OpenUnityMcpSettings.Endpoint
                    : "Open Unity MCP stopped";
            }
        }

        [EditorToolbarElement(ToggleServerElementId, typeof(SceneView))]
        private sealed class ToggleServerElement : EditorToolbarButton
        {
            public ToggleServerElement()
            {
                clicked += Toggle;
                RegisterCallback<AttachToPanelEvent>(_ =>
                {
                    Refresh();
                    schedule.Execute(Refresh).Every(500);
                });
            }

            private void Toggle()
            {
                if (OpenUnityMcpServer.IsRunning)
                {
                    OpenUnityMcpServer.Stop();
                }
                else
                {
                    OpenUnityMcpServer.Start(OpenUnityMcpSettings.Port);
                }

                Refresh();
            }

            private void Refresh()
            {
                if (OpenUnityMcpServer.IsRunning)
                {
                    text = "Stop";
                    tooltip = "Stop the Open Unity MCP server";
                    icon = EditorGUIUtility.IconContent("PreMatCube").image as Texture2D;
                }
                else
                {
                    text = "Start";
                    tooltip = "Start the Open Unity MCP server on port " + OpenUnityMcpSettings.Port;
                    icon = EditorGUIUtility.IconContent("PlayButton").image as Texture2D;
                }
            }
        }

        [EditorToolbarElement(OpenWindowElementId, typeof(SceneView))]
        private sealed class OpenWindowElement : EditorToolbarButton
        {
            public OpenWindowElement()
            {
                tooltip = "Open the Open Unity MCP window";
                icon = EditorGUIUtility.IconContent("d_SettingsIcon").image as Texture2D;
                clicked += OpenUnityMcpWindow.Open;
            }
        }
    }
}
