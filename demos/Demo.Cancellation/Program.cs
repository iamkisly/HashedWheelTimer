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
            Console.WriteLine($"Demo start: {DateTime.Now}");
            var source = new CancellationTokenSource();
            
            var loggerFactory = LoggerFactory.Create(builder => {
                builder
                    .AddConsole()
                    .AddFilter(level => level >= LogLevel.None);
            });

            var timerFactory = new TimerFactory(loggerFactory);
            var timer = timerFactory.Create(builder => {
                builder
                    .SetTickInterval(TimeSpan.FromSeconds(1))
                    .SetBucketCount(64);
            });

            var timerTask = timer.RunAsync(source.Token);

            var token = source.Token;
            try
            {
                var now = PreciseTimeSpanExtensions.Elapsed;
                for (var i = 1; i <= 10; i++)
                {
                    var timeout = timer.CreateTimeout(new ActionTimerTask<bool>((timeout, _) =>
                    {
                        Console.WriteLine($"Timeout {(timeout.Expired ? "- Expired" : "")} {PreciseTimeSpanExtensions.Elapsed - now}");
                        return ValueTask.FromResult(true);
                    }), TimeSpan.FromSeconds(i));
                }
                await Task.Delay(TimeSpan.FromSeconds(5), token).ContinueWith(task =>
                {
                    source.Cancel();
                    Console.WriteLine("Token cancelled");
                }, token);
            }
            catch (OperationCanceledException ex)
            {
                Console.WriteLine("OperationCanceled");
                Console.WriteLine(ex);
            }
            finally 
            {
                var unprocessed = timer.Stop();
                Console.WriteLine($"Unprocessed: {unprocessed.Count()}");
                Console.WriteLine($"Demo end: {DateTime.Now}");
            }
        }
    }
}
