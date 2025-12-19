namespace ZapJobs.Core.Checkpoints;

/// <summary>
/// Options for checkpoint behavior. Can be configured globally or per-job.
/// </summary>
public class CheckpointOptions
{
    /// <summary>
    /// Default TTL for checkpoints. Null means no expiration.
    /// Default: 7 days
    /// </summary>
    public TimeSpan? DefaultTtl { get; set; } = TimeSpan.FromDays(7);

    /// <summary>
    /// Maximum size in bytes for checkpoint data.
    /// Larger checkpoints will be rejected.
    /// Default: 1 MB
    /// </summary>
    public int MaxDataSizeBytes { get; set; } = 1024 * 1024; // 1 MB

    /// <summary>
    /// Maximum number of checkpoints to retain per job run.
    /// Oldest checkpoints are deleted when limit is exceeded.
    /// Default: 100
    /// </summary>
    public int MaxCheckpointsPerRun { get; set; } = 100;

    /// <summary>
    /// Minimum data size to trigger automatic compression.
    /// Data smaller than this threshold won't be compressed.
    /// Default: 1 KB
    /// </summary>
    public int CompressionThresholdBytes { get; set; } = 1024; // 1 KB

    /// <summary>
    /// Enable automatic compression for large checkpoint data.
    /// Uses GZip compression.
    /// Default: true
    /// </summary>
    public bool EnableCompression { get; set; } = true;

    /// <summary>
    /// Include metadata with each checkpoint (job type, attempt, progress).
    /// Useful for debugging but adds overhead.
    /// Default: true
    /// </summary>
    public bool IncludeMetadata { get; set; } = true;

    /// <summary>
    /// How often to run the checkpoint cleanup job.
    /// Default: 1 hour
    /// </summary>
    public TimeSpan CleanupInterval { get; set; } = TimeSpan.FromHours(1);

    /// <summary>
    /// Delete checkpoints for completed/failed jobs after this duration.
    /// Null means keep checkpoints until their individual TTL expires.
    /// Default: 24 hours
    /// </summary>
    public TimeSpan? DeleteAfterJobCompletion { get; set; } = TimeSpan.FromHours(24);
}

/// <summary>
/// Context about why/how a job is resuming from a checkpoint.
/// </summary>
public class ResumeContext
{
    /// <summary>
    /// Whether the job is resuming from a previous run.
    /// </summary>
    public bool IsResuming { get; init; }

    /// <summary>
    /// The attempt number that created the checkpoint being resumed from.
    /// </summary>
    public int? PreviousAttempt { get; init; }

    /// <summary>
    /// When the checkpoint was created.
    /// </summary>
    public DateTimeOffset? CheckpointCreatedAt { get; init; }

    /// <summary>
    /// The key of the most recent checkpoint.
    /// </summary>
    public string? LastCheckpointKey { get; init; }

    /// <summary>
    /// Reason for the resume (retry after failure, manual restart, etc).
    /// </summary>
    public ResumeReason Reason { get; init; }
}

/// <summary>
/// Reason why a job is resuming.
/// </summary>
public enum ResumeReason
{
    /// <summary>Not resuming (first attempt)</summary>
    None = 0,

    /// <summary>Automatic retry after transient failure</summary>
    RetryAfterFailure = 1,

    /// <summary>Manual restart via dashboard or API</summary>
    ManualRestart = 2,

    /// <summary>Worker crash or timeout</summary>
    WorkerRecovery = 3,

    /// <summary>System restart (app pool recycle, deployment, etc)</summary>
    SystemRestart = 4
}

/// <summary>
/// Result of a checkpoint save operation.
/// </summary>
public class CheckpointResult
{
    /// <summary>Whether the save was successful</summary>
    public bool Success { get; init; }

    /// <summary>The saved checkpoint ID</summary>
    public Guid? CheckpointId { get; init; }

    /// <summary>Sequence number assigned to the checkpoint</summary>
    public int SequenceNumber { get; init; }

    /// <summary>Whether data was compressed</summary>
    public bool WasCompressed { get; init; }

    /// <summary>Final data size in bytes (after compression if applied)</summary>
    public int DataSizeBytes { get; init; }

    /// <summary>Error message if save failed</summary>
    public string? ErrorMessage { get; init; }

    /// <summary>Creates a successful result</summary>
    public static CheckpointResult Ok(Guid id, int sequenceNumber, bool compressed, int size) => new()
    {
        Success = true,
        CheckpointId = id,
        SequenceNumber = sequenceNumber,
        WasCompressed = compressed,
        DataSizeBytes = size
    };

    /// <summary>Creates a failed result</summary>
    public static CheckpointResult Fail(string error) => new()
    {
        Success = false,
        ErrorMessage = error
    };
}
