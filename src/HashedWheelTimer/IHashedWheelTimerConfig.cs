using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace HashedWheelTimer;

public interface IHashedWheelTimerConfig
{
    TimeSpan TickInterval { get; }
    int BucketCount { get; }
    int MaxPendingTimeouts { get; }
    
    // Max Degree of Parallelism
    int MaxDOP { get; } 
}