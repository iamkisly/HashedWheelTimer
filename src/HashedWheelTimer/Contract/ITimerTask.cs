using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace HashedWheelTimer
{
    /// <summary>
    /// A task which is executed after the delay specified with <see cref="ITimer.NewTimeout"/>.
    /// </summary>
    public interface ITimerTask
    {
        /// <summary>
        /// Executed after the delay specified with <see cref="ITimer.NewTimeout"/>.
        /// </summary>
        /// <param name="timeout">A handle which is associated with this task.</param>
        /// <returns>A <see cref="ValueTask"/> representing the asynchronous operation.</returns>
        ValueTask RunAsync(ITimeout timeout, CancellationToken cancellationToken);
    }
}
