using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace StrangeApe.OpenUnityMcp
{
    internal static class OpenUnityMcpPreferences
    {
        private const string PreferencesPath = "Preferences/Open Unity MCP";

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
            OpenUnityMcpClientSetup.DrawClientSetupGui();
            EditorGUILayout.Space();

            EditorGUILayout.HelpBox("The server binds to 127.0.0.1 only. Configure your MCP client to use the endpoint above.", MessageType.Info);
        }
    }
}
