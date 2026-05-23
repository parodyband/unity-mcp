using UnityEditor;
using UnityEngine;

namespace StrangeApe.OpenUnityMcp
{
    internal sealed class OpenUnityMcpWindow : EditorWindow
    {
        private int _port;

        [MenuItem("Tools/Open Unity MCP/Status")]
        public static void Open()
        {
            GetWindow<OpenUnityMcpWindow>("Open Unity MCP");
        }

        [MenuItem("Tools/Open Unity MCP/Start Server")]
        public static void StartServer()
        {
            OpenUnityMcpServer.Start(OpenUnityMcpSettings.Port);
        }

        [MenuItem("Tools/Open Unity MCP/Start Server", true)]
        public static bool ValidateStartServer()
        {
            return !OpenUnityMcpServer.IsRunning;
        }

        [MenuItem("Tools/Open Unity MCP/Stop Server")]
        public static void StopServer()
        {
            OpenUnityMcpServer.Stop();
        }

        [MenuItem("Tools/Open Unity MCP/Stop Server", true)]
        public static bool ValidateStopServer()
        {
            return OpenUnityMcpServer.IsRunning;
        }

        [MenuItem("Tools/Open Unity MCP/Auto Start")]
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

        private void OnEnable()
        {
            _port = OpenUnityMcpSettings.Port;
        }

        private void OnGUI()
        {
            EditorGUILayout.LabelField("Status", EditorStyles.boldLabel);
            EditorGUILayout.LabelField(OpenUnityMcpServer.IsRunning ? "Running" : "Stopped");

            using (new EditorGUI.DisabledScope(OpenUnityMcpServer.IsRunning))
            {
                _port = EditorGUILayout.IntField("Port", _port);
                if (_port != OpenUnityMcpSettings.Port)
                {
                    OpenUnityMcpSettings.Port = _port;
                    _port = OpenUnityMcpSettings.Port;
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
