using System.Runtime.CompilerServices;

namespace HashedWheelTimer.Contract
{
    /// <summary>
    /// A task which is executed after the delay specified with <see cref="Contract.ITimer.CreateTimeout"/>.
    /// </summary>
    public interface ITimerTask
    {
        /// <summary>
        /// Executed after the delay specified with <see cref="Contract.ITimer.CreateTimeout"/>.
        /// </summary>
        /// <param name="timeout">A handle which is associated with this task.</param>
        /// <param name="cancellationToken"></param>
        /// <returns>A <see cref="ValueTask"/> representing the asynchronous operation.</returns>
        ValueTask RunAsync(ITimeout timeout, CancellationToken cancellationToken);
    }
    
    /// <summary>
    /// User timer tasks must be thread-safe if MaxDOP > 1.
    /// </summary>
    public interface IAwaitableTimerTask<TResult> : ITimerTask
    {
        Task<TResult> ResultTask { get; }
        TaskAwaiter<TResult> GetAwaiter() => ResultTask.GetAwaiter();
    }
    
    public interface IAwaitableTimerTask : ITimerTask
    {
        Task ResultTask { get; }
        TaskAwaiter GetAwaiter() => ResultTask.GetAwaiter();
    }
}
