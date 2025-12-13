namespace ZapJobs.Core;

/// <summary>
/// Manages dead letter queue operations for failed jobs
/// </summary>
public interface IDeadLetterManager
{
    /// <summary>Get entries from the dead letter queue</summary>
    Task<IReadOnlyList<DeadLetterEntry>> GetEntriesAsync(
        DeadLetterStatus? status = null,
        string? jobTypeId = null,
        int limit = 100,
        int offset = 0,
        CancellationToken ct = default);

    /// <summary>Get a single entry by ID</summary>
    Task<DeadLetterEntry?> GetEntryAsync(Guid id, CancellationToken ct = default);

    /// <summary>Get count of entries in the dead letter queue</summary>
    Task<int> GetCountAsync(DeadLetterStatus? status = null, CancellationToken ct = default);

    /// <summary>Requeue a dead letter entry for processing</summary>
    /// <returns>New run ID</returns>
    Task<Guid> RequeueAsync(Guid deadLetterId, string? newInput = null, CancellationToken ct = default);

    /// <summary>Discard entry (won't be processed)</summary>
    Task DiscardAsync(Guid deadLetterId, string? notes = null, CancellationToken ct = default);

    /// <summary>Archive entry (keep for records)</summary>
    Task ArchiveAsync(Guid deadLetterId, string? notes = null, CancellationToken ct = default);

    /// <summary>Bulk requeue all pending entries for a job type</summary>
    /// <returns>Number of entries requeued</returns>
    Task<int> RequeueAllAsync(string jobTypeId, CancellationToken ct = default);

    /// <summary>Bulk discard all pending entries for a job type</summary>
    /// <returns>Number of entries discarded</returns>
    Task<int> DiscardAllAsync(string jobTypeId, string? notes = null, CancellationToken ct = default);
}
