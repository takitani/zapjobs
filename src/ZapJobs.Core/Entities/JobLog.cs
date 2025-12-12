namespace ZapJobs.Core;

/// <summary>
/// A log entry for a job run
/// </summary>
public class JobLog
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>The run this log belongs to</summary>
    public Guid RunId { get; set; }

    /// <summary>Log level</summary>
    public JobLogLevel Level { get; set; } = JobLogLevel.Info;

    /// <summary>Log message</summary>
    public string Message { get; set; } = string.Empty;

    /// <summary>Category/phase (e.g., "init", "process", "complete")</summary>
    public string? Category { get; set; }

    /// <summary>Additional context data as JSON</summary>
    public string? ContextJson { get; set; }

    /// <summary>Duration of the operation in milliseconds (optional)</summary>
    public int? DurationMs { get; set; }

    /// <summary>Exception details if any</summary>
    public string? Exception { get; set; }

    /// <summary>When this log was created</summary>
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}
