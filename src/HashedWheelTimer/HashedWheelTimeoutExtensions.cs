using HashedWheelTimer;

public static class HashedWheelTimeoutExtensions
{
    public static async IAsyncEnumerable<TResult> GetEnumerableResult<TResult>(this ITimeout timeout)
    {
        if (timeout.TimerTask is not ITimerTask<IReadOnlyList<TResult>> timeSpanTask) yield break;
        foreach (var item in await timeSpanTask)
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
}