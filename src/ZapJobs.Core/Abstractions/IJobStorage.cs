namespace ZapJobs.Core;

/// <summary>
/// Storage backend abstraction - implement for each database type
/// </summary>
public interface IJobStorage
{
    // Job Definitions

    /// <summary>Get a job definition by ID</summary>
    Task<JobDefinition?> GetJobDefinitionAsync(string jobTypeId, CancellationToken ct = default);

    /// <summary>Get all job definitions</summary>
    Task<IReadOnlyList<JobDefinition>> GetAllDefinitionsAsync(CancellationToken ct = default);

    /// <summary>Create or update a job definition</summary>
    Task UpsertDefinitionAsync(JobDefinition definition, CancellationToken ct = default);

    /// <summary>Delete a job definition</summary>
    Task DeleteDefinitionAsync(string jobTypeId, CancellationToken ct = default);

    // Job Runs

    /// <summary>Enqueue a new job run</summary>
    Task<Guid> EnqueueAsync(JobRun run, CancellationToken ct = default);

    /// <summary>Get a run by ID</summary>
    Task<JobRun?> GetRunAsync(Guid runId, CancellationToken ct = default);

    /// <summary>Get pending runs ready for execution</summary>
    Task<IReadOnlyList<JobRun>> GetPendingRunsAsync(string[] queues, int limit = 100, CancellationToken ct = default);

    /// <summary>Get runs awaiting retry</summary>
    Task<IReadOnlyList<JobRun>> GetRunsForRetryAsync(CancellationToken ct = default);

    /// <summary>Get runs by status</summary>
    Task<IReadOnlyList<JobRun>> GetRunsByStatusAsync(JobRunStatus status, int limit = 100, int offset = 0, CancellationToken ct = default);

    /// <summary>Get runs for a job type</summary>
    Task<IReadOnlyList<JobRun>> GetRunsByJobTypeAsync(string jobTypeId, int limit = 100, int offset = 0, CancellationToken ct = default);

    /// <summary>Update a run</summary>
    Task UpdateRunAsync(JobRun run, CancellationToken ct = default);

    /// <summary>Try to acquire a run for processing (returns true if acquired)</summary>
    Task<bool> TryAcquireRunAsync(Guid runId, string workerId, CancellationToken ct = default);

    // Scheduling

    /// <summary>Get jobs that are due for execution</summary>
    Task<IReadOnlyList<JobDefinition>> GetDueJobsAsync(DateTime asOf, CancellationToken ct = default);

    /// <summary>Update the next run time for a job</summary>
    Task UpdateNextRunAsync(string jobTypeId, DateTime? nextRun, DateTime? lastRun = null, JobRunStatus? lastStatus = null, CancellationToken ct = default);

    // Logs

    /// <summary>Add a log entry</summary>
    Task AddLogAsync(JobLog log, CancellationToken ct = default);

    /// <summary>Add multiple log entries</summary>
    Task AddLogsAsync(IEnumerable<JobLog> logs, CancellationToken ct = default);

    /// <summary>Get logs for a run</summary>
    Task<IReadOnlyList<JobLog>> GetLogsAsync(Guid runId, int limit = 500, CancellationToken ct = default);

    // Heartbeats

    /// <summary>Send a heartbeat from a worker</summary>
    Task SendHeartbeatAsync(JobHeartbeat heartbeat, CancellationToken ct = default);

    /// <summary>Get all heartbeats</summary>
    Task<IReadOnlyList<JobHeartbeat>> GetHeartbeatsAsync(CancellationToken ct = default);

    /// <summary>Get stale heartbeats (workers that haven't reported recently)</summary>
    Task<IReadOnlyList<JobHeartbeat>> GetStaleHeartbeatsAsync(TimeSpan threshold, CancellationToken ct = default);

    /// <summary>Remove stale heartbeats</summary>
    Task CleanupStaleHeartbeatsAsync(TimeSpan threshold, CancellationToken ct = default);

    // Continuations

    /// <summary>Add a continuation to a parent run</summary>
    Task AddContinuationAsync(JobContinuation continuation, CancellationToken ct = default);

    /// <summary>Get all continuations for a parent run</summary>
    Task<IReadOnlyList<JobContinuation>> GetContinuationsAsync(Guid parentRunId, CancellationToken ct = default);

    /// <summary>Update a continuation</summary>
    Task UpdateContinuationAsync(JobContinuation continuation, CancellationToken ct = default);

    // Dead Letter Queue

    /// <summary>Move a failed run to the dead letter queue</summary>
    Task MoveToDeadLetterAsync(JobRun failedRun, CancellationToken ct = default);

    /// <summary>Get a dead letter entry by ID</summary>
    Task<DeadLetterEntry?> GetDeadLetterEntryAsync(Guid id, CancellationToken ct = default);

    /// <summary>Get dead letter entries with optional filtering</summary>
    Task<IReadOnlyList<DeadLetterEntry>> GetDeadLetterEntriesAsync(
        DeadLetterStatus? status = null,
        string? jobTypeId = null,
        int limit = 100,
        int offset = 0,
        CancellationToken ct = default);

    /// <summary>Get count of dead letter entries</summary>
    Task<int> GetDeadLetterCountAsync(DeadLetterStatus? status = null, CancellationToken ct = default);

    /// <summary>Update a dead letter entry</summary>
    Task UpdateDeadLetterEntryAsync(DeadLetterEntry entry, CancellationToken ct = default);

    // Maintenance

    /// <summary>Cleanup old completed runs</summary>
    Task<int> CleanupOldRunsAsync(TimeSpan retention, CancellationToken ct = default);

    /// <summary>Cleanup old logs</summary>
    Task<int> CleanupOldLogsAsync(TimeSpan retention, CancellationToken ct = default);

    /// <summary>Get storage statistics</summary>
    Task<JobStorageStats> GetStatsAsync(CancellationToken ct = default);
}

/// <summary>
/// Storage statistics
/// </summary>
public record JobStorageStats(
    int TotalJobs,
    int TotalRuns,
    int PendingRuns,
    int RunningRuns,
    int CompletedToday,
    int FailedToday,
    int ActiveWorkers,
    long TotalLogEntries,
    int DeadLetterCount);
