
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Xunit.Abstractions;
using HashedWheelTimer;
using Microsoft.Extensions.Logging;

namespace TimerTest
{
    public class HashedWheelTimerTest
    {
        private readonly ITestOutputHelper _output;
        private readonly int _backet_count = 512;
        private readonly TimerFactory _timerFactory;

        public HashedWheelTimerTest(ITestOutputHelper output)
        {
            var loggerFactory = LoggerFactory.Create(builder => {
                builder
                    .AddConsole()
                    .AddFilter(level => level >= LogLevel.None);
            });

            _timerFactory = new TimerFactory(loggerFactory);

            _output = output;
        }

        [Fact]
        public async Task TestScheduleTimeoutShouldNotRunBeforeDelay()
        {
            var timer = _timerFactory.Create(builder => {
                builder
                    .SetTickInterval(TimeSpan.FromMilliseconds(100))
                    .SetBucketCount(512);
            });
            var barrier = new CountdownEvent(1);
            var timeout = timer.CreateTimeout(
                new ActionTimerTask<bool>(
                    t =>
                    {
                        Assert.Fail("This should not have run");
                        return ValueTask.FromResult(barrier.Signal());
                    }),
                TimeSpan.FromSeconds(10));
            timer.RunAsync(CancellationToken.None);
            Assert.False(barrier.Wait(TimeSpan.FromSeconds(3)));
            Assert.False(timeout.Expired, "Timer should not expire");
            timer.Stop();
        }
        
        [Fact]
        public async Task TestScheduleTimeoutShouldRunAfterDelay()
        {
            var timer = _timerFactory.Create(builder => {
                builder
                    .SetTickInterval(TimeSpan.FromMilliseconds(100))
                    .SetBucketCount(512);
            });
            var barrier = new CountdownEvent(1);
            var timeout = timer.CreateTimeout(
                new ActionTimerTask<bool>(
                    t => ValueTask.FromResult(barrier.Signal())
                ),
                TimeSpan.FromSeconds(2));
            timer.RunAsync(CancellationToken.None);
            Assert.True(barrier.Wait(TimeSpan.FromSeconds(3)), "Timer should wait");
            Assert.True(timeout.Expired, "Timer should expire");
            timer.Stop();
        }
        
        [Fact]
        public async Task TestStopTimer()
        {
            var latch = new CountdownEvent(3);
            var timerProcessed = _timerFactory.Create(builder => {
                builder
                    .SetTickInterval(TimeSpan.FromMilliseconds(100))
                    .SetBucketCount(512);
            });
            timerProcessed.RunAsync(CancellationToken.None);
            for (var i = 0; i < 3; i++)
            {
                timerProcessed.CreateTimeout(
                    new ActionTimerTask<bool>(t => ValueTask.FromResult(latch.Signal())),
                    TimeSpan.FromMilliseconds(1)
                );
            }
            
            latch.Wait();
            Assert.Empty(timerProcessed.Stop()); // "Number of unprocessed timeouts should be 0"

            var timerUnprocessed = _timerFactory.Create(builder => {
                builder
                    .SetTickInterval(TimeSpan.FromMilliseconds(100))
                    .SetBucketCount(512);
            });
            timerUnprocessed.RunAsync(CancellationToken.None);
            for (var i = 0; i < 5; i++)
            {
                timerUnprocessed.CreateTimeout(new ActionTimerTask<bool>(_ => ValueTask.FromResult(true)), TimeSpan.FromSeconds(5));
            }
            
            await Task.Delay(1000); // Simulate delay
            var unprocessedTimeouts = timerUnprocessed.Stop();
            Assert.NotEmpty(unprocessedTimeouts); // "Number of unprocessed timeouts should be greater than 0"
        }
        
        [Fact]
        public async Task TestTimerShouldThrowExceptionAfterShutdownForNewTimeouts()
        {
            var latch = new CountdownEvent(3);
            var timer = _timerFactory.Create(builder => {
                builder
                    .SetTickInterval(TimeSpan.FromMilliseconds(100))
                    .SetBucketCount(512);
            });
            timer.RunAsync(CancellationToken.None);
            for (var i = 0; i < 3; i++)
            {
                timer.CreateTimeout(
                    new ActionTimerTask<bool>(t => ValueTask.FromResult(latch.Signal())),
                    TimeSpan.FromMilliseconds(1)
                );
            }

            latch.Wait(3000);
            timer.Stop();

            Assert.Throws<InvalidOperationException>(() =>
                timer.CreateTimeout(CreateNoOpTimerTask(), TimeSpan.FromMilliseconds(1)));
        }
        
        
        [Fact]
        public void TestTimerOverflowWheelLength()
        {
            var timer = _timerFactory.Create(builder => {
                builder
                    .SetTickInterval(TimeSpan.FromMilliseconds(100))
                    .SetBucketCount(32);
            });
            timer.RunAsync(CancellationToken.None);
            
            var latch = new CountdownEvent(3);
            ActionTimerTask<bool>? task = null;
            task = new ActionTimerTask<bool>(t =>
            {
                timer.CreateTimeout(task, TimeSpan.FromMilliseconds(100));
                return ValueTask.FromResult(latch.Signal());
            });
            timer.CreateTimeout(task, TimeSpan.FromMilliseconds(100));

            Assert.True(latch.Wait(5000), "latch.Wait(5000)");
            Assert.NotEmpty(timer.Stop());
        }
        
