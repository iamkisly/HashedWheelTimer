using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using HashedWheelTimer.Contract;

namespace HashedWheelTimer;

public class TimerFactory(ILoggerFactory loggerFactory) : ITimerFactory
{
    public HashedWheelTimer Create()
    {
        return Create(builder => builder.SetBucketCount(128));
    }

    public HashedWheelTimer Create(Action<Builder> configure)
    {
        var builder = new Builder(loggerFactory);
        configure?.Invoke(builder);

        return builder.Build();
    }

    public class Builder(ILoggerFactory loggerFactory) : IHashedWheelTimerConfig
    {
        private readonly ILoggerFactory _loggerFactory = loggerFactory;
            

        private TimeSpan _tickInterval;
        private int _bucketCount = 256;
        private int _maxPendingTimeouts = -1;
        private int _maxdop;

        public Builder SetTickInterval(TimeSpan tickInterval)
        {
            _tickInterval = tickInterval;
            return this;
        }

        public Builder SetBucketCount(int bucketCount)
        {
            _bucketCount = bucketCount;
            return this;
        }
        
        public Builder SetMaxDegreeOfParallelism(int maxdop)
        {
            _maxdop = maxdop;
            return this;
        }

        public Builder SetMaxPendingTimeouts(int maxPendingTimeouts)
        {
            _maxPendingTimeouts = maxPendingTimeouts;
            return this;
        }

        public TimeSpan TickInterval => _tickInterval.Ticks == 0 ? TimeSpan.FromMilliseconds(100) : _tickInterval;
        public int BucketCount => _bucketCount;
        public int MaxPendingTimeouts => _maxPendingTimeouts;
        public int MaxDOP => _maxdop;


        //public IHashedWheelTimeoutDefaultPolicy Policy { get; set; }

        public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
        {
            foreach (var error in ValidateBucketCount())
                yield return error;

            foreach (var error in ValidateTickInterval())
                yield return error;

            foreach (var error in ValidateMaxPendingTimeouts())
                yield return error;
            
            foreach (var error in ValidateMaxDOP())
                yield return error;

            foreach (var error in ValidateWheelConfiguration())
                yield return error;
        }

        private IEnumerable<ValidationResult> ValidateBucketCount()
        {
            if (BucketCount <= 0)
                yield return ValidationError(nameof(BucketCount), "must be greater than 0");

            if (BucketCount > int.MaxValue / 2 + 1)
                yield return ValidationError(nameof(BucketCount), $"may not be greater than 2^30: {BucketCount}");

            if (!IsPowerOfTwo(BucketCount))
                yield return ValidationError(nameof(BucketCount), "should be a power of two for optimal performance");
        }

        private IEnumerable<ValidationResult> ValidateTickInterval()
        {
            if (TickInterval <= TimeSpan.Zero)
                yield return ValidationError(nameof(TickInterval), "must be greater than 0");

            if (TickInterval.TotalMilliseconds < 1)
                yield return ValidationError(nameof(TickInterval), "should be at least 1ms");

            if (TickInterval.Ticks % TimeSpan.TicksPerMillisecond != 0)
                yield return ValidationError(nameof(TickInterval), "should be a whole number of milliseconds");
        }

        private IEnumerable<ValidationResult> ValidateMaxPendingTimeouts()
        {
            if (MaxPendingTimeouts is < 1 and not -1)
                yield return ValidationError(nameof(MaxPendingTimeouts), "must be -1 (unlimited) or ≥ 1");

            if (MaxPendingTimeouts > 1_000_000)
                yield return ValidationError(nameof(MaxPendingTimeouts), "is too large (max: 1,000,000)");
        }
        
        private IEnumerable<ValidationResult> ValidateMaxDOP()
        {
            if (MaxDOP is < 1 and not -1)
                yield return ValidationError(nameof(MaxPendingTimeouts), "must be -1 (unlimited) or ≥ 1");

            if (MaxPendingTimeouts > 128)
                yield return ValidationError(nameof(MaxPendingTimeouts), "is too large (max: 128)");
        }

        private IEnumerable<ValidationResult> ValidateWheelConfiguration()
        {
            var messageTemplate = string.Empty;
            try
            {
                var totalWheelTimeMs = checked(TickInterval.TotalMilliseconds * BucketCount);

                if (totalWheelTimeMs > 60_000)
                    messageTemplate = $"Total wheel time ({totalWheelTimeMs}ms) is too long (max: 1 minute)";
            }
            catch (OverflowException)
            {
                messageTemplate = "TickInterval * BucketCount causes overflow";
            }
            if (messageTemplate.Length > 0)
                yield return ValidationError(
                    fieldNames: [nameof(TickInterval), nameof(BucketCount)],
                    message: messageTemplate);
        }

        private static ValidationResult ValidationError(string fieldName, string message)
            => new ValidationResult($"{fieldName} {message}", new[] { fieldName });

        private static ValidationResult ValidationError(string[] fieldNames, string message)
            => new ValidationResult(message, fieldNames);

        private static bool IsPowerOfTwo(int n) => (n & (n - 1)) == 0 && n > 0;


        public HashedWheelTimer Build()
        {
            var errors = new List<ValidationResult>();
            if (!Validator.TryValidateObject(this, new ValidationContext(this), errors, true))
            {
                throw new ValidationException(
                    "Invalid HashedWheelTimer configuration:\n" +
                    string.Join("\n", errors.Select(e => e.ErrorMessage)));
            }
                
            var logger = _loggerFactory.CreateLogger<HashedWheelTimer>();
            return new HashedWheelTimer(this, logger);
        }
    }
}