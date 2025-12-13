namespace ZapJobs.Core;

/// <summary>
/// Represents a job that has permanently failed after exhausting all retry attempts.
/// Stored in the dead letter queue for review and potential reprocessing.
/// </summary>
public class DeadLetterEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Original run ID that failed</summary>
    public Guid OriginalRunId { get; set; }

    /// <summary>Job type that failed</summary>
    public string JobTypeId { get; set; } = string.Empty;

    /// <summary>Queue the job was in</summary>
    public string Queue { get; set; } = "default";

    /// <summary>Original input as JSON</summary>
    public string? InputJson { get; set; }

    /// <summary>Last error message</summary>
    public string ErrorMessage { get; set; } = string.Empty;

    /// <summary>Last error type (exception type name)</summary>
    public string? ErrorType { get; set; }

    /// <summary>Last stack trace</summary>
    public string? StackTrace { get; set; }

    /// <summary>Number of attempts before moving to DLQ</summary>
    public int AttemptCount { get; set; }

    /// <summary>When moved to DLQ</summary>
    public DateTime MovedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Status in DLQ</summary>
    public DeadLetterStatus Status { get; set; } = DeadLetterStatus.Pending;

    /// <summary>When requeued (if applicable)</summary>
    public DateTime? RequeuedAt { get; set; }

    /// <summary>New run ID after requeue</summary>
    public Guid? RequeuedRunId { get; set; }

    /// <summary>Notes from operator</summary>
    public string? Notes { get; set; }
}
