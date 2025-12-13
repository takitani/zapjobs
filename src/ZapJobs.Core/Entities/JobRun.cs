namespace ZapJobs.Core;

/// <summary>
/// A single execution of a job
/// </summary>
public class JobRun
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>The job type this run belongs to</summary>
    public string JobTypeId { get; set; } = string.Empty;

    // Execution state

    /// <summary>Current status of the run</summary>
    public JobRunStatus Status { get; set; } = JobRunStatus.Pending;

    /// <summary>How this run was triggered</summary>
    public JobTriggerType TriggerType { get; set; } = JobTriggerType.Manual;

    /// <summary>Who/what triggered this run</summary>
    public string? TriggeredBy { get; set; }

    /// <summary>Worker instance processing this run</summary>
    public string? WorkerId { get; set; }

    /// <summary>Queue this run was placed in</summary>
    public string Queue { get; set; } = "default";

    // Timing

    /// <summary>When the run was created/enqueued</summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>When the run is scheduled to execute (for delayed jobs)</summary>
    public DateTime? ScheduledAt { get; set; }

    /// <summary>When execution started</summary>
    public DateTime? StartedAt { get; set; }

    /// <summary>When execution completed (success or failure)</summary>
    public DateTime? CompletedAt { get; set; }

    /// <summary>Execution duration in milliseconds</summary>
    public int? DurationMs { get; set; }

    // Progress

    /// <summary>Current progress (0-100 or item count)</summary>
    public int Progress { get; set; }

    /// <summary>Total items to process (for progress calculation)</summary>
    public int Total { get; set; }

    /// <summary>Current progress message</summary>
    public string? ProgressMessage { get; set; }

    // Metrics

    /// <summary>Number of items processed</summary>
    public int ItemsProcessed { get; set; }

    /// <summary>Number of items that succeeded</summary>
    public int ItemsSucceeded { get; set; }

    /// <summary>Number of items that failed</summary>
    public int ItemsFailed { get; set; }

    // Retry

    /// <summary>Current attempt number (1 = first try, 2 = first retry, etc.)</summary>
    public int AttemptNumber { get; set; } = 1;

    /// <summary>When the next retry should occur</summary>
    public DateTime? NextRetryAt { get; set; }

    // Error

    /// <summary>Error message if failed</summary>
    public string? ErrorMessage { get; set; }

    /// <summary>Error stack trace if failed</summary>
    public string? StackTrace { get; set; }

    /// <summary>Exception type name</summary>
    public string? ErrorType { get; set; }

    // Data

    /// <summary>Input parameters as JSON</summary>
    public string? InputJson { get; set; }

    /// <summary>Output/result as JSON</summary>
    public string? OutputJson { get; set; }

    /// <summary>Additional metadata as JSON</summary>
    public string? MetadataJson { get; set; }

    // Batch

    /// <summary>Batch this run belongs to (if any)</summary>
    public Guid? BatchId { get; set; }
}
