using UnityEditor;

namespace StrangeApe.OpenUnityMcp
{
    internal static class OpenUnityMcpSettings
    {
        public const int DefaultPort = 8080;

        private const string AutoStartKey = "StrangeApe.OpenUnityMcp.AutoStart";
        private const string PortKey = "StrangeApe.OpenUnityMcp.Port";
        private const string DisabledToolKeyPrefix = "StrangeApe.OpenUnityMcp.ToolDisabled.";

        public static bool AutoStart
        {
            get => EditorPrefs.GetBool(AutoStartKey, false);
            set => EditorPrefs.SetBool(AutoStartKey, value);
        }

        public static int Port
        {
            get => EditorPrefs.GetInt(PortKey, DefaultPort);
            set => EditorPrefs.SetInt(PortKey, SanitizePort(value));
        }

        public static string Endpoint => "http://127.0.0.1:" + Port + "/mcp";

        public static bool IsToolEnabled(string toolName)
        {
            if (string.IsNullOrEmpty(toolName))
            {
                return true;
            }

            return !EditorPrefs.GetBool(DisabledToolKeyPrefix + toolName, false);
        }

        public static void SetToolEnabled(string toolName, bool enabled)
        {
            if (string.IsNullOrEmpty(toolName))
            {
                return;
            }

            var key = DisabledToolKeyPrefix + toolName;
            if (enabled)
            {
                EditorPrefs.DeleteKey(key);
            }
            else
            {
                EditorPrefs.SetBool(key, true);
            }
        }

        private static int SanitizePort(int port)
        {
            if (port < 1024 || port > 65535)
            {
                return DefaultPort;
            }

            return port;
        }
    }
}

