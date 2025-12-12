using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ZapJobs.Core;

namespace ZapJobs.Tracking;

/// <summary>
/// Implementation of IJobTracker for monitoring jobs
/// </summary>
public class JobTrackerService : IJobTracker
{
    private readonly IJobStorage _storage;
    private readonly ZapJobsOptions _options;
    private readonly ILogger<JobTrackerService> _logger;

    public JobTrackerService(
        IJobStorage storage,
        IOptions<ZapJobsOptions> options,
        ILogger<JobTrackerService> logger)
    {
        _storage = storage;
        _options = options.Value;
        _logger = logger;
    }

    public async Task StartRunAsync(Guid runId, string workerId, CancellationToken ct = default)
    {
        var run = await _storage.GetRunAsync(runId, ct);
        if (run == null)
            return;

        run.Status = JobRunStatus.Running;
        run.StartedAt = DateTime.UtcNow;
        run.WorkerId = workerId;
        await _storage.UpdateRunAsync(run, ct);

        _logger.LogDebug("Started run {RunId} on worker {WorkerId}", runId, workerId);
    }

    public async Task CompleteRunAsync(Guid runId, JobMetrics metrics, object? output = null, CancellationToken ct = default)
    {
        var run = await _storage.GetRunAsync(runId, ct);
        if (run == null)
            return;

        run.Status = JobRunStatus.Completed;
        run.CompletedAt = DateTime.UtcNow;
        run.DurationMs = run.StartedAt.HasValue
            ? (int)(DateTime.UtcNow - run.StartedAt.Value).TotalMilliseconds
            : null;
        run.ItemsProcessed = metrics.ItemsProcessed;
        run.ItemsSucceeded = metrics.ItemsSucceeded;
        run.ItemsFailed = metrics.ItemsFailed;
        run.Progress = 100;

        if (output != null)
        {
            run.OutputJson = JsonSerializer.Serialize(output);
        }

        await _storage.UpdateRunAsync(run, ct);

        _logger.LogInformation(
            "Completed run {RunId}: {Processed} processed, {Succeeded} succeeded, {Failed} failed",
            runId, metrics.ItemsProcessed, metrics.ItemsSucceeded, metrics.ItemsFailed);
    }

    public async Task FailRunAsync(Guid runId, Exception ex, CancellationToken ct = default)
    {
        var run = await _storage.GetRunAsync(runId, ct);
        if (run == null)
            return;

        run.Status = JobRunStatus.Failed;
        run.CompletedAt = DateTime.UtcNow;
        run.DurationMs = run.StartedAt.HasValue
            ? (int)(DateTime.UtcNow - run.StartedAt.Value).TotalMilliseconds
            : null;
        run.ErrorMessage = ex.Message;
        run.StackTrace = ex.StackTrace;

        await _storage.UpdateRunAsync(run, ct);

        _logger.LogError(ex, "Run {RunId} failed: {Message}", runId, ex.Message);
    }

    public async Task UpdateProgressAsync(Guid runId, int processed, int total, string? message = null, CancellationToken ct = default)
    {
        var run = await _storage.GetRunAsync(runId, ct);
        if (run == null)
            return;

        run.Progress = total > 0 ? (int)((processed / (double)total) * 100) : 0;
        run.ItemsProcessed = processed;
        run.Total = total;
        run.ProgressMessage = message;

        await _storage.UpdateRunAsync(run, ct);
    }

    public async Task<JobRun?> GetRunAsync(Guid runId, CancellationToken ct = default)
    {
        return await _storage.GetRunAsync(runId, ct);
    }

    public async Task<IReadOnlyList<JobRun>> GetRunsAsync(string jobTypeId, int limit = 50, int offset = 0, CancellationToken ct = default)
    {
        return await _storage.GetRunsByJobTypeAsync(jobTypeId, limit, offset, ct);
    }

    public async Task<IReadOnlyList<JobRun>> GetRunsByStatusAsync(JobRunStatus status, int limit = 50, int offset = 0, CancellationToken ct = default)
    {
        return await _storage.GetRunsByStatusAsync(status, limit, offset, ct);
    }

    public async Task<IReadOnlyList<JobLog>> GetLogsAsync(Guid runId, int limit = 500, CancellationToken ct = default)
    {
        return await _storage.GetLogsAsync(runId, limit, ct);
    }

    public async Task<IReadOnlyList<JobDefinition>> GetJobsAsync(CancellationToken ct = default)
    {
        return await _storage.GetAllDefinitionsAsync(ct);
    }

    public async Task<IReadOnlyList<WorkerStatus>> GetWorkerStatusAsync(CancellationToken ct = default)
    {
        var heartbeats = await _storage.GetHeartbeatsAsync(ct);
        var healthyThreshold = DateTime.UtcNow.AddMinutes(-2);

        return heartbeats.Select(h => new WorkerStatus(
            WorkerId: h.WorkerId,
            CurrentJobType: h.CurrentJobType,
            CurrentRunId: h.CurrentRunId,
            LastHeartbeat: h.Timestamp,
            IsHealthy: h.Timestamp >= healthyThreshold && !h.IsShuttingDown,
            JobsProcessed: h.JobsProcessed,
            StartedAt: h.StartedAt
        )).ToList();
    }

    public async Task<JobStats> GetStatsAsync(CancellationToken ct = default)
    {
        var storageStats = await _storage.GetStatsAsync(ct);

        return new JobStats(
            TotalJobs: storageStats.TotalJobs,
            EnabledJobs: storageStats.TotalJobs, // Storage stats doesn't have enabled count separately
            PendingRuns: storageStats.PendingRuns,
            RunningRuns: storageStats.RunningRuns,
            CompletedToday: storageStats.CompletedToday,
            FailedToday: storageStats.FailedToday,
            ActiveWorkers: storageStats.ActiveWorkers
        );
    }
}
