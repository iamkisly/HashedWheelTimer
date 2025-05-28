using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace HashedWheelTimer
{
    public sealed partial class HashedWheelTimer : ITimer
    {
        private readonly ILogger<HashedWheelTimer> _logger;

        private readonly long _maxPendingTimeouts;
        private long _pendingTimeouts;

        private readonly HashedWheelBucket[] _buckets;
        private readonly int _bucketCount;
        private readonly int _mask;

        private readonly TimeSpan _tickInterval;
        private readonly long _tickDuration;

        private readonly Worker _worker;

        public HashedWheelTimer(IHashedWheelTimerConfig config, ILogger<HashedWheelTimer> logger)
        {
            _logger = logger;
            _tickInterval = config.TickInterval;
            _tickDuration = config.TickInterval.Ticks;
            _bucketCount = config.BucketCount;
            _maxPendingTimeouts = config.MaxPendingTimeouts;

            // Rounding a number to the nearest power of two
            // Example ticksPerWheel=190, _mask = 256 -1 => 0xFF
            _mask = 1;
            while (_mask < _bucketCount)
                _mask <<= 1;

            // As with ArrayPool, we cast the array size to a power of two.
            // We could change it to use the raw value, but that would require complex logic to calculate the index.
            _bucketCount = _mask;
            _mask--;

            _buckets = new HashedWheelBucket[_bucketCount];
            for (var i = 0; i < _bucketCount; i++)
            {
                var bucket = new HashedWheelBucket(RepeatTimeout);
                _buckets[i] = bucket;
            }

            _worker = new Worker(this);
        }

        private PreciseTimeSpan StartTime { get; set; }
        private long _timeoutId = -1;

        public ITimeout CreateTimeout(ITimerTask task, TimeSpan delay, int reccuring = -1)
        {
            ArgumentNullException.ThrowIfNull(task);
            if (_worker.Shutdown)
            {
                throw new InvalidOperationException("Cannot create a timer that is already shutting down");
            }
            if (_maxPendingTimeouts > 0)
            {
                if (_pendingTimeouts >= _maxPendingTimeouts)
                {
                    throw new RejectedExecutionException(
                        $"Number of pending timeouts ({_pendingTimeouts +1}) is greater than " +
                        $"or equal to maximum allowed pending timeouts ({_maxPendingTimeouts})");
                }
                Interlocked.Increment(ref _pendingTimeouts);
            }
            
            var deadline = (delay.Deadline() - StartTime).ToTimeSpan().CeilingToMilliseconds();

            var (remaining, bucketIndex) = CalculateTimeoutPosition(deadline);

            _timeoutId++;
            var timeout = new HashedWheelTimeout(
                id: _timeoutId, 
                task: task,
                deadline: deadline, 
                interval: delay, 
                remaining: remaining,
                recurring: reccuring,
                onComplete: (timeout) =>
                {
                    if (_maxPendingTimeouts > 0)
                        Interlocked.Decrement(ref _pendingTimeouts);
                });
            _buckets[bucketIndex].AddTimeout(timeout);
            return timeout;

        }

        private void RepeatTimeout(HashedWheelTimeout timeout)
        {
            timeout.Deadline += timeout.Interval;
            var (remaining, bucketIndex) = CalculateTimeoutPosition(timeout.Deadline);
            timeout.RemainingRounds = remaining;
            timeout.RecurringRounds--;
            _buckets[bucketIndex].AddTimeout(timeout);
        }

        public async Task RunAsync(CancellationToken cancellationToken)
        {
            if (_worker.Shutdown)
            {
                throw new InvalidOperationException("Cannot run a timer that is already shutting down");
            }
            else if (_worker.Started) { }
            else
            {
                await _worker.RunAsync(cancellationToken).ConfigureAwait(false);
            }
        }

        public IEnumerable<ITimeout> Stop()
        {
            _worker.Stop();
            return _buckets.SelectMany(bucket => bucket.UnprocessedTimeouts);
        }

        (int bucketIndex, int remainingRounds) CalculateTimeoutPosition(TimeSpan deadline)
        {
            var calculated = deadline.Ticks / _tickDuration;
            var tick = _worker.Tick;

            // Вычисляем сколько всего тиков осталось, и в итоге полных оборотов колеса
            var remaining = (calculated - tick) / _bucketCount;
            var bucketIndex = Math.Max(calculated, tick) & _mask;
            return ((int)remaining, (int)bucketIndex);
        }

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

            public async Task ExpireTimeoutsAsync(TimeSpan deadline, int maxParallelism, CancellationToken cancellationToken)
            {
                var semaphore = new SemaphoreSlim(maxParallelism);
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

                    timeout.RemainingRounds--;
                    if (timeout.RemainingRounds > 0)
                        _remainingTimeouts.Enqueue(timeout);
                    else
                        _currentTickTimeouts.Enqueue(timeout);
                }
            }
        }
        
        private sealed class HashedWheelTimeout(
            long id, ITimerTask task, TimeSpan deadline, TimeSpan interval, int remaining = 0, int recurring = 0, 
            Action<HashedWheelTimeout>? onComplete = null
        ) : ITimeout
        {
            public long Id => id;
            public TimeSpan Deadline { get; internal set; } = deadline;
            public TimeSpan Interval { get; } = interval;

            public ITimerTask Task { get; } = task;
            public int RemainingRounds { get; internal set; } = remaining;
            public int RecurringRounds { get; internal set; } = recurring;
            private TimerState State { get; set; } = TimerState.None;
            public bool Expired => State == TimerState.Expired;
            public bool Canceled => State == TimerState.Canceled;

            public async Task ExpireAsync(CancellationToken cancellationToken)
            {
                if (State is TimerState.Expired or TimerState.Canceled)
                    return;

                if (RecurringRounds == 0 && !Canceled)
                {
                    onComplete?.Invoke(this);
                    State = TimerState.Expired;
                }
                await Task.RunAsync(this, cancellationToken).ConfigureAwait(false);
            }

            public bool Cancel()
            {
                if (State is TimerState.Canceled)
                    return false;

                State = TimerState.Canceled;
                return true;
            }
        }

        private enum TimerState
        {
            None,
            Canceled,
            Expired
        }
    }
}
