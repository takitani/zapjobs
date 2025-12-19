namespace ZapJobs.Core.Events;

/// <summary>
/// Event raised when a job starts execution
/// </summary>
public sealed class JobStartedEvent : IJobEvent
{
    /// <inheritdoc />
    public Guid RunId { get; init; }

    /// <inheritdoc />
    public string JobTypeId { get; init; } = string.Empty;

    /// <inheritdoc />
    public DateTimeOffset Timestamp { get; init; }

    /// <summary>
    /// The current attempt number (1-based)
    /// </summary>
    public int AttemptNumber { get; init; }

    /// <summary>
    /// The serialized input data for the job
    /// </summary>
    public string? InputJson { get; init; }

    /// <summary>
    /// The queue the job is running from
    /// </summary>
    public string Queue { get; init; } = string.Empty;

    /// <summary>
    /// The worker ID processing the job (if available)
    /// </summary>
    public string? WorkerId { get; init; }
}
