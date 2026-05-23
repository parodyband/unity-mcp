using UnityEditor;

namespace StrangeApe.OpenUnityMcp
{
    [InitializeOnLoad]
    internal static class OpenUnityMcpBootstrap
    {
        static OpenUnityMcpBootstrap()
        {
            UnityMainThread.Initialize();
            EditorApplication.quitting += OpenUnityMcpServer.Stop;
            AssemblyReloadEvents.beforeAssemblyReload += OpenUnityMcpServer.Stop;

            if (OpenUnityMcpSettings.AutoStart)
            {
                EditorApplication.delayCall += () => OpenUnityMcpServer.Start(OpenUnityMcpSettings.Port);
            }
        }
    }
}

