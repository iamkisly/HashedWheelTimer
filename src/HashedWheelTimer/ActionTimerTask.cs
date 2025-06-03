using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using HashedWheelTimer.Contract;

namespace HashedWheelTimer;

public abstract class TimerTask<TResult>
{
    protected TaskCompletionSource<TResult> _completionSource;
    
    protected abstract void OnComplete(ITimeout timeout, TResult result);
    protected abstract void OnException(Exception ex);
    protected abstract void OnCanceled(CancellationToken cancellationToken);
    public abstract ValueTask RunAsync(ITimeout timeout, CancellationToken cancellationToken);
}

public abstract class GenericTimerTask<TResult> : TimerTask<TResult>
{
    protected Func<ITimeout, CancellationToken, ValueTask<TResult>> _asyncAction;
    
    public  Task<TResult> ResultTask => _completionSource.Task;
    public override async ValueTask RunAsync(ITimeout timeout, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(timeout);

        try
        {
            var result = await _asyncAction(timeout, cancellationToken).ConfigureAwait(false);
            OnComplete(timeout, result);
        }
        catch (OperationCanceledException) { OnCanceled(cancellationToken); }
        catch (Exception ex) { OnException(ex); }
    }
    
    protected override void OnComplete(ITimeout timeout, TResult result) => _completionSource.SetResult(result);
    protected override void OnException(Exception ex) => _completionSource.TrySetException(ex);
    protected override void OnCanceled(CancellationToken cancellationToken) 
        => _completionSource.TrySetCanceled(cancellationToken);
}


// TimerTask with SINGLE result value 
public sealed class ActionTimerTask<TResult> : GenericTimerTask<TResult>, ITimerTask<TResult>
{
    public ActionTimerTask(Func<ITimeout, CancellationToken, ValueTask<TResult>> asyncAction)
    {
        _completionSource = new TaskCompletionSource<TResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        _asyncAction = asyncAction;
    }
}


// TimerTask with MULTIPLE result value
public sealed class RecurringActionTimerTask<TResult> : GenericTimerTask<TResult>, ITimerTask<IAsyncEnumerable<TResult>>
{
    private new readonly TaskCompletionSource<IAsyncEnumerable<TResult>> _completionSource = 
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    
    private readonly ConcurrentQueue<TResult> _results = new();
    private bool _isCompleted;

    public RecurringActionTimerTask(Func<ITimeout, CancellationToken, ValueTask<TResult>> asyncAction)
    {
        _asyncAction = asyncAction;
    }
    
    public new Task<IAsyncEnumerable<TResult>> ResultTask => _completionSource.Task;
    protected override void OnComplete(ITimeout timeout, TResult result)
    {
        _results.Enqueue(result);

        if (timeout is { Expired: false, Canceled: false }) return;
        _isCompleted = true;
        _completionSource.TrySetResult(GetResultsAsync());
    }
    
    private async IAsyncEnumerable<TResult> GetResultsAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            if (_results.TryDequeue(out var item))
                yield return item;
            else if (_isCompleted)
                yield break;
            
            await Task.Delay(100, cancellationToken).ConfigureAwait(false); 
        }
    }
}

// TimerTask without any result value

public sealed class VoidResultTimerTask(Func<ITimeout, CancellationToken, ValueTask> asyncAction) 
    : GenericTimerTask<object?>, ITimerTask<object?>
{
    protected override void OnComplete(ITimeout timeout, object? result) => _completionSource.SetResult(null);
    
    public override async ValueTask RunAsync(ITimeout timeout, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(timeout);

        try
        {
            await asyncAction(timeout, cancellationToken).ConfigureAwait(false);
            OnComplete(timeout, null);
        }
        catch (OperationCanceledException) { OnCanceled(cancellationToken); }
        catch (Exception ex) { OnException(ex); }
    }
}