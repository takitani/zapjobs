namespace ZapJobs.Core;

/// <summary>
/// Represents a continuation that executes when a batch completes
/// </summary>
public class BatchContinuation
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>The batch this continuation belongs to</summary>
    public Guid BatchId { get; set; }

    /// <summary>When to trigger: 'success', 'failure', or 'complete'</summary>
    public string TriggerType { get; set; } = "success";

    /// <summary>Job type to execute as continuation</summary>
    public string JobTypeId { get; set; } = string.Empty;

    /// <summary>Input for continuation job (JSON)</summary>
    public string? InputJson { get; set; }

    /// <summary>Status of this continuation</summary>
    public ContinuationStatus Status { get; set; } = ContinuationStatus.Pending;

    /// <summary>Run ID created for this continuation (set when triggered)</summary>
    public Guid? ContinuationRunId { get; set; }

    /// <summary>When this continuation was created</summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
