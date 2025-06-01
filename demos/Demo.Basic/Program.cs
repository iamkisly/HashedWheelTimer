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
            var timeout0 = timer.CreateTimeout(new ActionTimerTask<int>(async (timeout, token) =>
            {
                Task.Delay(TimeSpan.FromSeconds(60), token).ContinueWith((_) => Console.WriteLine("timeout0 finish !!!!")).Wait(token);
                Console.WriteLine($"Timeout Task {PreciseTimeSpanExtensions.Elapsed - now} {(timeout.Expired ? "- Expired" : "")} Action ");
                return 100500;
            }), TimeSpan.FromSeconds(15));
            
            var timeout1 = timer.CreateTimeout(new VoidResultTimerTask((timeout, token) =>
            {
                Console.WriteLine($"Timeout Task {PreciseTimeSpanExtensions.Elapsed - now} {(timeout.Expired ? "- Expired" : "")} Void");
                return ValueTask.CompletedTask;
            }), TimeSpan.FromSeconds(17));
            
            for (var i = 5; i < 100; i++)
            {
                var timeout = timer.CreateTimeout(new VoidResultTimerTask((timeout, token) =>
                {
                    Console.WriteLine($"Timeout {PreciseTimeSpanExtensions.Elapsed - now} {(timeout.Expired ? "- Expired" : "")}");
                    return default;
                }), TimeSpan.FromMilliseconds(100*i));
            }
            /*
            if (timeout0.TimerTask is ITimerTask<int> timerTask00)
            {
                var n = await  timerTask00;
            }
            */
            var n = await timeout0.GetResult<int>();
            Console.WriteLine(n);
            
            await Task.Delay(TimeSpan.FromSeconds(5), cts.Token);

            var timeout2 = timer.CreateTimeout(new RecurringActionTimerTask<TimeSpan>((timeout, token)  =>
            {
                var elapsed = PreciseTimeSpanExtensions.Elapsed - now;
                Console.WriteLine($"Timeout {elapsed} {(timeout.Expired ? "- Expired" : "")}");
                return ValueTask.FromResult(elapsed.ToTimeSpan());
            }), TimeSpan.FromMilliseconds(1000), recurring: 5);

            
            timer.CreateVoidTimeout((timeout, token) => Console.WriteLine("Hello World!"), delay: TimeSpan.FromSeconds(1));
            
            timer.CreateVoidTimeout(async (timeout, token) => 
            {
                Task.Delay(TimeSpan.FromSeconds(120)).Wait();
                var elapsed = PreciseTimeSpanExtensions.Elapsed - now;
                Console.WriteLine($"Hello World 2! {elapsed}");
            }, delay: TimeSpan.FromSeconds(1));
            
            await Task.Delay(TimeSpan.FromSeconds(20), cts.Token);
            /*
            if (timeout2.TimerTask is ITimerTask<IReadOnlyList<TimeSpan>> timeSpanTask)
            {
                var result = await timeSpanTask;
                foreach (var r in result)
                {
                    Console.WriteLine($"TimeSpan: {r}");
                }
            }
            */
            
            
            await foreach (var result in timeout2.GetEnumerableResult<TimeSpan>().WithCancellation(cts.Token))
            {
                Console.WriteLine($"TimeSpan: {result}");
                if (result > TimeSpan.FromSeconds(24))
                {
                    await cts.CancelAsync();
                }
            }
            
            
            
            
            Console.ReadLine();
            var timeouts = timer.Stop();
            Console.WriteLine(timeouts.Count());
        }
    }
}
