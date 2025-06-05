using HashedWheelTimer.Contract;

namespace HashedWheelTimer;

public static class HashedWheelTimeoutExtensions
{
    public static async IAsyncEnumerable<TResult> GetEnumerableResult<TResult>(this ITimeout timeout)
    {
        if (timeout.TimerTask is not IAwaitableTimerTask<IAsyncEnumerable<TResult>> timerTask) yield break;
        await foreach (var item in await timerTask)
        {
            yield return item;
        }
    }

    public static async ValueTask<TResult> GetResult<TResult>(this ITimeout timeout)
    {
        if (timeout.TimerTask is IAwaitableTimerTask<TResult> taskResult)
        {
            return await taskResult;
        }
        throw new InvalidOperationException($"Cannot get {typeof(TResult)} result from timer task");
    }
    
    public static async Task ExecuteVoidTask(this ITimeout timeout)
    {
        if (timeout.TimerTask is IAwaitableTimerTask timerTask)
            await timerTask.ResultTask;
    }
}