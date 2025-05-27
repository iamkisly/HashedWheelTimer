using HashedWheelTimer;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
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
                    .SetTickInterval(TimeSpan.FromMilliseconds(5000))
                    .SetBucketCount(128);
            });
            /*
            var timerTask = timer.RunAsync(cts.Token);

            var now = PreciseTimeSpanExtensions.Elapsed;
            for (var i = 19; i < 100; i++)
            {
                var timeout = timer.CreateTimeout(new ActionTimerTask<bool>(timeout =>
                {
                    Console.WriteLine($"Timed out Expired({timeout.Expired}) {PreciseTimeSpanExtensions.Elapsed - now}");

                    return ValueTask.FromResult(true);
                }), TimeSpan.FromSeconds(i));
            }
            await Task.Delay(TimeSpan.FromSeconds(100), cts.Token);
            //cts.Cancel();

            var timeouts = timer.Stop();

            Console.ReadLine();

            */
            // Cancellation WaitNextTick test 
            
            var worker = new Worker(timer);
            
            Console.WriteLine(DateTime.Now);

            try
            {
                Task.WaitAll(
                    Task.Delay(TimeSpan.FromSeconds(3), cts.Token).ContinueWith(task => { 
                        cts.Cancel();
                        Console.WriteLine("Token cancelled");
                    }, cts.Token),

                    Task.Run(async () => await worker.WaitNextTick(cts.Token))
                        .ContinueWith((task) => Console.WriteLine($"1 {DateTime.Now}"), cts.Token),
                    
                    Task.Run(async () => await worker.WaitNextTick(CancellationToken.None))
                        .ContinueWith((task) => Console.WriteLine($"2 {DateTime.Now}"))
                );
            }
            catch (AggregateException ex) { 
                if (ex.InnerException is OperationCanceledException) 
                {
                    Console.WriteLine("OperationCanceled");
                    Console.WriteLine(ex);
                    Console.WriteLine(ex.Message);
                    Console.WriteLine(ex.InnerException);
                }
            }

            Console.WriteLine(DateTime.Now);
            worker.Stop();
            
        }
    }
}
