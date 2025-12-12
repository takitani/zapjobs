namespace ZapJobs.Core;

/// <summary>
/// API for tracking and monitoring job execution
/// </summary>
public interface IJobTracker
{
    /// <summary>Mark a run as started</summary>
    Task StartRunAsync(Guid runId, string workerId, CancellationToken ct = default);

    /// <summary>Mark a run as completed successfully</summary>
    Task CompleteRunAsync(Guid runId, JobMetrics metrics, object? output = null, CancellationToken ct = default);

    /// <summary>Mark a run as failed</summary>
    Task FailRunAsync(Guid runId, Exception ex, CancellationToken ct = default);

    /// <summary>Update progress during execution</summary>
    Task UpdateProgressAsync(Guid runId, int processed, int total, string? message = null, CancellationToken ct = default);

    /// <summary>Get run details by ID</summary>
    Task<JobRun?> GetRunAsync(Guid runId, CancellationToken ct = default);

    /// <summary>Get runs for a job type</summary>
    Task<IReadOnlyList<JobRun>> GetRunsAsync(string jobTypeId, int limit = 50, int offset = 0, CancellationToken ct = default);

    /// <summary>Get runs by status</summary>
    Task<IReadOnlyList<JobRun>> GetRunsByStatusAsync(JobRunStatus status, int limit = 50, int offset = 0, CancellationToken ct = default);

    /// <summary>Get logs for a run</summary>
    Task<IReadOnlyList<JobLog>> GetLogsAsync(Guid runId, int limit = 500, CancellationToken ct = default);

    /// <summary>Get all job definitions</summary>
    Task<IReadOnlyList<JobDefinition>> GetJobsAsync(CancellationToken ct = default);

    /// <summary>Get worker health status</summary>
    Task<IReadOnlyList<WorkerStatus>> GetWorkerStatusAsync(CancellationToken ct = default);

    /// <summary>Get overall statistics</summary>
    Task<JobStats> GetStatsAsync(CancellationToken ct = default);
}

/// <summary>
/// Metrics from a job execution
/// </summary>
public record JobMetrics(int ItemsProcessed, int ItemsSucceeded, int ItemsFailed);

/// <summary>
/// Status of a worker instance
/// </summary>
public record WorkerStatus(
    string WorkerId,
    string? CurrentJobType,
    Guid? CurrentRunId,
    DateTime LastHeartbeat,
    bool IsHealthy,
    int JobsProcessed,
    DateTime StartedAt);

/// <summary>
/// Overall job system statistics
/// </summary>
public record JobStats(
    int TotalJobs,
    int EnabledJobs,
    int PendingRuns,
    int RunningRuns,
    int CompletedToday,
    int FailedToday,
    int ActiveWorkers);
