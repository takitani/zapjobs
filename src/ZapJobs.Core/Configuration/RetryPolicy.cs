namespace ZapJobs.Core;

/// <summary>
/// Configuration for retry behavior on job failures
/// </summary>
public class RetryPolicy
{
    /// <summary>Maximum retry attempts (0 = no retries)</summary>
    public int MaxRetries { get; set; } = 3;

    /// <summary>Initial delay before first retry</summary>
    public TimeSpan InitialDelay { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>Multiplier for exponential backoff (2.0 = double each retry)</summary>
    public double BackoffMultiplier { get; set; } = 2.0;

    /// <summary>Maximum delay between retries</summary>
    public TimeSpan MaxDelay { get; set; } = TimeSpan.FromHours(1);

    /// <summary>Add jitter to prevent thundering herd</summary>
    public bool UseJitter { get; set; } = true;

    /// <summary>Jitter factor (0.0-1.0)</summary>
    public double JitterFactor { get; set; } = 0.2;

    /// <summary>
    /// Exception types that should NOT be retried.
    /// By default, argument and validation errors are not retried.
    /// </summary>
    public HashSet<Type> NonRetryableExceptions { get; set; } =
    [
        typeof(ArgumentException),
        typeof(ArgumentNullException),
        typeof(ArgumentOutOfRangeException),
        typeof(InvalidOperationException),
        typeof(NotSupportedException),
        typeof(NotImplementedException)
    ];

    /// <summary>
    /// Calculate the delay before the next retry attempt
    /// </summary>
    /// <param name="attemptNumber">Current attempt (1-based)</param>
    /// <returns>Delay before retry</returns>
    public TimeSpan CalculateDelay(int attemptNumber)
    {
        // Exponential backoff: InitialDelay * (Multiplier ^ (attempt - 1))
        var delayTicks = InitialDelay.Ticks * Math.Pow(BackoffMultiplier, attemptNumber - 1);
        var delay = TimeSpan.FromTicks((long)delayTicks);

        // Cap at MaxDelay
        if (delay > MaxDelay)
            delay = MaxDelay;

        // Add jitter to prevent thundering herd
        if (UseJitter)
        {
            var jitterRange = delay.Ticks * JitterFactor;
            var jitter = (Random.Shared.NextDouble() * 2 - 1) * jitterRange; // -jitter to +jitter
            delay = TimeSpan.FromTicks(delay.Ticks + (long)jitter);

            // Ensure delay is positive
            if (delay < TimeSpan.Zero)
                delay = TimeSpan.FromSeconds(1);
        }

        return delay;
    }

    /// <summary>
    /// Determine if a retry should be attempted
    /// </summary>
    /// <param name="exception">The exception that occurred</param>
    /// <param name="attemptNumber">Current attempt (1-based)</param>
    /// <returns>True if retry should be attempted</returns>
    public bool ShouldRetry(Exception exception, int attemptNumber)
    {
        // Check max retries
        if (attemptNumber >= MaxRetries)
            return false;

        // Check if exception type is non-retryable
        var exType = exception.GetType();
        if (NonRetryableExceptions.Contains(exType))
            return false;

        // Check base types too
        foreach (var nonRetryable in NonRetryableExceptions)
        {
            if (nonRetryable.IsAssignableFrom(exType))
                return false;
        }

        return true;
    }

    /// <summary>
    /// Create a policy that never retries
    /// </summary>
    public static RetryPolicy NoRetry => new() { MaxRetries = 0 };

    /// <summary>
    /// Create a policy with fixed delay (no exponential backoff)
    /// </summary>
    public static RetryPolicy Fixed(int maxRetries, TimeSpan delay) => new()
    {
        MaxRetries = maxRetries,
        InitialDelay = delay,
        BackoffMultiplier = 1.0,
        UseJitter = false
    };

    /// <summary>
    /// Create a policy with exponential backoff
    /// </summary>
    public static RetryPolicy Exponential(int maxRetries, TimeSpan initialDelay, double multiplier = 2.0) => new()
    {
        MaxRetries = maxRetries,
        InitialDelay = initialDelay,
        BackoffMultiplier = multiplier,
        UseJitter = true
    };
}
