using UnityEditor;

namespace StrangeApe.OpenUnityMcp
{
    [InitializeOnLoad]
    internal static class OpenUnityMcpBootstrap
    {
        static OpenUnityMcpBootstrap()
        {
            UnityMainThread.Initialize();
            UnityMcpReloadState.MarkAssemblyLoaded();
            EditorApplication.quitting += OpenUnityMcpServer.Stop;
            AssemblyReloadEvents.beforeAssemblyReload += BeforeAssemblyReload;

            if (OpenUnityMcpSettings.AutoStart || UnityMcpReloadState.ShouldRestartServerAfterReload)
            {
                var port = UnityMcpReloadState.ShouldRestartServerAfterReload
                    ? UnityMcpReloadState.ServerPortBeforeReload
                    : OpenUnityMcpSettings.Port;
                EditorApplication.delayCall += () => OpenUnityMcpServer.Start(port);
            }
        }

        private static void BeforeAssemblyReload()
        {
            UnityMcpReloadState.MarkBeforeAssemblyReload(OpenUnityMcpServer.IsRunning, OpenUnityMcpServer.Port);
            OpenUnityMcpServer.Stop();
        }
    }
}
