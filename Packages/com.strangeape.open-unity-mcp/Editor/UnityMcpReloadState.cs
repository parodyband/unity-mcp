using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEditor;

namespace StrangeApe.OpenUnityMcp
{
    internal static class UnityMcpReloadState
    {
        private const string AssemblyLoadSequenceKey = "StrangeApe.OpenUnityMcp.AssemblyLoadSequence";
        private const string LastAssemblyReloadStartedTimeKey = "StrangeApe.OpenUnityMcp.LastAssemblyReloadStartedTime";
        private const string LastAssemblyLoadCompletedTimeKey = "StrangeApe.OpenUnityMcp.LastAssemblyLoadCompletedTime";
        private const string LastScriptCompilationRequestTimeKey = "StrangeApe.OpenUnityMcp.LastScriptCompilationRequestTime";
        private const string LastScriptCompilationRequestSequenceKey = "StrangeApe.OpenUnityMcp.LastScriptCompilationRequestSequence";
        private const string ServerWasRunningBeforeReloadKey = "StrangeApe.OpenUnityMcp.ServerWasRunningBeforeReload";
        private const string ServerPortBeforeReloadKey = "StrangeApe.OpenUnityMcp.ServerPortBeforeReload";

        public static void MarkAssemblyLoaded()
        {
            SessionState.SetInt(AssemblyLoadSequenceKey, AssemblyLoadSequence + 1);
            SessionState.SetString(LastAssemblyLoadCompletedTimeKey, CurrentEditorTime());
        }

        public static bool ShouldRestartServerAfterReload => SessionState.GetBool(ServerWasRunningBeforeReloadKey, false);

        public static int ServerPortBeforeReload => SessionState.GetInt(ServerPortBeforeReloadKey, OpenUnityMcpSettings.Port);

        public static void MarkBeforeAssemblyReload(bool serverRunning, int serverPort)
        {
            SessionState.SetString(LastAssemblyReloadStartedTimeKey, CurrentEditorTime());
            SessionState.SetBool(ServerWasRunningBeforeReloadKey, serverRunning);
            if (serverRunning && serverPort > 0)
            {
                SessionState.SetInt(ServerPortBeforeReloadKey, serverPort);
            }
        }

        public static void MarkScriptCompilationRequested()
        {
            SessionState.SetString(LastScriptCompilationRequestTimeKey, CurrentEditorTime());
            SessionState.SetInt(LastScriptCompilationRequestSequenceKey, AssemblyLoadSequence);
        }

        public static void AddToPayload(Dictionary<string, object> payload)
        {
            if (payload == null)
            {
                throw new ArgumentNullException(nameof(payload));
            }

            var requestSequence = SessionState.GetInt(LastScriptCompilationRequestSequenceKey, -1);
            payload["assemblyLoadSequence"] = AssemblyLoadSequence;
            payload["lastAssemblyReloadStartedTime"] = GetOptionalTime(LastAssemblyReloadStartedTimeKey);
            payload["lastAssemblyLoadCompletedTime"] = GetOptionalTime(LastAssemblyLoadCompletedTimeKey);
            payload["lastScriptCompilationRequestTime"] = GetOptionalTime(LastScriptCompilationRequestTimeKey);
            payload["serverReloadedSinceLastScriptCompilationRequest"] = requestSequence >= 0 && AssemblyLoadSequence > requestSequence;
            payload["serverWasRunningBeforeLastAssemblyReload"] = ShouldRestartServerAfterReload;
        }

        private static int AssemblyLoadSequence => SessionState.GetInt(AssemblyLoadSequenceKey, 0);

        private static string CurrentEditorTime()
        {
            return EditorApplication.timeSinceStartup.ToString("R", CultureInfo.InvariantCulture);
        }

        private static object GetOptionalTime(string key)
        {
            var value = SessionState.GetString(key, string.Empty);
            return string.IsNullOrEmpty(value) ? null : value;
        }
    }
}
