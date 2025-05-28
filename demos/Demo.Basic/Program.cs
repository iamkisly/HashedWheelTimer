using HashedWheelTimer;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Threading.Tasks;
using static HashedWheelTimer.HashedWheelTimer;

namespace Demo
{
    internal class Program
    {
        static async Task Main(string[] args)
        {
            var cts = new CancellationTokenSource();

            var loggerFactory = LoggerFactory.Create(builder => {
                builder
                    .AddConsole()
                    .AddFilter(level => level >= LogLevel.None);
            });

            var timerFactory = new TimerFactory(loggerFactory);
            var timer = timerFactory.Create(builder => {
                builder
                    .SetTickInterval(TimeSpan.FromMilliseconds(100))
                    .SetBucketCount(512);
            });
            
            var timerTask = timer.RunAsync(cts.Token);

            var now = PreciseTimeSpanExtensions.Elapsed;
            for (var i = 5; i < 100; i++)
            {
                var timeout = timer.CreateTimeout(new ActionTimerTask<bool>(timeout =>
                {
                    Console.WriteLine($"Timeout {PreciseTimeSpanExtensions.Elapsed - now} {(timeout.Expired ? "- Expired" : "")}");
                    return ValueTask.FromResult(true);
                }), TimeSpan.FromMilliseconds(100*i));
            }
            await Task.Delay(TimeSpan.FromSeconds(20), cts.Token);

            var timeout1 = timer.CreateTimeout(new ActionTimerTask<bool>(timeout =>
            {
                Console.WriteLine($"Timeout {PreciseTimeSpanExtensions.Elapsed - now} {(timeout.Expired ? "- Expired" : "")}");
                return ValueTask.FromResult(true);
            }), TimeSpan.FromMilliseconds(1000), reccuring: 5);

            await Task.Delay(TimeSpan.FromSeconds(20), cts.Token);

            var timeouts = timer.Stop();
            Console.ReadLine();
        }
    }
}
