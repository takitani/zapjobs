namespace ZapJobs.Core.Events;

/// <summary>
/// Event raised when a job completes successfully
/// </summary>
public sealed class JobCompletedEvent : IJobEvent
{
    /// <inheritdoc />
    public Guid RunId { get; init; }

    /// <inheritdoc />
    public string JobTypeId { get; init; } = string.Empty;

    /// <inheritdoc />
    public DateTimeOffset Timestamp { get; init; }

    /// <summary>
    /// How long the job took to execute
    /// </summary>
    public TimeSpan Duration { get; init; }

    /// <summary>
    /// The serialized output data from the job
    /// </summary>
    public string? OutputJson { get; init; }

    /// <summary>
    /// Number of items successfully processed (for batch-style jobs)
    /// </summary>
    public int ItemsSucceeded { get; init; }

    /// <summary>
    /// Number of items that failed processing (for batch-style jobs)
    /// </summary>
    public int ItemsFailed { get; init; }

    /// <summary>
    /// Total number of items processed
    /// </summary>
    public int ItemsProcessed { get; init; }

    /// <summary>
    /// The attempt number that succeeded (1-based)
    /// </summary>
    public int AttemptNumber { get; init; }

    /// <summary>
    /// The queue the job ran from
    /// </summary>
    public string Queue { get; init; } = string.Empty;
}
