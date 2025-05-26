using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace HashedWheelTimer
{
    public class ActionTimerTask<T> : ITimerTask
    {
        private readonly Func<ITimeout, ValueTask<T>> _action;

        public ActionTimerTask(Func<ITimeout, ValueTask<T>> asyncAction)
        {
            this._action = asyncAction ?? throw new ArgumentNullException(nameof(asyncAction));
        }

        public async ValueTask RunAsync(ITimeout timeout, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(timeout);

            try
            {
                await _action(timeout).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                // Log or handle the exception as needed
                Console.WriteLine($"Exception occurred in ActionTimerTask: {ex}");
            }
        }
    }
}
