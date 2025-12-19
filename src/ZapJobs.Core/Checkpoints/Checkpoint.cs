namespace ZapJobs.Core.Checkpoints;

/// <summary>
/// Represents a saved checkpoint state for a job run.
/// Checkpoints allow long-running jobs to save progress and resume after failures.
/// </summary>
public class Checkpoint
{
    /// <summary>Unique identifier for this checkpoint</summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>The job run this checkpoint belongs to</summary>
    public Guid RunId { get; set; }

    /// <summary>
    /// Checkpoint key/name. Use different keys for different checkpoint types
    /// (e.g., "progress", "batch-state", "cursor").
    /// </summary>
    public string Key { get; set; } = string.Empty;

    /// <summary>Serialized checkpoint data as JSON</summary>
    public string DataJson { get; set; } = string.Empty;

    /// <summary>
    /// Version number for schema migrations.
    /// Increment this when checkpoint data structure changes.
    /// </summary>
    public int Version { get; set; } = 1;

    /// <summary>
    /// Sequence number for ordering checkpoints.
    /// Auto-incremented per run.
    /// </summary>
    public int SequenceNumber { get; set; }

    /// <summary>Size of DataJson in bytes</summary>
    public int DataSizeBytes { get; set; }

    /// <summary>Whether the data is compressed</summary>
    public bool IsCompressed { get; set; }

    /// <summary>When this checkpoint was created</summary>
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>When this checkpoint expires (null = never)</summary>
    public DateTimeOffset? ExpiresAt { get; set; }

    /// <summary>Additional metadata about the checkpoint</summary>
    public string? MetadataJson { get; set; }
}

/// <summary>
/// Metadata stored with a checkpoint for debugging and monitoring
/// </summary>
public class CheckpointMetadata
{
    /// <summary>Job type that created this checkpoint</summary>
    public string? JobTypeId { get; set; }

    /// <summary>Attempt number when checkpoint was saved</summary>
    public int AttemptNumber { get; set; }

    /// <summary>Items processed at checkpoint time</summary>
    public int? ItemsProcessed { get; set; }

    /// <summary>Progress percentage at checkpoint time</summary>
    public int? Progress { get; set; }

    /// <summary>Custom message from the job</summary>
    public string? Message { get; set; }
}
