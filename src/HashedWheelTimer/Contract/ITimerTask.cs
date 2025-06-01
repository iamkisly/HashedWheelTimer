using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;

namespace HashedWheelTimer
{
    /// <summary>
    /// A task which is executed after the delay specified with <see cref="ITimer.CreateTimeout"/>.
    /// </summary>
    public interface ITimerTask
    {
        /// <summary>
        /// Executed after the delay specified with <see cref="ITimer.CreateTimeout"/>.
        /// </summary>
        /// <param name="timeout">A handle which is associated with this task.</param>
        /// <param name="cancellationToken"></param>
        /// <returns>A <see cref="ValueTask"/> representing the asynchronous operation.</returns>
        ValueTask RunAsync(ITimeout timeout, CancellationToken cancellationToken);
    }
    
    public interface ITimerTask<TResult> : ITimerTask
    {
        Task<TResult> ResultTask { get; }
        TaskAwaiter<TResult> GetAwaiter() => ResultTask.GetAwaiter();
    }
    
}
