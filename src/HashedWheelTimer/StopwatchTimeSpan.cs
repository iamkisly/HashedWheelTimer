using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;

namespace HashedWheelTimer
{
    /// <summary>
    /// A static class for working with time and timestamps.
    /// </summary>
    public static class StopwatchTimeSpan
    {
        /// <summary>
        /// Returns the time rounded to the nearest interval.
        /// </summary>
        /// <param name="time">The time to be rounded.</param>
        /// <param name="ticksPerInterval">The number of ticks in the interval.</param>
        /// <returns>The time rounded to the nearest interval.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
        private static TimeSpan Ceiling(this TimeSpan time, long ticksPerInterval)
        {
            var remainder = time.Ticks % ticksPerInterval;
            return remainder == 0 ? time : new TimeSpan(time.Ticks - remainder + ticksPerInterval);
        }

        /// <summary>
        /// Returns the time rounded to the nearest interval.
        /// </summary>
        /// <param name="ticksInterval">The number of ticks to be rounded.</param>
        /// <param name="ticksPerInterval">The number of ticks in the interval.</param>
        /// <returns>The time rounded to the nearest interval.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
        private static TimeSpan Ceiling(long ticksInterval, long ticksPerInterval) => new TimeSpan(CeilingTicks(ticksInterval, ticksPerInterval));

        /// <summary>
        /// Returns the number of ticks rounded to the nearest interval.
        /// </summary>
        /// <param name="ticksInterval">The number of ticks to be rounded.</param>
        /// <param name="ticksPerInterval">The number of ticks in the interval.</param>
        /// <returns>The number of ticks rounded to the nearest interval.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
        private static long CeilingTicks(long ticksInterval, long ticksPerInterval)
        {
            var remainder = ticksInterval % ticksPerInterval;
            return remainder == 0 ? ticksInterval : ticksInterval - remainder + ticksPerInterval;
        }

        /// <summary>
        /// Returns the time rounded to the nearest microsecond.
        /// </summary>
        /// <param name="time">The time to be rounded.</param>
        /// <returns>The time rounded to the nearest microsecond.</returns>
        public static TimeSpan CeilingToMicroseconds(this TimeSpan time) => time.Ceiling(TimeSpan.TicksPerMicrosecond);

        /// <summary>
        /// Returns the time rounded to the nearest millisecond.
        /// </summary>
        /// <param name="time">The time to be rounded.</param>
        /// <returns>The time rounded to the nearest millisecond.</returns>
        public static TimeSpan CeilingToMilliseconds(this TimeSpan time) => time.Ceiling(TimeSpan.TicksPerMillisecond);

        /// <summary>
        /// Returns the time rounded to the nearest microsecond.
        /// </summary>
        /// <param name="time">The time to be rounded.</param>
        /// <returns>The time rounded to the nearest microsecond.</returns>
        public static TimeSpan CeilingToMicroseconds(long time) => Ceiling(time, TimeSpan.TicksPerMicrosecond);

        /// <summary>
        /// Returns the time rounded to the nearest millisecond.
        /// </summary>
        /// <param name="time">The time to be rounded.</param>
        /// <returns>The time rounded to the nearest millisecond.</returns>
        public static TimeSpan CeilingToMilliseconds(long time) => Ceiling(time, TimeSpan.TicksPerMillisecond);
        
    }

