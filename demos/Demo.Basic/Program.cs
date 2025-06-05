using HashedWheelTimer;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Threading.Tasks;
using HashedWheelTimer.Contract;
using static HashedWheelTimer.HashedWheelTimer;

namespace Demo
{
    internal class Program
    {
        static async Task Main(string[] args)
        {
            var tokenSource = new CancellationTokenSource();

            var loggerFactory = LoggerFactory.Create(builder => {
                builder
                    .AddConsole()
                    .AddFilter(level => level >= LogLevel.None);
            });

            // Configure timer factory
            var timerFactory = new TimerFactory(loggerFactory);
            var timer = timerFactory.Create(builder => {
                builder
                    .SetTickInterval(TimeSpan.FromMilliseconds(100))
                    .SetBucketCount(512)
                    .SetMaxPendingTimeouts(200)
                    .SetMaxDegreeOfParallelism(Environment.ProcessorCount*2 -1);
            });
            
            // Create instance HashedWheelTimer
            var timerTask = timer.RunAsync(tokenSource.Token);
            
            // Exact current time in TSC system ticks 
            var preciseStartTime = PreciseTimeSpanExtensions.Elapsed;
            
            // VoidResultTimerTask does not return any value
            var timeout0 = timer.CreateTimeout(new VoidTimerTask((timeout, token) =>
            {
                var elapsed = PreciseTimeSpanExtensions.Elapsed - preciseStartTime;
                Console.WriteLine($"{elapsed} timeout0 (void)");
                return default;
            }), TimeSpan.FromSeconds(5));
            
            var timeout1 = timer.CreateVoidTimeout((timeout, token) =>
            {
                var elapsed = PreciseTimeSpanExtensions.Elapsed - preciseStartTime;
                Console.WriteLine($"{elapsed} timeout1 (void)");
            }, TimeSpan.FromSeconds(6));
            
            
            // ActionTimerTask allows you to return a value
            var timeout2 = timer.CreateTimeout(new ActionTimerTask<int>(async (timeout, token) =>
            {
                var elapsed = PreciseTimeSpanExtensions.Elapsed - preciseStartTime;
                Console.WriteLine($"{elapsed} timeout2 (result)");
                return await ValueTask.FromResult(100500);
            }), TimeSpan.FromSeconds(7));
            
            var timeout3 = timer.CreateActionTimeout((timeout, token) =>
            {
                var elapsed = PreciseTimeSpanExtensions.Elapsed - preciseStartTime;
                Console.WriteLine($"{elapsed} timeout3 (result)");
                return 100501;
            }, TimeSpan.FromSeconds(8));
            
            
            // Tasks that repeat at equal intervals. The recurring parameter defines the number of repetitions.
            // Therefore, the number of runs will be "recurring +1".
            var timeout4 = timer.CreateTimeout(new VoidTimerTask((timeout, token)  =>
            {
                var elapsed = PreciseTimeSpanExtensions.Elapsed - preciseStartTime;
                Console.WriteLine($"{elapsed} timeout4 (void recurring) {(timeout.Expired ? "- Expired" : "")}");
                return default;
            }), delay: TimeSpan.FromSeconds(1), recurring: 2);
            
            var timeout5 = timer.CreateTimeout(new RecurringTimerTask<TimeSpan>((timeout, token)  =>
            {
                var elapsed = PreciseTimeSpanExtensions.Elapsed - preciseStartTime;
                Console.WriteLine($"{elapsed} timeout5 (recurring) {(timeout.Expired ? "- Expired" : "")}");
                return ValueTask.FromResult(elapsed.ToTimeSpan());
            }), delay: TimeSpan.FromSeconds(3), recurring: 1);
            
            
            // The timer is not intended and should not handle long tasks.
            // You should choose whether you will run them in the thread pool or create a task factory,
            // so as not to occupy one thread in the pool for a long time.
            
            var timeout6 = timer.CreateVoidTimeout(async (timeout, token) =>
            {
                var task = await Task.Factory.StartNew(
                    async () => await Task.Delay(TimeSpan.FromSeconds(10), token)
                        .ContinueWith((_) =>
                        {
                            var elapsed = PreciseTimeSpanExtensions.Elapsed - preciseStartTime; 
                            Console.WriteLine($"{elapsed} timeout6 - long range task finished !!!!");
                        }, token),
                    creationOptions: TaskCreationOptions.LongRunning, scheduler: TaskScheduler.Default,
                    cancellationToken: token
                );
                
                var elapsed = PreciseTimeSpanExtensions.Elapsed - preciseStartTime;
                Console.WriteLine($"{elapsed} timeout6 - long range task ...");
            }, TimeSpan.FromSeconds(10));
            
            // Now timeout7 must run before This was a bad example, but it illustrates well that the timer
            // should not be locked. Otherwise you will stop all the tasks in the wheel.
            
            var timeout7 = timer.CreateTimeout(new VoidTimerTask((timeout, token)  =>
            {
                var elapsed = PreciseTimeSpanExtensions.Elapsed - preciseStartTime;
                Console.WriteLine($"{elapsed} timeout7 (void) {(timeout.Expired ? "- Expired" : "")}");
                return default;
            }), TimeSpan.FromSeconds(13));            
            
            // Delay so that all timeouts have time to work out,
            // and it is possible to get the return values of the execution results from them.
            
            await Task.Delay(TimeSpan.FromSeconds(10), tokenSource.Token);
            
            var n = await timeout3.GetResult<int>();
            Console.WriteLine($"timeout3 result= {n}");
            
            // Unfortunately I haven't come up with a good enough architecture,
            // so we have to specify the type of the stored value again.
            
            await foreach (var ts in timeout5.GetEnumerableResult<TimeSpan>().WithCancellation(tokenSource.Token))
            {
                Console.WriteLine($"timeout5 result= {ts}");
            }
            
            Console.ReadLine();
            var timeouts = timer.Stop();
            Console.WriteLine(timeouts.Count());
        }
    }
}

