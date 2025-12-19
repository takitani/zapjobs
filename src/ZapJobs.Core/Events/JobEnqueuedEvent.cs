namespace ZapJobs.Core.Events;

/// <summary>
/// Event raised when a job is enqueued for execution
/// </summary>
public sealed class JobEnqueuedEvent : IJobEvent
{
    /// <inheritdoc />
    public Guid RunId { get; init; }

    /// <inheritdoc />
    public string JobTypeId { get; init; } = string.Empty;

    /// <inheritdoc />
    public DateTimeOffset Timestamp { get; init; }

    /// <summary>
    /// The queue the job was placed in
    /// </summary>
    public string Queue { get; init; } = string.Empty;

    /// <summary>
    /// The serialized input data for the job
    /// </summary>
    public string? InputJson { get; init; }

    /// <summary>
    /// How the job was triggered
    /// </summary>
    public JobTriggerType TriggerType { get; init; }

    /// <summary>
    /// Who or what triggered the job
    /// </summary>
    public string? TriggeredBy { get; init; }

    /// <summary>
    /// When the job is scheduled to run (if scheduled)
    /// </summary>
    public DateTimeOffset? ScheduledAt { get; init; }
}
