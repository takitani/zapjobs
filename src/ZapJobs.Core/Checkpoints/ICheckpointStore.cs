namespace ZapJobs.Core.Checkpoints;

/// <summary>
/// Storage abstraction for job checkpoints.
/// Implementations handle persistence, compression, and cleanup.
/// </summary>
public interface ICheckpointStore
{
    /// <summary>
    /// Saves a checkpoint for the specified job run.
    /// If a checkpoint with the same key exists, creates a new version.
    /// </summary>
    /// <param name="runId">The job run ID</param>
    /// <param name="key">Checkpoint key (e.g., "progress", "cursor", "batch-state")</param>
    /// <param name="data">Checkpoint data (will be serialized to JSON)</param>
    /// <param name="options">Optional override for checkpoint options</param>
    /// <param name="ct">Cancellation token</param>
    /// <typeparam name="T">Type of the checkpoint data</typeparam>
    /// <returns>Result of the save operation</returns>
    Task<CheckpointResult> SaveAsync<T>(
        Guid runId,
        string key,
        T data,
        CheckpointSaveOptions? options = null,
        CancellationToken ct = default) where T : class;

    /// <summary>
    /// Gets the latest checkpoint for a job run with the specified key.
    /// </summary>
    /// <param name="runId">The job run ID</param>
    /// <param name="key">Checkpoint key</param>
    /// <param name="ct">Cancellation token</param>
    /// <typeparam name="T">Type to deserialize the checkpoint data to</typeparam>
    /// <returns>The checkpoint data, or null if not found</returns>
    Task<T?> GetAsync<T>(Guid runId, string key, CancellationToken ct = default) where T : class;

    /// <summary>
    /// Gets the raw checkpoint entity for a job run with the specified key.
    /// Useful for accessing metadata without deserializing the data.
    /// </summary>
    Task<Checkpoint?> GetCheckpointAsync(Guid runId, string key, CancellationToken ct = default);

    /// <summary>
    /// Gets all checkpoints for a job run, ordered by sequence number descending.
    /// </summary>
    Task<IReadOnlyList<Checkpoint>> GetAllAsync(Guid runId, CancellationToken ct = default);

    /// <summary>
    /// Gets checkpoint history for a specific key, ordered by sequence number descending.
    /// </summary>
    /// <param name="runId">The job run ID</param>
    /// <param name="key">Checkpoint key</param>
    /// <param name="limit">Maximum number of checkpoints to return</param>
    /// <param name="ct">Cancellation token</param>
    Task<IReadOnlyList<Checkpoint>> GetHistoryAsync(
        Guid runId,
        string key,
        int limit = 10,
        CancellationToken ct = default);

    /// <summary>
    /// Checks if a checkpoint exists for the job run.
    /// </summary>
    Task<bool> ExistsAsync(Guid runId, string key, CancellationToken ct = default);

    /// <summary>
    /// Deletes a specific checkpoint by ID.
    /// </summary>
    Task<bool> DeleteAsync(Guid checkpointId, CancellationToken ct = default);

    /// <summary>
    /// Deletes all checkpoints for a job run.
    /// </summary>
    /// <returns>Number of checkpoints deleted</returns>
    Task<int> DeleteAllAsync(Guid runId, CancellationToken ct = default);

    /// <summary>
    /// Deletes checkpoints with the specified key for a job run.
    /// </summary>
    /// <returns>Number of checkpoints deleted</returns>
    Task<int> DeleteByKeyAsync(Guid runId, string key, CancellationToken ct = default);

    /// <summary>
    /// Cleans up expired checkpoints across all job runs.
    /// Called periodically by the cleanup service.
    /// </summary>
    /// <returns>Number of checkpoints deleted</returns>
    Task<int> CleanupExpiredAsync(CancellationToken ct = default);

    /// <summary>
    /// Cleans up checkpoints for completed jobs older than the specified age.
    /// </summary>
    /// <param name="completedJobAge">Delete checkpoints for jobs completed before this duration</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>Number of checkpoints deleted</returns>
    Task<int> CleanupCompletedJobsAsync(TimeSpan completedJobAge, CancellationToken ct = default);

    /// <summary>
    /// Enforces the max checkpoints per run limit.
    /// Deletes oldest checkpoints that exceed the limit.
    /// </summary>
    /// <param name="runId">The job run ID</param>
    /// <param name="maxCheckpoints">Maximum checkpoints to keep</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>Number of checkpoints deleted</returns>
    Task<int> EnforceLimitAsync(Guid runId, int maxCheckpoints, CancellationToken ct = default);
}

/// <summary>
/// Options for a specific checkpoint save operation.
/// </summary>
public class CheckpointSaveOptions
{
    /// <summary>
    /// Override the default TTL for this checkpoint.
    /// Null uses the global default.
    /// </summary>
    public TimeSpan? Ttl { get; set; }

    /// <summary>
    /// Override compression setting for this checkpoint.
    /// Null uses the global default.
    /// </summary>
    public bool? Compress { get; set; }

    /// <summary>
    /// Schema version for this checkpoint data.
    /// Use to handle migrations when checkpoint structure changes.
    /// </summary>
    public int Version { get; set; } = 1;

    /// <summary>
    /// Custom metadata to store with the checkpoint.
    /// </summary>
    public CheckpointMetadata? Metadata { get; set; }
}
