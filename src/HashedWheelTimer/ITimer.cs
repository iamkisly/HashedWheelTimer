using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace HashedWheelTimer
{
    /// <summary>
    /// Schedules <see cref="ITimerTask"/>s for one-time future execution in a background thread.
    /// </summary>
    public interface ITimer
    {
        /// <summary>
        /// Schedules the specified <see cref="ITimerTask"/> for one-time execution after the specified delay.
        /// </summary>
        /// <param name="task">The task to execute after the delay.</param>
        /// <param name="delay">The delay after which the task will execute.</param>
        /// <returns>A handle which is associated with the specified task.</returns>
        ITimeout CreateTimeout(ITimerTask task, TimeSpan delay);

        /// <summary>
        /// Releases all resources acquired by this <see cref="ITimer"/> and cancels all tasks which were scheduled but not executed yet.
        /// </summary>
        /// <returns>The handles associated with the tasks which were canceled by this method.</returns>
        IEnumerable<ITimeout> Stop();
    }
}
