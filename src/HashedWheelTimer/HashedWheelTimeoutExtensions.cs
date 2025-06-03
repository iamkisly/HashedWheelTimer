using HashedWheelTimer.Contract;

namespace HashedWheelTimer;

public static class HashedWheelTimeoutExtensions
{
    public static async IAsyncEnumerable<TResult> GetEnumerableResult<TResult>(this ITimeout timeout)
    {
        if (timeout.TimerTask is not ITimerTask<IEnumerable<TResult>> timerTask) yield break;
        foreach (var item in await timerTask)
        {
            yield return item;
        }
    }

    public static async ValueTask<TResult> GetResult<TResult>(this ITimeout timeout)
    {
        if (timeout.TimerTask is ITimerTask<TResult> taskResult)
        {
            return await taskResult;
        }
        throw new InvalidOperationException($"Cannot get {typeof(TResult)} result from timer task");
    }
    
    public static async Task ExecuteVoidTask(this ITimeout timeout)
    {
        if (timeout.TimerTask is ITimerTask<object?> timerTask)
            await timerTask.ResultTask;
    }
}