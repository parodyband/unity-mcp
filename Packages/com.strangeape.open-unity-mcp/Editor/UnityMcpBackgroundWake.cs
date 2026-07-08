#if UNITY_EDITOR_WIN
using System;
using System.Runtime.InteropServices;
using UnityEditor;
#endif

namespace StrangeApe.OpenUnityMcp
{
    /// <summary>
    /// On Windows, an unfocused editor parks its tick loop, so the
    /// <c>EditorApplication.update</c> pump stops and queued MCP requests sit until the
    /// window regains focus. The main thread keeps pumping OS window messages while
    /// backgrounded, so a Win32 <c>SetTimer</c> callback still fires there; it requests a
    /// player-loop update to keep the editor ticking while the server is running.
    /// No-op on macOS/Linux, where the editor keeps ticking in the background.
    /// </summary>
    internal static class UnityMcpBackgroundWake
    {
#if UNITY_EDITOR_WIN
        private const uint IntervalMilliseconds = 100;

        private delegate void TimerProc(IntPtr hWnd, uint uMsg, UIntPtr nIDEvent, uint dwTime);

        [DllImport("user32.dll")]
        private static extern UIntPtr SetTimer(IntPtr hWnd, UIntPtr nIDEvent, uint uElapse, TimerProc lpTimerFunc);

        [DllImport("user32.dll")]
        private static extern bool KillTimer(IntPtr hWnd, UIntPtr uIDEvent);

        // Rooted in a static field so the native timer never invokes a collected delegate.
        private static readonly TimerProc Callback = OnTimer;
        private static UIntPtr _timerId;

        /// <summary>Main thread only: the timer binds to the calling thread's message queue.</summary>
        public static void Start()
        {
            if (_timerId != UIntPtr.Zero)
            {
                return;
            }

            _timerId = SetTimer(IntPtr.Zero, UIntPtr.Zero, IntervalMilliseconds, Callback);
        }

        /// <summary>
        /// Main thread only. Must run before every assembly reload while started (the server
        /// stops in <c>beforeAssemblyReload</c>), or the native timer would call into an
        /// unloaded domain and crash the editor.
        /// </summary>
        public static void Stop()
        {
            if (_timerId == UIntPtr.Zero)
            {
                return;
            }

            KillTimer(IntPtr.Zero, _timerId);
            _timerId = UIntPtr.Zero;
        }

        private static void OnTimer(IntPtr hWnd, uint uMsg, UIntPtr nIDEvent, uint dwTime)
        {
            // A managed exception escaping a native callback would crash the editor.
            try
            {
                EditorApplication.QueuePlayerLoopUpdate();
            }
            catch
            {
            }
        }
#else
        public static void Start()
        {
        }

        public static void Stop()
        {
        }
#endif
    }
}
