using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using HashedWheelTimer.Contract;

namespace HashedWheelTimer;

public sealed partial class HashedWheelTimer
{
    private sealed class HashedWheelTimeout(
        long id, ITimerTask task, TimeSpan deadline, TimeSpan interval, int remaining = 0, int recurring = 0, 
        Action<HashedWheelTimeout>? onComplete = null
    ) : ITimeout
    {
        private long _deadlineTicks = deadline.Ticks;
        private int _remaining = remaining;
        private int _recurring = recurring;

        public long Id => id;
        public ITimerTask TimerTask => task;
        public event EventHandler<TimeoutExceptionEventArgs>? TimeoutExceptionOccurred;
        
        public TimeSpan Deadline 
        {
            get => TimeSpan.FromTicks(Volatile.Read(ref _deadlineTicks));
            internal set => Volatile.Write(ref _deadlineTicks, value.Ticks);
        }
        public TimeSpan Interval { get; } = interval;

        public int RemainingRounds 
        {
            get => Volatile.Read(ref _remaining);
            internal set => Volatile.Write(ref _remaining, value);
        }
        internal void RemainingRoundsDecrease() => Interlocked.Decrement(ref _remaining);

        public int RecurringRounds
        {
            get => Volatile.Read(ref _recurring);
            internal set => Volatile.Write(ref _recurring, value);
        }
        internal void RecurringRoundsDecrease() => Interlocked.Decrement(ref _recurring);

        // race condition avoid 
        private int _state;

        private TimerState State
        {
            get => (TimerState)Volatile.Read(ref _state);
            set => Volatile.Write(ref _state, (int)value);
        }

        public bool Expired => State == TimerState.Expired;
        public bool Canceled => State == TimerState.Canceled;
        
        public async Task ExpireAsync(CancellationToken cancellationToken)
        {
            if (State is TimerState.Expired or TimerState.Canceled)
                return;

            if (RecurringRounds <= 0 && !Canceled)
            {
                onComplete?.Invoke(this);
                State = TimerState.Expired;
            }

            try
            {
                await TimerTask.RunAsync(this, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                State = TimerState.Canceled;
            }
            catch (Exception ex)
            {
                //_logger?.LogError(ex, "Exception in timeout {Id}", Id);
                OnTimeoutException(ex, this);
            }
        }

        public bool Cancel()
        {
            if (State is TimerState.Canceled)
                return false;

            State = TimerState.Canceled;
            return true;
        }
        
        private void OnTimeoutException(Exception ex, ITimeout timeout)
        {
            TimeoutExceptionOccurred?.Invoke(this, new TimeoutExceptionEventArgs(ex, timeout));
        }
    }
}

public class TimeoutExceptionEventArgs(Exception exception, ITimeout timeout) : EventArgs
{
    public Exception Exception { get; } = exception;
    public ITimeout Timeout { get; } = timeout;
}