using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace HashedWheelTimer;

public sealed partial class HashedWheelTimer
{
    [DebuggerDisplay("Count = {DebugCount}")]
    private sealed class HashedWheelBucket(Action<HashedWheelTimeout>? onRecurring = null)
    {
        private readonly ConcurrentQueue<HashedWheelTimeout> _currentTickTimeouts = new();
        private readonly ConcurrentQueue<HashedWheelTimeout> _remainingTimeouts = new();

        // Для диагностики — считается быстро, не O(n)
        internal int DebugCount => _currentTickTimeouts.Count + _remainingTimeouts.Count;

        public void AddTimeout(HashedWheelTimeout timeout)
        {
            if (timeout.RemainingRounds <= 0)
                _currentTickTimeouts.Enqueue(timeout);
            else
                _remainingTimeouts.Enqueue(timeout);
        }

        public void Clear()
        {
            _currentTickTimeouts.Clear();
            _remainingTimeouts.Clear();
        }

        public IEnumerable<HashedWheelTimeout> UnprocessedTimeouts
        {
            get
            {
                foreach (var timeout in _remainingTimeouts)
                    yield return timeout;
        
                foreach (var timeout in _currentTickTimeouts)
                    yield return timeout;
            }
        }

        public async Task ExpireTimeoutsAsync(TimeSpan deadline, int maxDOP, CancellationToken cancellationToken)
        {
            var semaphore = new SemaphoreSlim(maxDOP);
            var tasks = new List<Task>();

            while (!cancellationToken.IsCancellationRequested && _currentTickTimeouts.TryDequeue(out var timeout))
            {
                if (timeout.Canceled) continue;
                if (timeout.Deadline > deadline) continue;
                    
                await semaphore.WaitAsync(cancellationToken);
                tasks.Add(Task.Run(async () =>
                {
                    try
                    {
                        await timeout.ExpireAsync(cancellationToken);
                        if (timeout is { RecurringRounds: > 0, Canceled: false }) 
                        {
                            onRecurring?.Invoke(timeout);
                        }
                    }
                    finally 
                    { 
                        semaphore.Release(); 
                    }
                }, cancellationToken));
            }

            if (tasks.Count > 0)
            {
                await Task.WhenAll(tasks);
            }
        }

        public void ReduceRound(CancellationToken cancellationToken)
        {
            var count = _remainingTimeouts.Count;
            while (count > 0 && !cancellationToken.IsCancellationRequested)
            {
                if (!_remainingTimeouts.TryDequeue(out var timeout)) break;

                count--;
                if (timeout.Canceled) continue;

                timeout.RemainingRoundsDecrease();
                if (timeout.RemainingRounds > 0)
                    _remainingTimeouts.Enqueue(timeout);
                else
                    _currentTickTimeouts.Enqueue(timeout);
            }
        }
    }
}