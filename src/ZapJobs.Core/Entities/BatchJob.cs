namespace ZapJobs.Core;

/// <summary>
/// Links a job run to its parent batch
/// </summary>
public class BatchJob
{
    /// <summary>The batch this job belongs to</summary>
    public Guid BatchId { get; set; }

    /// <summary>The job run</summary>
    public Guid RunId { get; set; }

    /// <summary>Order within the batch (for display purposes)</summary>
    public int Order { get; set; }
}
