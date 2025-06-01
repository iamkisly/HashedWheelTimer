using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace HashedWheelTimer
{
    public delegate ValueTask<TResult> TimerAction<TResult>(ITimeout timeout, CancellationToken ct);

    public class ActionTimerTask<TResult>(TimerAction<TResult> asyncAction) : ITimerTask<TResult>
    {
        private readonly TaskCompletionSource<TResult> _completionSource = new(TaskCreationOptions.RunContinuationsAsynchronously);
        
        public Task<TResult> ResultTask => _completionSource.Task;
        public async ValueTask RunAsync(ITimeout timeout, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(timeout);
            
            try
            {
                var result = await asyncAction(timeout, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                _completionSource.TrySetCanceled(cancellationToken);
            }
            catch (Exception ex)
            {
                _completionSource.TrySetException(ex);
            }
        }
    }
    
    public class RecurringActionTimerTask<TResult>(TimerAction<TResult> asyncAction) : ITimerTask<IReadOnlyList<TResult>>
    {
        private readonly TaskCompletionSource<IReadOnlyList<TResult>> _completionSource = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly ConcurrentQueue<TResult> _results = new();
        
        public Task<IReadOnlyList<TResult>>  ResultTask => _completionSource.Task;
        public async ValueTask RunAsync(ITimeout timeout, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(timeout);
            
            try
            {
                var result = await asyncAction(timeout, cancellationToken).ConfigureAwait(false);
                _results.Enqueue(result);
                
                if (timeout.Expired || timeout.Canceled)
                    _completionSource.TrySetResult(_results.ToArray());
            }
            catch (OperationCanceledException)
            {
                _completionSource.TrySetCanceled(cancellationToken);
            }
            catch (Exception ex)
            {
                _completionSource.TrySetException(ex);
            }
        }
    }
    
    public delegate ValueTask TimerAction(ITimeout timeout, CancellationToken ct);
    public sealed class VoidResultTimerTask(TimerAction asyncAction) : ITimerTask<object?>
    {
        private readonly TaskCompletionSource<object?> _completionSource = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<object?> ResultTask => _completionSource.Task;

        public async ValueTask RunAsync(ITimeout timeout, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(timeout);
            
            try
            {
                await asyncAction(timeout, cancellationToken).ConfigureAwait(false);
                _completionSource.TrySetResult(null);
            }
            catch (OperationCanceledException)
            {
                _completionSource.TrySetCanceled(cancellationToken);
            }
            catch (Exception ex)
            {
                _completionSource.TrySetException(ex);
            }
        }
    }
}
