# HashedWheelTimer

[![License](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE)

## About

**HashedWheelTimer** is a high-performance C# implementation of the "hashed wheel timer" data structure, designed for efficient scheduling and management of delayed and recurring tasks with minimal overhead.

> **Note:**  
> This repository is a fork of [`HashedWheelTimer.cs`](https://github.com/Azure/DotNetty/blob/dev/src/DotNetty.Common/Utilities/HashedWheelTimer.cs) from the [azure/dotnetty](https://github.com/Azure/DotNetty) project.  
> All original copyrights belong to their respective owners; the source code here was adapted for isolated reuse and further improvements.

## Purpose

A hashed wheel timer is a special data structure for scalable timeout management and task scheduling, offering high efficiency for a massive number of timers. It's well-suited for networking applications, connection timeouts, and scenarios that require many concurrent delayed or periodic tasks.

## Quick Start

### Installation

Add the `HashedWheelTimer.cs` file (and any required interfaces) to your project.  
You will also need to provide implementations for the `ITimerTask` interface and optionally configure logging.

### Usage Example

```csharp
using System;

using Microsoft.Extensions.Logging;
using HashedWheelTimer;

class Program
{
    static async Task Main()
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
                .SetBucketCount(512)
                .SetMaxPendingTimeouts(128);
        });
        
        var _ = timer.RunAsync(cts.Token);

        // Schedule a one-time task after 5 seconds
        for (var i = 5; i < 100; i++)
        {
            var timeout = timer.CreateTimeout(
                new PrintTask("Timeout expired!"), 
                TimeSpan.FromMilliseconds(200*i) 
            );
        }
        await Task.Delay(TimeSpan.FromSeconds(3), cts.Token);
        // Schedule a recurring task every 1 second, 5 times
        var recurring = timer.CreateTimeout(
            new PrintTask("Timeout Repeated.."), 
            TimeSpan.FromMilliseconds(500), 
            reccuring: 5
        );
        Console.WriteLine("Repeated timeout scheduled. Waiting...");

        await Task.Delay(TimeSpan.FromSeconds(5));

        // Stop the timer and collect any unprocessed tasks
        var unprocessed = timer.Stop();

        Console.WriteLine($"Stopped timer. Unprocessed timeouts: {unprocessed.Count()}");
    }
}

// For example only. Use ActionTimerTask
// Remember that DateTime.UtcNow has a larger delay than StopWatch.
public class PrintTask(string message) : ITimerTask
{
    public ValueTask RunAsync(ITimeout timeout, CancellationToken cancellationToken)
    {
        Console.WriteLine($"[{DateTime.UtcNow:HH:mm:ss.fff}] {message}");
        return ValueTask.CompletedTask;
    }
}
```
As a result, we will get the following printout.

```
/home/me/workspace/dotnet/HashedWheelTimer/demos/Demo.Basic/bin/Debug/net9.0/Demo.Basic
[21:00:05.304] Timeout expired!
[21:00:05.502] Timeout expired!
[21:00:05.702] Timeout expired!
[21:00:05.902] Timeout expired!
[21:00:06.102] Timeout expired!
[21:00:06.302] Timeout expired!
[21:00:06.502] Timeout expired!
[21:00:06.702] Timeout expired!
[21:00:06.902] Timeout expired!
[21:00:07.102] Timeout expired!
Repeated timeout scheduled. Waiting...
[21:00:07.302] Timeout expired!
[21:00:07.502] Timeout expired!
[21:00:07.701] Timeout expired!
[21:00:07.902] Timeout expired!
[21:00:08.102] Timeout expired!
[21:00:08.301] Timeout Repeated..
[21:00:08.302] Timeout expired!
[21:00:08.502] Timeout expired!
[21:00:08.702] Timeout expired!
[21:00:08.901] Timeout expired!
[21:00:09.102] Timeout expired!
[21:00:09.302] Timeout Repeated..
[21:00:09.302] Timeout expired!
[21:00:09.501] Timeout expired!
[21:00:09.702] Timeout expired!
[21:00:09.902] Timeout expired!
[21:00:10.102] Timeout expired!
[21:00:10.302] Timeout Repeated..
[21:00:10.302] Timeout expired!
[21:00:10.502] Timeout expired!
[21:00:10.702] Timeout expired!
[21:00:10.902] Timeout expired!
[21:00:11.102] Timeout expired!
[21:00:11.302] Timeout Repeated..
[21:00:11.302] Timeout expired!
[21:00:11.502] Timeout expired!
[21:00:11.702] Timeout expired!
[21:00:11.901] Timeout expired!
[21:00:12.102] Timeout expired!
Stopped timer. Unprocessed timeouts: 61

Process finished with exit code 0.

```

The RecurringRounds parameter should be understood as the number of repetitions AFTER the task is executed.


### Canceling a Timeout

Unlike the original code for the Сancel() method, this implementation does not remove the timeout immediately. 
Because it uses a CuncurrentQueue instead of a custom bi-linked list implementation, the cancelled timeout will be 
removed from processing the next time the wheel is spun.

```csharp
var timeout = timer.CreateTimeout(new PrintTask("Will not run"), TimeSpan.FromMilliseconds(500));
timeout.Cancel(); // The task will not be executed
```

## API Overview

- **HashedWheelTimer.HashedWheelTimer**  
  Main class for scheduling delayed and recurring tasks.

- **CreateTimeout(ITimerTask task, TimeSpan delay, int reccuring = 0)**  
  Schedules a task to be executed after a delay. If `reccuring` > 0, the task will repeat.

- **RunAsync(CancellationToken)**  
  Starts the timer processing loop.

- **Stop()**  
  Stops the timer and returns any unprocessed timeouts.

## License

MIT — see [LICENSE](LICENSE).

## Credits

- [Azure/DotNetty](https://github.com/Azure/DotNetty) for the original HashedWheelTimer implementation and architecture.