    public readonly struct PreciseTimeSpan : IEquatable<PreciseTimeSpan>
    {
        private readonly long _ticks;
        private PreciseTimeSpan(long ticks) => _ticks = ticks;
        public long Ticks => _ticks;
        
        public static readonly PreciseTimeSpan Zero = new PreciseTimeSpan(0);
        
        public static PreciseTimeSpan FromTicks(long ticks) => new PreciseTimeSpan(ticks);
        
        public override int GetHashCode() => _ticks.GetHashCode();
        public int CompareTo(PreciseTimeSpan other) => _ticks.CompareTo(other._ticks);

        public bool Equals(PreciseTimeSpan other) => _ticks == other._ticks;
        public override bool Equals(object? obj) => obj is PreciseTimeSpan other && Equals(other);
        
        public static bool operator ==(PreciseTimeSpan t1, PreciseTimeSpan t2) => t1._ticks == t2._ticks;
        public static bool operator !=(PreciseTimeSpan t1, PreciseTimeSpan t2) => t1._ticks != t2._ticks;
        public static bool operator > (PreciseTimeSpan t1, PreciseTimeSpan t2) => t1._ticks >  t2._ticks;
        public static bool operator < (PreciseTimeSpan t1, PreciseTimeSpan t2) => t1._ticks <  t2._ticks;
        public static bool operator >=(PreciseTimeSpan t1, PreciseTimeSpan t2) => t1._ticks >= t2._ticks;
        public static bool operator <=(PreciseTimeSpan t1, PreciseTimeSpan t2) => t1._ticks <= t2._ticks;
        
        public static PreciseTimeSpan operator +(PreciseTimeSpan t, TimeSpan duration)
        {
            var ticks = t._ticks + duration.ToPreciseTicks();
            return new PreciseTimeSpan(ticks);
        }

        public static PreciseTimeSpan operator -(PreciseTimeSpan t, TimeSpan duration)
        {
            var ticks = t._ticks - duration.ToPreciseTicks();
            return new PreciseTimeSpan(ticks);
        }

        public static PreciseTimeSpan operator -(PreciseTimeSpan t1, PreciseTimeSpan t2)
        {
            var ticks = t1._ticks - t2._ticks;
            return new PreciseTimeSpan(ticks);
        }
        
        public static implicit operator TimeSpan(PreciseTimeSpan time) => time.ToTimeSpan();
        public static explicit operator PreciseTimeSpan(TimeSpan time) => time.ToPreciseTimeSpan();
        
        public override string ToString() => $"{this.ToTimeSpan()}";
    }

    public static class PreciseTimeSpanExtensions
    {
        private static readonly long StartTimestamp = Stopwatch.GetTimestamp();
        private static readonly double TickFrequency = (double) TimeSpan.TicksPerSecond / Stopwatch.Frequency;
        private static readonly double ReverseTickFrequency = (double) Stopwatch.Frequency / TimeSpan.TicksPerSecond;
        
        public static long Now => Stopwatch.GetTimestamp();

        /// <summary>
        /// Возвращает время, прошедшее с момента инициализации класса, в виде TimeSpan.
        /// </summary>
        public static PreciseTimeSpan Elapsed
        {
            get
            {
                var elapsedTicks = Stopwatch.GetTimestamp() - StartTimestamp;
                return elapsedTicks > 0
                    ? PreciseTimeSpan.FromTicks(elapsedTicks)
                    : PreciseTimeSpan.Zero;
            }
        }

        public static PreciseTimeSpan ElapsedFrom(PreciseTimeSpan time) 
            => PreciseTimeSpan.FromTicks(time.Ticks - StartTimestamp);
        
        public static PreciseTimeSpan Deadline(this TimeSpan time)
        {
            var current = Stopwatch.GetTimestamp() - StartTimestamp;
            var deadline = current + time.ToPreciseTicks();
            return PreciseTimeSpan.FromTicks(deadline);
        }

        /// <summary>
        /// Преобразует "точный" TimeSpan в обычный.
        /// </summary>
        public static TimeSpan ToTimeSpan(this PreciseTimeSpan time) 
            => FromPreciseTicks(time.Ticks);
        
        /// <summary>
        /// Преобразует "обычный" TimeSpan в точный.
        /// </summary>
        public static PreciseTimeSpan ToPreciseTimeSpan(this TimeSpan time) 
            => PreciseTimeSpan.FromTicks(time.ToPreciseTicks());
        
        /// <summary>
        /// Преобразует "точные тики" обратно в TimeSpan.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
        internal static TimeSpan FromPreciseTicks(long preciseTicks) 
            => TimeSpan.FromTicks((long)(preciseTicks * TickFrequency));
        
        /// <summary>
        /// Преобразует TimeSpan в "точные тики" (с учетом частоты Stopwatch).
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
        internal static long ToPreciseTicks(this TimeSpan timeSpan) => Stopwatch.IsHighResolution 
            ? (long)(timeSpan.Ticks * ReverseTickFrequency) 
            : timeSpan.Ticks;
    }
}
