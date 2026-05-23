using System;
using System.Collections.Concurrent;
using System.Threading;
using UnityEditor;

namespace StrangeApe.OpenUnityMcp
{
    internal static class UnityMainThread
    {
        private static readonly ConcurrentQueue<Action> Work = new ConcurrentQueue<Action>();
        private static int _mainThreadId;
        private static bool _initialized;

        public static void Initialize()
        {
            if (_initialized)
            {
                return;
            }

            _mainThreadId = Thread.CurrentThread.ManagedThreadId;
            EditorApplication.update += Pump;
            AssemblyReloadEvents.beforeAssemblyReload += Pump;
            _initialized = true;
        }

        public static T Invoke<T>(Func<T> func)
        {
            if (func == null)
            {
                throw new ArgumentNullException(nameof(func));
            }

            if (_mainThreadId == Thread.CurrentThread.ManagedThreadId)
            {
                return func();
            }

            Exception exception = null;
            var result = default(T);
            using (var done = new ManualResetEventSlim(false))
            {
                Work.Enqueue(() =>
                {
                    try
                    {
                        result = func();
                    }
                    catch (Exception ex)
                    {
                        exception = ex;
                    }
                    finally
                    {
                        done.Set();
                    }
                });

                if (!done.Wait(TimeSpan.FromSeconds(30)))
                {
                    throw new TimeoutException("Timed out waiting for Unity main thread.");
                }
            }

            if (exception != null)
            {
                throw exception;
            }

            return result;
        }

        public static void Invoke(Action action)
        {
            Invoke(() =>
            {
                action();
                return true;
            });
        }

        private static void Pump()
        {
            while (Work.TryDequeue(out var action))
            {
                action();
            }
        }
    }
}

