using HashedWheelTimer;
using HashedWheelTimer.Contract;

namespace HashedWheelTimer;

public static class HashedWheelTimerExtensions
{
    // void actions
    public static ITimeout CreateVoidTimeout(this HashedWheelTimer timer, 
        Func<ITimeout, CancellationToken, ValueTask> action, TimeSpan delay) => 
        timer.CreateTimeout(delay: delay,
            task: new VoidResultTimerTask(async (timeout, token) =>
            await action(timeout, token)));
    
    public static ITimeout CreateVoidTimeout(this HashedWheelTimer timer, 
        Action<ITimeout, CancellationToken> action, TimeSpan delay) => 
        timer.CreateTimeout(delay: delay, 
            task: new VoidResultTimerTask((timeout, token) =>
            {
                action(timeout, token);
                return ValueTask.CompletedTask;
            }));

    
    // action with result
    public static ITimeout CreateActionTimeout<TResult>(this HashedWheelTimer timer, 
        Func<ITimeout, CancellationToken, Task<TResult>> action, TimeSpan delay) => 
        timer.CreateTimeout(delay: delay, 
            task: new ActionTimerTask<TResult>(async (timeout, token) => 
            await action(timeout, token)));
    
    public static ITimeout CreateActionTimeout<TResult>(this HashedWheelTimer timer, 
        Func<ITimeout, CancellationToken, TResult> action, TimeSpan delay) => 
        timer.CreateTimeout(delay: delay, 
            task: new ActionTimerTask<TResult>(async (timeout, token) => 
            await Task.Factory.StartNew(() => action(timeout, token), token)));


    // recurred timeout (with result)
    public static ITimeout CreateRecurredTimeout<TResult>(this HashedWheelTimer timer, 
        Func<ITimeout, CancellationToken, ValueTask<TResult>> action, TimeSpan delay, int recurring) =>
        timer.CreateTimeout(delay: delay, recurring: recurring, 
            task: new RecurringActionTimerTask<TResult>(async (timeout, token) => 
            await action(timeout, token)));
        
    public static ITimeout CreateRecurredTimeout<TResult>(this HashedWheelTimer timer, 
        Func<ITimeout, CancellationToken, TResult> action, TimeSpan delay, int recurring) =>
        timer.CreateTimeout(delay: delay, recurring: recurring, 
            task: new RecurringActionTimerTask<TResult>(async (timeout, token) => 
            await Task.Factory.StartNew(() => action(timeout, token), token)));
        
    
    // recurred timeout (void)
    public static ITimeout CreateVoidTimeout(this HashedWheelTimer timer, 
        Func<ITimeout, CancellationToken, ValueTask> action, TimeSpan delay, int recurring) => 
        timer.CreateTimeout(delay: delay, recurring: recurring, 
            task: new VoidResultTimerTask(async (timeout, token) => 
            await action(timeout, token)));
    
    public static ITimeout CreateVoidTimeout(this HashedWheelTimer timer, 
        Action<ITimeout, CancellationToken> action, TimeSpan delay, int recurring) => 
        timer.CreateTimeout(delay: delay, recurring: recurring, 
            task: new VoidResultTimerTask(async (timeout, token) => 
            await Task.Factory.StartNew(() => action(timeout, token), token)));
}