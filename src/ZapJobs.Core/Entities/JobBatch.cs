namespace ZapJobs.Core;

/// <summary>
/// Represents a batch of jobs that are created and tracked as a unit
/// </summary>
public class JobBatch
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Name for identification</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Parent batch ID (for nested batches)</summary>
    public Guid? ParentBatchId { get; set; }

    /// <summary>Current status of the batch</summary>
    public BatchStatus Status { get; set; } = BatchStatus.Created;

    /// <summary>Total jobs in this batch</summary>
    public int TotalJobs { get; set; }

    /// <summary>Jobs completed successfully</summary>
    public int CompletedJobs { get; set; }

    /// <summary>Jobs that failed</summary>
    public int FailedJobs { get; set; }

    /// <summary>Who created this batch</summary>
    public string? CreatedBy { get; set; }

    /// <summary>When the batch was created</summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>When the batch started (first job started)</summary>
    public DateTime? StartedAt { get; set; }

    /// <summary>When all jobs finished</summary>
    public DateTime? CompletedAt { get; set; }

    /// <summary>Expiration time for completed batches</summary>
    public DateTime? ExpiresAt { get; set; }
}
