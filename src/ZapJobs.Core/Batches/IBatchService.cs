namespace ZapJobs.Core;

/// <summary>
/// Service for creating and managing job batches
/// </summary>
public interface IBatchService
{
    /// <summary>Create a new batch with jobs</summary>
    Task<Guid> CreateBatchAsync(
        string name,
        Action<IBatchBuilder> configure,
        string? createdBy = null,
        CancellationToken ct = default);

    /// <summary>Get batch by ID</summary>
    Task<JobBatch?> GetBatchAsync(Guid batchId, CancellationToken ct = default);

    /// <summary>Get all jobs in a batch</summary>
    Task<IReadOnlyList<JobRun>> GetBatchJobsAsync(Guid batchId, CancellationToken ct = default);

    /// <summary>Get nested batches within a parent batch</summary>
    Task<IReadOnlyList<JobBatch>> GetNestedBatchesAsync(Guid parentBatchId, CancellationToken ct = default);

    /// <summary>Add more jobs to an existing batch that hasn't completed</summary>
    Task AddJobsToBatchAsync(
        Guid batchId,
        Action<IBatchBuilder> configure,
        CancellationToken ct = default);

    /// <summary>Cancel all pending jobs in batch</summary>
    Task CancelBatchAsync(Guid batchId, CancellationToken ct = default);

    /// <summary>Create a continuation batch that runs after the specified batch completes</summary>
    Task<Guid> ContinueBatchWithAsync(
        Guid batchId,
        string name,
        Action<IBatchBuilder> configure,
        CancellationToken ct = default);
}
