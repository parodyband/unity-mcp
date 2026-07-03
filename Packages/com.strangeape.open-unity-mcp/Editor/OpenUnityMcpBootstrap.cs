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
            EditorApplication.quitting += OnEditorQuitting;
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
            var reloadPort = running ? OpenUnityMcpServer.Port : _pendingStartPort;
            UnityMcpReloadState.MarkBeforeAssemblyReload(
                running || restartPending,
                reloadPort);

            // Tell the sidecar this is a reload (hold and retry), not a shutdown, so
            // it waits the outage out instead of surfacing a connection error. Only
            // meaningful when the server was actually up or slated to restart.
            if (running || restartPending)
            {
                UnityMcpStatusFile.WriteReloading(reloadPort > 0 ? reloadPort : OpenUnityMcpSettings.Port);
            }

            OpenUnityMcpServer.Stop();
        }

        private static void OnEditorQuitting()
        {
            // A clean quit is a terminal signal for the sidecar: mark stopped so it
            // stops waiting for a server that will not come back on its own.
            UnityMcpStatusFile.WriteStopped(OpenUnityMcpServer.IsRunning ? OpenUnityMcpServer.Port : OpenUnityMcpSettings.Port);
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
