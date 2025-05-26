using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace HashedWheelTimer
{
    public partial class HashedWheelTimer
    {
        public sealed class Worker(HashedWheelTimer timer)
        {
            private readonly TaskCompletionSource _completionSource = new();
            private long _wheelTick;

            public long Tick => _wheelTick;
            private WorkerState State { get; set; } = WorkerState.None;
            public bool Started => State == WorkerState.Started;
            public bool Shutdown => State == WorkerState.Shutdown;

            public Task ClosedFuture => _completionSource.Task;

            public async Task RunAsync(CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    timer.StartTime = PreciseTimeSpanExtensions.Elapsed;
                    if (timer.StartTime <= PreciseTimeSpan.Zero)
                        timer.StartTime = PreciseTimeSpan.FromTicks(1);

                    State = WorkerState.Started;
                    while (cancellationToken.IsCancellationRequested == false)
                    {
                        var deadline = await WaitNextTick(cancellationToken).ConfigureAwait(false);

                        if (!Started) break;
                        if (deadline <= TimeSpan.Zero) continue;
                        
                        var idx = timer._mask & (int)_wheelTick;
                        var bucket = timer._buckets[idx];

                        await bucket.ExpireTimeoutsAsync(deadline, 8, cancellationToken)
                            .ConfigureAwait(false);
                        bucket.ReduceRound(cancellationToken);
                        _wheelTick++;
                    }
                }
                finally
                {
                    _completionSource.TrySetResult();
                }
            }

            public void Stop()
            {
                State = WorkerState.Shutdown;
            }

            public async ValueTask<TimeSpan> WaitNextTick(CancellationToken cancellationToken)
            {
                var deadline = TimeSpan.FromTicks(timer._tickDuration * (_wheelTick + 1));
                while (true)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    
                    var currentTime = PreciseTimeSpanExtensions.Elapsed - timer.StartTime;
                    var sleepTime = deadline - currentTime;

                    if (sleepTime > TimeSpan.Zero)
                    {
                        await Task.Delay(sleepTime.CeilingToMilliseconds(), timer.CancellationToken)
                                  .ConfigureAwait(false);
                    }
                    else
                    {
                        return currentTime.ToTimeSpan();
                    }
                }
            }
        }
        
        private enum WorkerState
        {
            None,
            Started,
            Shutdown
        }
    }
}
