namespace ZapJobs.Core.History;

// Typed event payloads for structured event data.
// Unlike Hangfire's text logs, these are queryable and analyzable.

#region Job Lifecycle Payloads

/// <summary>Payload for JobScheduled event</summary>
public record JobScheduledPayload(
    string JobTypeId,
    string Queue,
    DateTimeOffset ScheduledFor,
    string? InputJson,
    string? CronExpression,
    bool IsRecurring);

/// <summary>Payload for JobEnqueued event</summary>
public record JobEnqueuedPayload(
    string JobTypeId,
    string Queue,
    string? InputJson,
    int? Priority);

/// <summary>Payload for JobStarted event</summary>
public record JobStartedPayload(
    string WorkerId,
    int AttemptNumber,
    bool IsRetry,
    bool IsResuming,
    string? ResumeReason);

/// <summary>Payload for JobCompleted event</summary>
public record JobCompletedPayload(
    long DurationMs,
    string? OutputJson,
    int SucceededCount,
    int FailedCount,
    int? ItemsProcessed);

/// <summary>Payload for JobFailed event</summary>
public record JobFailedPayload(
    string ErrorMessage,
    string? ErrorType,
    string? StackTrace,
    int AttemptNumber,
    int MaxRetries,
    bool WillRetry,
    DateTimeOffset? NextRetryAt,
    bool MovedToDeadLetter);

/// <summary>Payload for JobRetrying event</summary>
public record JobRetryingPayload(
    int FailedAttempt,
    int NextAttempt,
    int MaxRetries,
    long DelayMs,
    DateTimeOffset RetryAt,
    string? ErrorMessage);

/// <summary>Payload for JobCancelled event</summary>
public record JobCancelledPayload(
    string Reason,
    string? CancelledBy,
    int AttemptNumber);

/// <summary>Payload for JobTimedOut event</summary>
public record JobTimedOutPayload(
    long TimeoutMs,
    long ActualDurationMs,
    int AttemptNumber);

/// <summary>Payload for JobSkipped event</summary>
public record JobSkippedPayload(
    string Reason,
    string? SkipType);

/// <summary>Payload for JobDelayed event</summary>
public record JobDelayedPayload(
    string Reason,
    long DelayMs,
    DateTimeOffset DelayedUntil);

/// <summary>Payload for JobDeadLettered event</summary>
public record JobDeadLetteredPayload(
    string Reason,
    int TotalAttempts,
    string? LastError,
    DateTimeOffset FailedAt);

/// <summary>Payload for JobRequeued event</summary>
public record JobRequeuedPayload(
    Guid DeadLetterId,
    Guid NewRunId,
    string? RequeueReason,
    bool InputModified);

#endregion

#region Activity Payloads

/// <summary>Payload for ActivityStarted event</summary>
public record ActivityStartedPayload(
    string ActivityId,
    string ActivityType,
    int SequenceNumber,
    string? InputJson);

/// <summary>Payload for ActivityCompleted event</summary>
public record ActivityCompletedPayload(
    string ActivityId,
    string ActivityType,
    int SequenceNumber,
    long DurationMs,
    string? OutputJson);

/// <summary>Payload for ActivityFailed event</summary>
public record ActivityFailedPayload(
    string ActivityId,
    string ActivityType,
    int SequenceNumber,
    string ErrorMessage,
    string? ErrorType,
    bool WillRetry);

#endregion

#region Workflow Payloads

/// <summary>Payload for WorkflowStarted event</summary>
public record WorkflowStartedPayload(
    string WorkflowName,
    int TotalJobs,
    string? Description);

/// <summary>Payload for WorkflowCompleted event</summary>
public record WorkflowCompletedPayload(
    long DurationMs,
    int SucceededJobs,
    int FailedJobs,
    int TotalJobs);

/// <summary>Payload for ContinuationTriggered event</summary>
public record ContinuationTriggeredPayload(
    Guid ParentRunId,
    Guid ContinuationRunId,
    string ContinuationJobTypeId,
    string Condition,
    bool PassedParentOutput);

#endregion

#region State Payloads

/// <summary>Payload for CheckpointSaved event</summary>
public record CheckpointSavedPayload(
    string Key,
    int SequenceNumber,
    int DataSizeBytes,
    bool WasCompressed,
    int? Version);

/// <summary>Payload for CheckpointRestored event</summary>
public record CheckpointRestoredPayload(
    string Key,
    int SequenceNumber,
    DateTimeOffset CreatedAt,
    int? ItemsProcessedBefore);

/// <summary>Payload for OutputSet event</summary>
public record OutputSetPayload(
    string OutputType,
    int DataSizeBytes);

#endregion

#region Progress Payloads

/// <summary>Payload for ProgressUpdated event</summary>
public record ProgressUpdatedPayload(
    int Current,
    int Total,
    double Percentage,
    string? Message,
    long? EstimatedRemainingMs);

/// <summary>Payload for ItemProcessed event</summary>
public record ItemProcessedPayload(
    string? ItemId,
    int Index,
    bool Success,
    string? ErrorMessage,
    long ProcessingTimeMs);

