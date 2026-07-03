using System;
using System.Globalization;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace StrangeApe.OpenUnityMcp
{
    // Writes Temp/OpenUnityMcp/server-status.json so the external sidecar can tell
    // "the editor is rebooting, hold" apart from "the editor is gone, give up".
    //
    // Every write is best-effort inside try/catch: status reporting must never be
    // able to break a domain reload or an editor quit. The project root is cached
    // at load time on the main thread so writes triggered from a background thread
    // (e.g. the server accept loop) never touch Application.dataPath off-thread.
    internal static class UnityMcpStatusFile
    {
        private const string DirectoryName = "OpenUnityMcp";
        private const string FileName = "server-status.json";

        private static readonly object Gate = new object();
        private static string _statusFilePath;

        [InitializeOnLoadMethod]
        private static void CaptureProjectRoot()
        {
            try
            {
                var directory = Path.Combine(UnityMcpPathUtility.ProjectRoot, "Temp", DirectoryName);
                _statusFilePath = Path.Combine(directory, FileName);
            }
            catch
            {
                _statusFilePath = null;
            }
        }

        public static void WriteRunning(int port, string accessToken)
        {
            // The token is always included (whether or not enforcement is on) so the
            // sidecar can attach it unconditionally. Temp/ is per-user and gitignored,
            // the same trust boundary as the editor itself.
            var tokenJson = string.IsNullOrEmpty(accessToken)
                ? string.Empty
                : "\"token\":\"" + EscapeJsonString(accessToken) + "\",";
            Write("running", port, "\"endpoint\":\"http://127.0.0.1:" + port + "/mcp\"," + tokenJson);
        }

        public static void WriteReloading(int port)
        {
            Write("reloading", port, string.Empty);
        }

        public static void WriteStopped(int port)
        {
            Write("stopped", port, string.Empty);
        }

        private static void Write(string state, int port, string extraJson)
        {
            var path = _statusFilePath;
            if (string.IsNullOrEmpty(path))
            {
                return;
            }

            try
            {
                var directory = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture);
                var json = "{\"state\":\"" + state + "\",\"port\":" + port.ToString(CultureInfo.InvariantCulture) + "," +
                           extraJson + "\"timestamp\":" + timestamp + "}";

                lock (Gate)
                {
                    File.WriteAllText(path, json);
                }
            }
            catch (Exception ex)
            {
                // Status reporting is advisory only; a failure here must not disturb
                // server start/stop, reloads, or quit.
                Debug.LogWarning("[Open Unity MCP] Failed to write status file: " + ex.Message);
            }
        }

        // The access token is 64 hex characters, so this never actually escapes
        // anything today; kept as a guard so the status JSON stays valid if the
        // token format ever changes.
        private static string EscapeJsonString(string value)
        {
            var builder = new System.Text.StringBuilder(value.Length);
            foreach (var c in value)
            {
                switch (c)
                {
                    case '"':
                        builder.Append("\\\"");
                        break;
                    case '\\':
                        builder.Append("\\\\");
                        break;
                    default:
                        builder.Append(c);
                        break;
                }
            }

            return builder.ToString();
        }
    }
}
