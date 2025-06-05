using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using HashedWheelTimer.Contract;
using Microsoft.Extensions.Logging;
using ITimer = HashedWheelTimer.Contract.ITimer;

namespace HashedWheelTimer;

public sealed partial class HashedWheelTimer : ITimer
{
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger<HashedWheelTimer> _logger;

    private readonly long _maxPendingTimeouts;
    private long _pendingTimeouts;

    private readonly HashedWheelBucket[] _buckets;
    private readonly int _bucketCount;
    private readonly int _mask;

    private readonly TimeSpan _tickInterval;
    private readonly long _tickDuration;

    private readonly Worker _worker;
    private readonly int _maxDOP;

    public HashedWheelTimer(IHashedWheelTimerConfig config, ILoggerFactory loggerFactory)
    {
        _loggerFactory = loggerFactory;
        _logger = loggerFactory.CreateLogger<HashedWheelTimer>();
        
        _tickInterval = config.TickInterval;
        _tickDuration = config.TickInterval.Ticks;
        _bucketCount = config.BucketCount;
        _maxPendingTimeouts = config.MaxPendingTimeouts;
        _maxDOP = config.MaxDOP;

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
    
    /// <summary>
    /// Creates a new timeout. Throws RejectedExecutionException if MaxPendingTimeouts exceeded.
    /// </summary>
    /// <param name="task">The timer task to execute.</param>
    /// <param name="delay">Delay before execution.</param>
    /// <param name="recurring">Number of recurrences.</param>
    /// <exception cref="RejectedExecutionException">If MaxPendingTimeouts limit is reached.</exception>
    public ITimeout CreateTimeout(ITimerTask task, TimeSpan delay, int recurring = 0)
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
            recurring: recurring,
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
        timeout.RecurringRoundsDecrease();
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
            await Task.Factory.StartNew(async () => await _worker.RunAsync(cancellationToken).ConfigureAwait(false), 
                cancellationToken, TaskCreationOptions.LongRunning, TaskScheduler.Default);
        }
    }

    public IEnumerable<ITimeout> Stop()
    {
        _worker.Stop();
        return _buckets.SelectMany(bucket => bucket.UnprocessedTimeouts);
    }
    
    public Action<Exception, ITimeout>? OnUnhandledException { get; set; }    
    
    private (int bucketIndex, int remainingRounds) CalculateTimeoutPosition(TimeSpan deadline)
    {
        var calculated = deadline.Ticks / _tickDuration;
        var tick = _worker.Tick;

        // Вычисляем сколько всего тиков осталось, и в итоге полных оборотов колеса
        var remaining = (calculated - tick) / _bucketCount;
        var bucketIndex = Math.Max(calculated, tick) & _mask;
        return ((int)remaining, (int)bucketIndex);
    }
    
    private enum TimerState
    {
        None,
        Canceled,
        Expired
    }
}