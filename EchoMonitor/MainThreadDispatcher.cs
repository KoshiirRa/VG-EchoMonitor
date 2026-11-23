using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace EchoMonitor
{
    public sealed class MainThreadDispatcher
    {
        private readonly ConcurrentQueue<Action> _queue = new ConcurrentQueue<Action>();
        private readonly int _mainThreadId;

        public MainThreadDispatcher()
        {
            _mainThreadId = Thread.CurrentThread.ManagedThreadId;
        }

        public bool IsMainThread => Thread.CurrentThread.ManagedThreadId == _mainThreadId;

        public void Enqueue(Action action)
        {
            if (action == null) return;
            _queue.Enqueue(action);
        }

        public Task<T> InvokeAsync<T>(Func<T> func)
        {
            if (func == null) throw new ArgumentNullException(nameof(func));

            if (IsMainThread)
            {
                try
                {
                    return Task.FromResult(func());
                }
                catch (Exception ex)
                {
                    return Task.FromException<T>(ex);
                }
            }

            var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
            _queue.Enqueue(() =>
            {
                try
                {
                    tcs.TrySetResult(func());
                }
                catch (Exception ex)
                {
                    tcs.TrySetException(ex);
                }
            });
            return tcs.Task;
        }

        public T Invoke<T>(Func<T> func, int timeoutMs = 5000)
        {
            var task = InvokeAsync(func);
            if (!task.Wait(timeoutMs))
            {
                throw new TimeoutException($"MainThreadDispatcher timed out after {timeoutMs}ms.");
            }
            return task.GetAwaiter().GetResult();
        }

        public void Drain()
        {
            int processed = 0;
            const int maxPerFrame = 64;
            while (processed < maxPerFrame && _queue.TryDequeue(out var action))
            {
                try
                {
                    action();
                }
                catch (Exception ex)
                {
                    Plugin.Log?.LogError($"MainThreadDispatcher action error: {ex}");
                }
                processed++;
            }
        }
    }
}
