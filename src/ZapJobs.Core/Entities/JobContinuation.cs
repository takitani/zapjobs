namespace ZapJobs.Core;

/// <summary>
/// Represents a continuation that executes when a parent job completes
/// </summary>
public class JobContinuation
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Parent run that triggers this continuation</summary>
    public Guid ParentRunId { get; set; }

    /// <summary>Job type to execute as continuation</summary>
    public string ContinuationJobTypeId { get; set; } = string.Empty;

    /// <summary>When to execute the continuation</summary>
    public ContinuationCondition Condition { get; set; } = ContinuationCondition.OnSuccess;

    /// <summary>Input for continuation job (JSON)</summary>
    public string? InputJson { get; set; }

    /// <summary>Whether to pass parent output as input to continuation</summary>
    public bool PassParentOutput { get; set; }

    /// <summary>Queue for continuation job (null = use default)</summary>
    public string? Queue { get; set; }

    /// <summary>Status of this continuation</summary>
    public ContinuationStatus Status { get; set; } = ContinuationStatus.Pending;

    /// <summary>Run ID created for this continuation (set when triggered)</summary>
    public Guid? ContinuationRunId { get; set; }

    /// <summary>When this continuation was created</summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
