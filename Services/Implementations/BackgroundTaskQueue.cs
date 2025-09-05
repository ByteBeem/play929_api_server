using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using Play929Backend.Services.Interfaces;

namespace Play929Backend.Background
{
    public class BackgroundTaskQueue : IBackgroundTaskQueue
    {
        private readonly ConcurrentQueue<Func<CancellationToken, Task>> _workItems = new();
        private readonly SemaphoreSlim _signal = new(0);

        public void QueueBackgroundWorkItem(Func<CancellationToken, Task> workItem)
        {
            if (workItem == null)
                throw new ArgumentNullException(nameof(workItem));

            _workItems.Enqueue(workItem);
            _signal.Release();
        }

          public void Enqueue(Func<CancellationToken, Task> workItem)
            {
                if (workItem == null)
                    throw new ArgumentNullException(nameof(workItem));

                _workItems.Enqueue(workItem);
                _signal.Release();
            }

        public async Task<Func<CancellationToken, Task>> DequeueAsync(CancellationToken cancellationToken)
        {
            await _signal.WaitAsync(cancellationToken);
            _workItems.TryDequeue(out var workItem);

            return workItem!;
        }
    }
}