        [Fact]
        public void TestExecutionOnTime()
        {
            const int tickDuration = 200;
            const int timeout = 125;
            const int maxTimeout = 2 * (tickDuration + timeout);
            var timer = _timerFactory.Create(builder => {
                builder
                    .SetTickInterval(TimeSpan.FromMilliseconds(tickDuration))
                    .SetBucketCount(512);
            });
            timer.RunAsync(CancellationToken.None);
            
            var queue = new ConcurrentQueue<PreciseTimeSpan>();
            const int scheduledTasks = 10000;
            
            for (var i = 0; i < scheduledTasks; i++)
            {
                var start = PreciseTimeSpanExtensions.Elapsed;
                timer.CreateTimeout(
                    new ActionTimerTask<bool>(t =>
                    {
                        queue.Enqueue(PreciseTimeSpanExtensions.Elapsed - start);
                        return ValueTask.FromResult(true);
                    }),
                    TimeSpan.FromMilliseconds(timeout));
            }
            
            Task.Delay(200).Wait(); // Simulate delay
            for (var i = 0; i < scheduledTasks; i++)
            {
                queue.TryDequeue(out var time);
                var delay = time.ToTimeSpan().TotalMilliseconds;
                Assert.True(
                    delay is >= timeout and < maxTimeout, 
                    "Timeout + " + scheduledTasks + " delay " + delay + " must be " + timeout + " < " + maxTimeout);
            }

            timer.Stop();
        }

        [Fact]
        public void TestRejectedExecutionExceptionWhenTooManyTimeoutsAreAddedBackToBack()
        {
            var timer = _timerFactory.Create(builder => {
                builder
                    .SetTickInterval(TimeSpan.FromMilliseconds(100))
                    .SetBucketCount(32)
                    .SetMaxPendingTimeouts(2);
            });
            timer.RunAsync(CancellationToken.None);
            timer.CreateTimeout(CreateNoOpTimerTask(), TimeSpan.FromSeconds(5));
            timer.CreateTimeout(CreateNoOpTimerTask(), TimeSpan.FromSeconds(5));
            try
            {
                timer.CreateTimeout(CreateNoOpTimerTask(), TimeSpan.FromMilliseconds(1));
                Assert.Fail("Timer allowed adding 3 timeouts when maxPendingTimeouts was 2");
            }
            catch (RejectedExecutionException)
            {
                // Expected
            }
            finally
            {
                timer.Stop();
            }
        }
        
        [Fact]
        public void TestNewTimeoutShouldStopThrowingRejectedExecutionExceptionWhenExistingTimeoutIsCancelled()
        {
            const int tickDuration = 100;
            var timer = _timerFactory.Create(builder => {
                builder
                    .SetTickInterval(TimeSpan.FromMilliseconds(tickDuration))
                    .SetBucketCount(32)
                    .SetMaxPendingTimeouts(2);
            });
            timer.RunAsync(CancellationToken.None);

            timer.CreateTimeout(CreateNoOpTimerTask(), TimeSpan.FromSeconds(1));
            var timeoutToCancel = timer.CreateTimeout(CreateNoOpTimerTask(), TimeSpan.FromSeconds(1));
            Assert.True(timeoutToCancel.Cancel());

            Task.Delay(tickDuration * 25).Wait();

            var secondLatch = new CountdownEvent(1);
            timer.CreateTimeout(CreateCountdownEventTimerTask(secondLatch), TimeSpan.FromMilliseconds(90));

            secondLatch.Wait();
            timer.Stop();
        }

        [Fact] // (timeout = 3000)
        public void TestNewTimeoutShouldStopThrowingRejectedExecutionExceptionWhenExistingTimeoutIsExecuted()

        {
            var latch = new CountdownEvent(1);
            var timer = _timerFactory.Create(builder => {
                builder
                    .SetTickInterval(TimeSpan.FromMilliseconds(25))
                    .SetBucketCount(4)
                    .SetMaxPendingTimeouts(2);
            });
            timer.RunAsync(CancellationToken.None);            
            timer.CreateTimeout(CreateNoOpTimerTask(), TimeSpan.FromSeconds(5));
            timer.CreateTimeout(CreateCountdownEventTimerTask(latch), TimeSpan.FromMilliseconds(90));

            latch.Wait(3000);

            var secondLatch = new CountdownEvent(1);
            timer.CreateTimeout(CreateCountdownEventTimerTask(secondLatch), TimeSpan.FromMilliseconds(90));

            secondLatch.Wait(3000);
            timer.Stop();
        }


        private static ITimerTask CreateNoOpTimerTask() => new ActionTimerTask<bool>(_ => ValueTask.FromResult(true));
        static ActionTimerTask<bool> CreateCountdownEventTimerTask(CountdownEvent latch) => new(t => ValueTask.FromResult(latch.Signal()));
    }

}
