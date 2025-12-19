namespace ZapJobs.Core.Events;

/// <summary>
/// Event raised when a job is scheduled for retry after a failure
/// </summary>
public sealed class JobRetryingEvent : IJobEvent
{
    /// <inheritdoc />
    public Guid RunId { get; init; }

    /// <inheritdoc />
    public string JobTypeId { get; init; } = string.Empty;

    /// <inheritdoc />
    public DateTimeOffset Timestamp { get; init; }

    /// <summary>
    /// The attempt number that just failed (1-based)
    /// </summary>
    public int FailedAttempt { get; init; }

    /// <summary>
    /// The next attempt number (1-based)
    /// </summary>
    public int NextAttempt { get; init; }

    /// <summary>
    /// Maximum number of retries configured
    /// </summary>
    public int MaxRetries { get; init; }

    /// <summary>
    /// When the retry is scheduled
    /// </summary>
    public DateTimeOffset RetryAt { get; init; }

    /// <summary>
    /// The delay until the retry
    /// </summary>
    public TimeSpan Delay { get; init; }

    /// <summary>
    /// The error message from the failed attempt
    /// </summary>
    public string ErrorMessage { get; init; } = string.Empty;

    /// <summary>
    /// The queue the job is in
    /// </summary>
    public string Queue { get; init; } = string.Empty;
}
