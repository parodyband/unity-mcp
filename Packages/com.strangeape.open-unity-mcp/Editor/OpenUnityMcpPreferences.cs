using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace StrangeApe.OpenUnityMcp
{
    internal static class OpenUnityMcpPreferences
    {
        private const string PreferencesPath = "Preferences/Open Unity MCP";

        private static bool _toolsFoldout = true;
        private static Vector2 _toolsScroll;
        private static string _toolsFilter = string.Empty;
        private static bool _revealToken;

        [SettingsProvider]
        public static SettingsProvider CreateSettingsProvider()
        {
            return new SettingsProvider(PreferencesPath, SettingsScope.User)
            {
                label = "Open Unity MCP",
                guiHandler = _ => DrawPreferencesGui(),
                keywords = new HashSet<string>
                {
                    "open unity mcp",
                    "mcp",
                    "server",
                    "claude",
                    "codex"
                }
            };
        }

        [MenuItem("Tools/Open Unity MCP/Preferences", false, 0)]
        public static void Open()
        {
            SettingsService.OpenUserPreferences(PreferencesPath);
        }

        [MenuItem("Tools/Open Unity MCP/Start Server", false, 20)]
        public static void StartServer()
        {
            OpenUnityMcpServer.Start(OpenUnityMcpSettings.Port);
        }

        [MenuItem("Tools/Open Unity MCP/Start Server", true)]
        public static bool ValidateStartServer()
        {
            return !OpenUnityMcpServer.IsRunning;
        }

        [MenuItem("Tools/Open Unity MCP/Stop Server", false, 21)]
        public static void StopServer()
        {
            OpenUnityMcpServer.Stop();
        }

        [MenuItem("Tools/Open Unity MCP/Stop Server", true)]
        public static bool ValidateStopServer()
        {
            return OpenUnityMcpServer.IsRunning;
        }

        [MenuItem("Tools/Open Unity MCP/Auto Start", false, 40)]
        public static void ToggleAutoStart()
        {
            OpenUnityMcpSettings.AutoStart = !OpenUnityMcpSettings.AutoStart;
            Menu.SetChecked("Tools/Open Unity MCP/Auto Start", OpenUnityMcpSettings.AutoStart);
        }

        [MenuItem("Tools/Open Unity MCP/Auto Start", true)]
        public static bool ValidateToggleAutoStart()
        {
            Menu.SetChecked("Tools/Open Unity MCP/Auto Start", OpenUnityMcpSettings.AutoStart);
            return true;
        }

        private static void DrawPreferencesGui()
        {
            EditorGUILayout.LabelField("Server", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("Status", OpenUnityMcpServer.IsRunning ? "Running" : "Stopped");

            using (new EditorGUI.DisabledScope(OpenUnityMcpServer.IsRunning))
            {
                var port = EditorGUILayout.DelayedIntField("Port", OpenUnityMcpSettings.Port);
                if (port != OpenUnityMcpSettings.Port)
                {
                    OpenUnityMcpSettings.Port = port;
                }
            }

            EditorGUILayout.LabelField("Endpoint", OpenUnityMcpSettings.Endpoint);
            EditorGUILayout.Space();

            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(OpenUnityMcpServer.IsRunning))
                {
                    if (GUILayout.Button("Start"))
                    {
                        OpenUnityMcpServer.Start(OpenUnityMcpSettings.Port);
                    }
                }

                using (new EditorGUI.DisabledScope(!OpenUnityMcpServer.IsRunning))
                {
                    if (GUILayout.Button("Stop"))
                    {
                        OpenUnityMcpServer.Stop();
                    }
                }
            }

            var autoStart = EditorGUILayout.Toggle("Auto Start", OpenUnityMcpSettings.AutoStart);
            if (autoStart != OpenUnityMcpSettings.AutoStart)
            {
                OpenUnityMcpSettings.AutoStart = autoStart;
            }

            EditorGUILayout.Space();
            DrawAccessTokenGui();
            EditorGUILayout.Space();
            DrawToolsGui();
            EditorGUILayout.Space();
            OpenUnityMcpClientSetup.DrawClientSetupGui();
            EditorGUILayout.Space();

            EditorGUILayout.HelpBox("The server binds to 127.0.0.1 only. Configure your MCP client to use the endpoint above.", MessageType.Info);
        }

        private static void DrawAccessTokenGui()
        {
            EditorGUILayout.LabelField("Access Token", EditorStyles.boldLabel);

            var require = EditorGUILayout.Toggle(
                new GUIContent("Require Access Token", "When enabled, POST /mcp requires the access token below. /health stays open."),
                OpenUnityMcpSettings.RequireAccessToken);
            if (require != OpenUnityMcpSettings.RequireAccessToken)
            {
                OpenUnityMcpSettings.RequireAccessToken = require;
            }

            // Reading AccessToken lazily generates+persists it on first access, so the
            // token shown here is stable and matches what the server enforces.
            var token = OpenUnityMcpSettings.AccessToken;
            var display = _revealToken ? token : MaskToken(token);

            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField("Token", GUILayout.Width(90f));
                EditorGUILayout.SelectableLabel(display, EditorStyles.textField, GUILayout.Height(EditorGUIUtility.singleLineHeight));
                _revealToken = GUILayout.Toggle(_revealToken, _revealToken ? "Hide" : "Show", "Button", GUILayout.Width(50f));
                if (GUILayout.Button("Copy", GUILayout.Width(50f)))
                {
                    EditorGUIUtility.systemCopyBuffer = token;
                }

                if (GUILayout.Button("Regenerate", GUILayout.Width(90f)))
                {
                    if (EditorUtility.DisplayDialog(
                            "Regenerate Access Token",
                            "Generate a new access token? Any client configured with the old token will be rejected until it re-copies the new one. The change applies on the next server restart.",
                            "Regenerate",
                            "Cancel"))
                    {
                        OpenUnityMcpSettings.RegenerateAccessToken();
                    }
                }
            }

            EditorGUILayout.HelpBox(
                "Off by default: /mcp is gated only by a loopback-origin check. When enabled, clients must send " +
                "'Authorization: Bearer <token>' (or 'X-Open-Unity-Mcp-Token: <token>'). The bundled stdio sidecar reads " +
                "the token automatically from Temp/OpenUnityMcp/server-status.json — no client changes are needed. " +
                "Changes to this setting or the token apply on the next server restart.",
                MessageType.None);
        }

        private static string MaskToken(string token)
        {
            if (string.IsNullOrEmpty(token))
            {
                return string.Empty;
            }

            if (token.Length <= 8)
            {
                return new string('•', token.Length);
            }

            return token.Substring(0, 4) + new string('•', token.Length - 8) + token.Substring(token.Length - 4);
        }

        private static void DrawToolsGui()
        {
            OpenUnityMcpSettings.CompactToolList = EditorGUILayout.Toggle(
                new GUIContent("Compact Tool Catalog", "Advertise workflow tools; discover and call other enabled tools on demand. Reconnect your client after changing."),
                OpenUnityMcpSettings.CompactToolList);
            _toolsFoldout = EditorGUILayout.Foldout(_toolsFoldout, "Tools", true, EditorStyles.foldoutHeader);
            if (!_toolsFoldout)
            {
                return;
            }

            EditorGUILayout.HelpBox("Disabled tools are hidden from MCP clients and rejected if called. Restart or reconnect your client after toggling so it refreshes its tool list.", MessageType.None);

            var toolNames = new List<string>(McpToolRegistry.AllToolNames);

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Enable All", GUILayout.Width(90)))
                {
                    foreach (var name in toolNames)
                    {
                        OpenUnityMcpSettings.SetToolEnabled(name, true);
                    }
                }

                if (GUILayout.Button("Disable All", GUILayout.Width(90)))
                {
                    foreach (var name in toolNames)
                    {
                        OpenUnityMcpSettings.SetToolEnabled(name, false);
                    }
                }

                GUILayout.FlexibleSpace();
                EditorGUILayout.LabelField("Filter", GUILayout.Width(40));
                _toolsFilter = EditorGUILayout.TextField(_toolsFilter, GUILayout.MinWidth(120));
            }

            using (var scroll = new EditorGUILayout.ScrollViewScope(_toolsScroll, GUILayout.MinHeight(160), GUILayout.MaxHeight(320)))
            {
                _toolsScroll = scroll.scrollPosition;
                var filter = _toolsFilter == null ? string.Empty : _toolsFilter.Trim();
                foreach (var name in toolNames)
                {
                    if (filter.Length > 0 && name.IndexOf(filter, System.StringComparison.OrdinalIgnoreCase) < 0)
                    {
                        continue;
                    }

                    var enabled = OpenUnityMcpSettings.IsToolEnabled(name);
                    var description = McpToolRegistry.GetToolDescription(name);
                    var label = new GUIContent(name, description);
                    var newValue = EditorGUILayout.ToggleLeft(label, enabled);
                    if (newValue != enabled)
                    {
                        OpenUnityMcpSettings.SetToolEnabled(name, newValue);
                    }
                }
            }
        }
    }
}
