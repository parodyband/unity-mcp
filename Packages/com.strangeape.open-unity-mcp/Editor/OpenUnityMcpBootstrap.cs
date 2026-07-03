using System;
using UnityEditor;

namespace StrangeApe.OpenUnityMcp
{
    [InitializeOnLoad]
    internal static class OpenUnityMcpBootstrap
    {
        private static int _pendingStartPort = -1;

        static OpenUnityMcpBootstrap()
        {
            if (IsAssetImportWorkerProcess())
            {
                // AssetImportWorker clones also run [InitializeOnLoad]; starting a listener there
                // would fight the real editor process for the port.
                return;
            }

            UnityMainThread.Initialize();
            UnityMcpReloadState.MarkAssemblyLoaded();
            EditorApplication.quitting += OpenUnityMcpServer.Stop;
            AssemblyReloadEvents.beforeAssemblyReload += BeforeAssemblyReload;

            if (OpenUnityMcpSettings.AutoStart || UnityMcpReloadState.ShouldRestartServerAfterReload)
            {
                _pendingStartPort = UnityMcpReloadState.ShouldRestartServerAfterReload
                    ? UnityMcpReloadState.ServerPortBeforeReload
                    : OpenUnityMcpSettings.Port;
                // delayCall registrations are wiped by an assembly reload, so use a one-shot
                // update handler; the pending port keeps restart intent alive if another reload
                // lands before the first update tick.
                EditorApplication.update += StartPendingServer;
            }
        }

        private static void StartPendingServer()
        {
            EditorApplication.update -= StartPendingServer;
            var port = _pendingStartPort;
            _pendingStartPort = -1;
            if (port > 0 && !OpenUnityMcpServer.IsRunning)
            {
                OpenUnityMcpServer.Start(port);
            }
        }

        private static void BeforeAssemblyReload()
        {
            var running = OpenUnityMcpServer.IsRunning;
            var restartPending = _pendingStartPort > 0;
            UnityMcpReloadState.MarkBeforeAssemblyReload(
                running || restartPending,
                running ? OpenUnityMcpServer.Port : _pendingStartPort);
            OpenUnityMcpServer.Stop();
        }

        private static bool IsAssetImportWorkerProcess()
        {
            foreach (var argument in Environment.GetCommandLineArgs())
            {
                if (argument.IndexOf("AssetImportWorker", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    string.Equals(argument, "-adb2", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
