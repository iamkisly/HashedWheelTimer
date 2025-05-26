using Microsoft.Extensions.Logging;
using Microsoft.Extensions.ObjectPool;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace HashedWheelTimer
{
    public sealed partial class HashedWheelTimer : ITimer
    {
        private readonly ILogger<HashedWheelTimer> _logger;
        private readonly CancellationTokenSource _cancellationTokenSource = new();

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
                var bucket = new HashedWheelBucket();
                _buckets[i] = bucket;
            }

            _worker = new Worker(this);
        }

        private PreciseTimeSpan StartTime { get; set; }

        public ITimeout CreateTimeout(ITimerTask task, TimeSpan delay)
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
                    throw new NotImplementedException("Decrease pending timeouts trouble");
                    throw new RejectedExecutionException(
                        $"Number of pending timeouts ({_pendingTimeouts}) is greater than " +
                        $"or equal to maximum allowed pending timeouts ({_maxPendingTimeouts})");
                }
                _pendingTimeouts++;
            }
            
            var deadline = (delay.Deadline() - StartTime).ToTimeSpan().CeilingToMilliseconds();
            var calculated = deadline.Ticks / _tickDuration;

            var tick = _worker.Tick;
            
            // Вычисляем сколько всего тиков осталось, и в итоге полных оборотов колеса
            var remaining = (calculated - tick) / _bucketCount;
            var bucketIndex = Math.Max(calculated, tick) & _mask;

            var timeout = new HashedWheelTimeout(task, deadline, (int)remaining);
            _buckets[bucketIndex].AddTimeout(timeout);
            return timeout;

        }

        internal CancellationToken CancellationToken => _cancellationTokenSource.Token;

        public async Task RunAsync(CancellationToken cancellationToken)
        {
            if (_worker.Shutdown)
            {
                throw new InvalidOperationException("Cannot run a timer that is already shutting down");
            }
            else if (_worker.Started) { }
            else
            {
                await using (cancellationToken.Register(_cancellationTokenSource.Cancel))
                {
                    await _worker.RunAsync(_cancellationTokenSource.Token).ConfigureAwait(false);
                }
            }
        }

        public IEnumerable<ITimeout> Stop()
        {
            _worker.Stop();
            return _buckets.SelectMany(bucket => bucket.UnprocessedTimeouts);
        }

        [DebuggerDisplay("Count = {DebugCount}")]
        private sealed class HashedWheelBucket
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
                        try { await timeout.ExpireAsync(cancellationToken); }
                        finally { semaphore.Release(); }
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

        private sealed class HashedWheelTimeout(ITimerTask task, TimeSpan deadline, int remainingRounds = 0) : ITimeout
        {
            public TimeSpan Deadline { get; } = deadline;
            public ITimerTask Task { get; } = task;
            public int RemainingRounds { get; internal set; } = remainingRounds;

            private TimerState State { get; set; } = TimerState.None;
            public bool Expired => State == TimerState.Expired;
            public bool Canceled => State == TimerState.Canceled;

            public async Task ExpireAsync(CancellationToken cancellationToken)
            {
                if (State is TimerState.Expired or TimerState.Canceled)
                    return;

                State = TimerState.Expired;
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
