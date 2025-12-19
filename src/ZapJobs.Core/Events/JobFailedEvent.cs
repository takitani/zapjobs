namespace ZapJobs.Core.Events;

/// <summary>
/// Event raised when a job fails (either temporarily or permanently)
/// </summary>
public sealed class JobFailedEvent : IJobEvent
{
    /// <inheritdoc />
    public Guid RunId { get; init; }

    /// <inheritdoc />
    public string JobTypeId { get; init; } = string.Empty;

    /// <inheritdoc />
    public DateTimeOffset Timestamp { get; init; }

    /// <summary>
    /// The error message
    /// </summary>
    public string ErrorMessage { get; init; } = string.Empty;

    /// <summary>
    /// The exception type name
    /// </summary>
    public string? ErrorType { get; init; }

    /// <summary>
    /// The stack trace (if available)
    /// </summary>
    public string? StackTrace { get; init; }

    /// <summary>
    /// The attempt number that failed (1-based)
    /// </summary>
    public int AttemptNumber { get; init; }

    /// <summary>
    /// Whether the job will be retried
    /// </summary>
    public bool WillRetry { get; init; }

    /// <summary>
    /// When the retry is scheduled (if WillRetry is true)
    /// </summary>
    public DateTimeOffset? NextRetryAt { get; init; }

    /// <summary>
    /// Whether the job was moved to dead letter queue (permanent failure)
    /// </summary>
    public bool MovedToDeadLetter { get; init; }

    /// <summary>
    /// The queue the job was running from
    /// </summary>
    public string Queue { get; init; } = string.Empty;

    /// <summary>
    /// How long the job ran before failing
    /// </summary>
    public TimeSpan Duration { get; init; }
}