/// <summary>Payload for MilestoneReached event</summary>
public record MilestoneReachedPayload(
    string MilestoneName,
    int ItemsProcessed,
    long ElapsedMs,
    string? Description);

/// <summary>Payload for Heartbeat event</summary>
public record HeartbeatPayload(
    string WorkerId,
    int? ProgressPercentage,
    string? CurrentOperation);

#endregion

#region System Payloads

/// <summary>Payload for WorkerAssigned event</summary>
public record WorkerAssignedPayload(
    string WorkerId,
    string? WorkerHost,
    int WorkerThread);

/// <summary>Payload for RateLimitHit event</summary>
public record RateLimitHitPayload(
    string LimitType,
    string LimitScope,
    int CurrentCount,
    int MaxAllowed,
    long WindowMs,
    string Action);

/// <summary>Payload for TimerScheduled event</summary>
public record TimerScheduledPayload(
    string TimerId,
    DateTimeOffset FireAt,
    string? Purpose);

/// <summary>Payload for TimerFired event</summary>
public record TimerFiredPayload(
    string TimerId,
    DateTimeOffset ScheduledFor,
    long DelayMs);

#endregion

#region Custom Payloads

/// <summary>Payload for LogMessage event</summary>
public record LogMessagePayload(
    string Message,
    EventSeverity Severity,
    Dictionary<string, object?>? Properties);

/// <summary>Payload for MetricRecorded event</summary>
public record MetricRecordedPayload(
    string MetricName,
    double Value,
    string? Unit,
    Dictionary<string, string>? Tags);

/// <summary>Payload for BusinessEvent event</summary>
public record BusinessEventPayload(
    string EventName,
    string? EntityType,
    string? EntityId,
    Dictionary<string, object?>? Data);

#endregion

#region Aggregation Results

/// <summary>Summary statistics for a job's event history</summary>
public record EventHistorySummary
{
    /// <summary>Total number of events</summary>
    public int TotalEvents { get; init; }

    /// <summary>Events by category</summary>
    public Dictionary<string, int> EventsByCategory { get; init; } = new();

    /// <summary>Events by type</summary>
    public Dictionary<string, int> EventsByType { get; init; } = new();

    /// <summary>First event timestamp</summary>
    public DateTimeOffset? FirstEventAt { get; init; }

    /// <summary>Last event timestamp</summary>
    public DateTimeOffset? LastEventAt { get; init; }

    /// <summary>Total duration in milliseconds</summary>
    public long? TotalDurationMs { get; init; }

    /// <summary>Number of errors</summary>
    public int ErrorCount { get; init; }

    /// <summary>Number of retries</summary>
    public int RetryCount { get; init; }

    /// <summary>Average event rate (events per second)</summary>
    public double? EventsPerSecond { get; init; }
}

/// <summary>Timeline entry for display</summary>
public record TimelineEntry
{
    /// <summary>Event ID</summary>
    public Guid Id { get; init; }

    /// <summary>Event type</summary>
    public string EventType { get; init; } = string.Empty;

    /// <summary>Event category</summary>
    public string Category { get; init; } = string.Empty;

    /// <summary>When it occurred</summary>
    public DateTimeOffset Timestamp { get; init; }

    /// <summary>Time since previous event</summary>
    public long? MsSincePrevious { get; init; }

    /// <summary>Display title</summary>
    public string Title { get; init; } = string.Empty;

    /// <summary>Display description</summary>
    public string? Description { get; init; }

    /// <summary>Whether this is a success event</summary>
    public bool? IsSuccess { get; init; }

    /// <summary>Duration if applicable</summary>
    public long? DurationMs { get; init; }

    /// <summary>Severity level</summary>
    public EventSeverity Severity { get; init; }

    /// <summary>Parsed payload data</summary>
    public object? Payload { get; init; }
}

/// <summary>Job state reconstructed from events (for time-travel)</summary>
public record JobStateSnapshot
{
    /// <summary>Run ID</summary>
    public Guid RunId { get; init; }

    /// <summary>State at this point in time</summary>
    public string Status { get; init; } = string.Empty;

    /// <summary>Current attempt number</summary>
    public int AttemptNumber { get; init; }

    /// <summary>Progress percentage</summary>
    public int? ProgressPercentage { get; init; }

    /// <summary>Items processed</summary>
    public int? ItemsProcessed { get; init; }

    /// <summary>Last checkpoint key</summary>
    public string? LastCheckpointKey { get; init; }

    /// <summary>Current/last error</summary>
    public string? CurrentError { get; init; }

    /// <summary>Current worker</summary>
    public string? CurrentWorker { get; init; }

    /// <summary>Snapshot timestamp</summary>
    public DateTimeOffset SnapshotAt { get; init; }

    /// <summary>Events applied to reach this state</summary>
    public int EventsApplied { get; init; }

    /// <summary>Elapsed time from start</summary>
    public long? ElapsedMs { get; init; }
}

#endregion
