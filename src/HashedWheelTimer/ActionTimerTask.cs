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

public abstract class TimerTask : ITimerTask
{
    protected abstract void OnComplete(ITimeout timeout);
    protected abstract void OnException(Exception ex);
    protected abstract void OnCanceled(CancellationToken cancellationToken);
    public abstract ValueTask RunAsync(ITimeout timeout, CancellationToken cancellationToken);
}

public abstract class TimerTask<TResult> : TimerTask
{
    protected override void OnComplete(ITimeout timeout) => OnComplete(timeout, default!);
    protected abstract void OnComplete(ITimeout timeout, TResult result);
}


public delegate ValueTask<TResult> AsyncAction<TResult>(ITimeout timeout, CancellationToken cancellationToken);

public abstract class GenericTimerTask<TResult>(AsyncAction<TResult> asyncAction) : TimerTask<TResult>
{
    private readonly AsyncAction<TResult> _asyncAction =
        asyncAction ?? throw new ArgumentNullException(nameof(asyncAction));
    protected readonly TaskCompletionSource<TResult> CompletionSource =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    
    public override async ValueTask RunAsync(ITimeout timeout, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(timeout);
        try
        {
            var result = await _asyncAction(timeout, cancellationToken).ConfigureAwait(false);
            OnComplete(timeout, result);
        }
        catch (OperationCanceledException)
        {
            OnCanceled(cancellationToken);
        }
        catch (Exception ex)
        {
            OnException(ex);
            throw;
        }
    }
    
    protected override void OnComplete(ITimeout timeout, TResult result) => CompletionSource.SetResult(result);
    protected override void OnException(Exception ex)
    {
        CompletionSource.TrySetException(ex);
    }

    protected override void OnCanceled(CancellationToken cancellationToken) 
        => CompletionSource.TrySetCanceled(cancellationToken);
}


// TimerTask with SINGLE result value 
public sealed class ActionTimerTask<TResult> : GenericTimerTask<TResult>,
    IAwaitableTimerTask<TResult>
{
    public ActionTimerTask(AsyncAction<TResult> asyncAction) : base(asyncAction) { }
    public  Task<TResult> ResultTask => CompletionSource.Task;
};

public sealed class RecurringTimerTask<TResult> : TimerTask<TResult>, 
    IAwaitableTimerTask<IAsyncEnumerable<TResult>>
{
    private readonly ConcurrentQueue<TResult> _results = new();
    private bool _isCompleted;
    private readonly TaskCompletionSource<IAsyncEnumerable<TResult>> СompletionSource = 
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    
    private readonly AsyncAction<TResult> _asyncAction;
    public RecurringTimerTask(AsyncAction<TResult> asyncAction)
    {
        _asyncAction = asyncAction ?? throw new ArgumentNullException(nameof(asyncAction));
    }

    public override async ValueTask RunAsync(ITimeout timeout, CancellationToken cancellationToken)
    {
        try
        {
            var result = await _asyncAction(timeout, cancellationToken).ConfigureAwait(false);
            OnComplete(timeout, result); 
        }
        catch (OperationCanceledException)
        {
            OnCanceled(cancellationToken);
        }
        catch (Exception ex)
        {
            OnException(ex);
            throw;
        }
        finally
        {
            СompletionSource.TrySetResult(GetResultsAsync(cancellationToken));
        }
    }
    
    protected override void OnComplete(ITimeout timeout, TResult result)
    {
        _results.Enqueue(result);
        if (timeout is { Expired: false, Canceled: false }) return;
        Volatile.Write(ref _isCompleted, true);
    }

    protected override void OnException(Exception ex)
    {
        СompletionSource.TrySetException(ex);
    }

    protected override void OnCanceled(CancellationToken cancellationToken)
    {
        СompletionSource.TrySetCanceled(cancellationToken); 
    }

    private async IAsyncEnumerable<TResult> GetResultsAsync([EnumeratorCancellation] CancellationToken ct = default)
    {
        while (!ct.IsCancellationRequested && (!_isCompleted || !_results.IsEmpty))
        {
            if (_results.TryDequeue(out var item))
                yield return item;
            else
                await Task.Yield(); 
        }
    }

    public Task<IAsyncEnumerable<TResult>> ResultTask => СompletionSource.Task;
}


public sealed class VoidTimerTask : TimerTask, IAwaitableTimerTask
{
    private readonly TaskCompletionSource _completionSource = new();
    private readonly Func<ITimeout, CancellationToken, ValueTask> _asyncAction;

    public Task ResultTask => _completionSource.Task;

    public VoidTimerTask(Func<ITimeout, CancellationToken, ValueTask> asyncAction)
    {
        _asyncAction = asyncAction ?? throw new ArgumentNullException(nameof(asyncAction));
    }

    public override async ValueTask RunAsync(ITimeout timeout, CancellationToken cancellationToken)
    {
        try
        {
            await _asyncAction(timeout, cancellationToken).ConfigureAwait(false);
            OnComplete(timeout);
        }
        catch (OperationCanceledException)
        {
            OnCanceled(cancellationToken);
        }
        catch (Exception ex)
        {
            OnException(ex);
            throw;
        }
    }

    protected override void OnComplete(ITimeout timeout) => _completionSource.TrySetResult();
    protected override void OnException(Exception ex) => _completionSource.TrySetException(ex);
    protected override void OnCanceled(CancellationToken cancellationToken) 
        => _completionSource.TrySetCanceled(cancellationToken);
}
