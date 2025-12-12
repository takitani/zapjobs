using Microsoft.Extensions.Logging;
using ZapJobs.Core;

namespace ZapJobs.Execution;

/// <summary>
/// Handles retry logic with exponential backoff
/// </summary>
public class RetryHandler
{
    private readonly ILogger<RetryHandler> _logger;

    public RetryHandler(ILogger<RetryHandler> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Determine if a retry should be attempted
    /// </summary>
    public bool ShouldRetry(Exception exception, int currentAttempt, RetryPolicy policy)
    {
        // Check if exception type is explicitly non-retryable
        if (IsNonRetryableException(exception, policy))
        {
            _logger.LogDebug(
                "Exception type {ExceptionType} is non-retryable",
                exception.GetType().Name);
            return false;
        }

        // Check max retries
        if (currentAttempt >= policy.MaxRetries)
        {
            _logger.LogDebug(
                "Max retries ({MaxRetries}) reached at attempt {Attempt}",
                policy.MaxRetries, currentAttempt);
            return false;
        }

        return true;
    }

    /// <summary>
    /// Check if exception type should not be retried
    /// </summary>
    private static bool IsNonRetryableException(Exception exception, RetryPolicy policy)
    {
        var exType = exception.GetType();

        // Direct match
        if (policy.NonRetryableExceptions.Contains(exType))
            return true;

        // Check base types
        foreach (var nonRetryable in policy.NonRetryableExceptions)
        {
            if (nonRetryable.IsAssignableFrom(exType))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Calculate retry delay with optional jitter
    /// </summary>
    public TimeSpan CalculateDelay(int attemptNumber, RetryPolicy policy)
    {
        return policy.CalculateDelay(attemptNumber);
    }
}
