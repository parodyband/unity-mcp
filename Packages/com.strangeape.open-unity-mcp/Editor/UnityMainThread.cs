using System;
using System.Collections.Concurrent;
using System.Threading;
using UnityEditor;

namespace StrangeApe.OpenUnityMcp
{
    internal static class UnityMainThread
    {
        public const int DefaultTimeoutSeconds = 30;

        private static readonly ConcurrentQueue<WorkItem> Work = new ConcurrentQueue<WorkItem>();
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
            return Invoke(func, DefaultTimeoutSeconds);
        }

        public static T Invoke<T>(Func<T> func, int timeoutSeconds)
        {
            if (func == null)
            {
                throw new ArgumentNullException(nameof(func));
            }

            if (_mainThreadId == Thread.CurrentThread.ManagedThreadId)
            {
                return func();
            }

            var item = new WorkItem(() => func());
            Work.Enqueue(item);
            if (!item.Done.Wait(TimeSpan.FromSeconds(Math.Max(1, timeoutSeconds))))
            {
                if (item.TryAbandon())
                {
                    throw new TimeoutException("Timed out waiting for the Unity main thread; the queued action was cancelled before it ran.");
                }

                // The action claimed the slot before the timeout landed: it is running on the main
                // thread right now and cannot be interrupted, so its side effects may still apply.
                throw new TimeoutException("Timed out waiting for the Unity main thread; the action is still running and its effects may still apply.");
            }

            if (item.Exception != null)
            {
                throw item.Exception;
            }

            return (T)item.Result;
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
            while (Work.TryDequeue(out var item))
            {
                item.Run();
            }
        }

        private sealed class WorkItem
        {
            private const int StatePending = 0;
            private const int StateRunning = 1;
            private const int StateAbandoned = 2;

            private readonly Func<object> _func;
            private int _state;

            public readonly ManualResetEventSlim Done = new ManualResetEventSlim(false);
            public object Result;
            public Exception Exception;

            public WorkItem(Func<object> func)
            {
                _func = func;
            }

            public bool TryAbandon()
            {
                return Interlocked.CompareExchange(ref _state, StateAbandoned, StatePending) == StatePending;
            }

            public void Run()
            {
                if (Interlocked.CompareExchange(ref _state, StateRunning, StatePending) != StatePending)
                {
                    // The waiter timed out and abandoned this item; running it anyway would apply
                    // side effects nobody is waiting for and double-execute retried requests.
                    return;
                }

                try
                {
                    Result = _func();
                }
                catch (Exception ex)
                {
                    Exception = ex;
                }
                finally
                {
                    Done.Set();
                }
            }
        }
    }
}
