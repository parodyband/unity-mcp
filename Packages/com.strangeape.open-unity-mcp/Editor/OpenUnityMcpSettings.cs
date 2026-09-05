using System.Security.Cryptography;
using UnityEditor;

namespace StrangeApe.OpenUnityMcp
{
    internal static class OpenUnityMcpSettings
    {
        public const int DefaultPort = 8080;

        private const string AutoStartKey = "StrangeApe.OpenUnityMcp.AutoStart";
        private const string CompactToolListKey = "StrangeApe.OpenUnityMcp.CompactToolList";

        public static bool CompactToolList
        {
            get => EditorPrefs.GetBool(CompactToolListKey, true);
            set => EditorPrefs.SetBool(CompactToolListKey, value);
        }
        private const string PortKey = "StrangeApe.OpenUnityMcp.Port";
        private const string DisabledToolKeyPrefix = "StrangeApe.OpenUnityMcp.ToolDisabled.";
        private const string RequireAccessTokenKey = "StrangeApe.OpenUnityMcp.RequireAccessToken";
        private const string AccessTokenKey = "StrangeApe.OpenUnityMcp.AccessToken";

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

        // Opt-in bearer-token auth for POST /mcp. Default OFF: the local-loopback
        // origin check remains the only gate unless a user explicitly turns this on.
        // Read on the main thread and snapshotted into the server on Start — never
        // read per-request off the accept-loop's ThreadPool threads.
        public static bool RequireAccessToken
        {
            get => EditorPrefs.GetBool(RequireAccessTokenKey, false);
            set => EditorPrefs.SetBool(RequireAccessTokenKey, value);
        }

        // A stable per-user+project shared secret. Generated once on first read and
        // persisted, so it survives editor restarts and client configs never go
        // stale. 32 random bytes rendered as 64 lowercase hex characters.
        public static string AccessToken
        {
            get
            {
                var token = EditorPrefs.GetString(AccessTokenKey, string.Empty);
                if (string.IsNullOrEmpty(token))
                {
                    token = GenerateToken();
                    EditorPrefs.SetString(AccessTokenKey, token);
                }

                return token;
            }
        }

        // Replaces the persisted token with a fresh value. Clients configured with
        // the old token must re-copy it; the sidecar picks the new one up from the
        // status file on the next server start.
        public static string RegenerateAccessToken()
        {
            var token = GenerateToken();
            EditorPrefs.SetString(AccessTokenKey, token);
            return token;
        }

        private static string GenerateToken()
        {
            var bytes = new byte[32];
            using (var rng = RandomNumberGenerator.Create())
            {
                rng.GetBytes(bytes);
            }

            var chars = new char[bytes.Length * 2];
            for (var i = 0; i < bytes.Length; i++)
            {
                var b = bytes[i];
                chars[i * 2] = HexDigit(b >> 4);
                chars[i * 2 + 1] = HexDigit(b & 0xF);
            }

            return new string(chars);
        }

        private static char HexDigit(int value)
        {
            return (char)(value < 10 ? '0' + value : 'a' + (value - 10));
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

