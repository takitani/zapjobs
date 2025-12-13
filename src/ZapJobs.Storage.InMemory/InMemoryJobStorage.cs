using System.Collections.Concurrent;
using ZapJobs.Core;

namespace ZapJobs.Storage.InMemory;

/// <summary>
/// In-memory implementation of IJobStorage for development and testing
/// </summary>
public class InMemoryJobStorage : IJobStorage
{
    private readonly ConcurrentDictionary<string, JobDefinition> _definitions = new();
    private readonly ConcurrentDictionary<Guid, JobRun> _runs = new();
    private readonly ConcurrentDictionary<Guid, List<JobLog>> _logs = new();
    private readonly ConcurrentDictionary<string, JobHeartbeat> _heartbeats = new();
    private readonly ConcurrentDictionary<Guid, JobContinuation> _continuations = new();
    private readonly ConcurrentDictionary<Guid, DeadLetterEntry> _deadLetterEntries = new();
    private readonly ConcurrentDictionary<Guid, JobBatch> _batches = new();
    private readonly ConcurrentDictionary<Guid, List<BatchJob>> _batchJobs = new();
    private readonly ConcurrentDictionary<Guid, BatchContinuation> _batchContinuations = new();
    private readonly ConcurrentDictionary<string, List<DateTime>> _rateLimitExecutions = new();
    private readonly object _lock = new();

    /// <summary>
    /// Internal access to heartbeats for testing purposes
    /// </summary>
    internal ConcurrentDictionary<string, JobHeartbeat> Heartbeats => _heartbeats;

    // Job Definitions

    public Task<JobDefinition?> GetJobDefinitionAsync(string jobTypeId, CancellationToken ct = default)
    {
        _definitions.TryGetValue(jobTypeId, out var definition);
        return Task.FromResult(definition);
    }

    public Task<IReadOnlyList<JobDefinition>> GetAllDefinitionsAsync(CancellationToken ct = default)
    {
        return Task.FromResult<IReadOnlyList<JobDefinition>>(_definitions.Values.ToList());
    }

    public Task UpsertDefinitionAsync(JobDefinition definition, CancellationToken ct = default)
    {
        definition.UpdatedAt = DateTime.UtcNow;
        _definitions[definition.JobTypeId] = definition;
        return Task.CompletedTask;
    }

    public Task DeleteDefinitionAsync(string jobTypeId, CancellationToken ct = default)
    {
        _definitions.TryRemove(jobTypeId, out _);
        return Task.CompletedTask;
    }

    // Job Runs

    public Task<Guid> EnqueueAsync(JobRun run, CancellationToken ct = default)
    {
        run.CreatedAt = DateTime.UtcNow;
        _runs[run.Id] = run;
        return Task.FromResult(run.Id);
    }

    public Task<JobRun?> GetRunAsync(Guid runId, CancellationToken ct = default)
    {
        _runs.TryGetValue(runId, out var run);
        return Task.FromResult(run);
    }

    public Task<IReadOnlyList<JobRun>> GetPendingRunsAsync(string[] queues, int limit = 100, CancellationToken ct = default)
    {
        var runs = _runs.Values
            .Where(r => r.Status == JobRunStatus.Pending && queues.Contains(r.Queue))
            .OrderBy(r => r.CreatedAt)
            .Take(limit)
            .ToList();

        return Task.FromResult<IReadOnlyList<JobRun>>(runs);
    }

    public Task<IReadOnlyList<JobRun>> GetRunsForRetryAsync(CancellationToken ct = default)
    {
        var runs = _runs.Values
            .Where(r => r.Status == JobRunStatus.AwaitingRetry)
            .OrderBy(r => r.NextRetryAt)
            .ToList();

        return Task.FromResult<IReadOnlyList<JobRun>>(runs);
    }

    public Task<IReadOnlyList<JobRun>> GetRunsByStatusAsync(JobRunStatus status, int limit = 100, int offset = 0, CancellationToken ct = default)
    {
        var runs = _runs.Values
            .Where(r => r.Status == status)
            .OrderByDescending(r => r.CreatedAt)
            .Skip(offset)
            .Take(limit)
            .ToList();

        return Task.FromResult<IReadOnlyList<JobRun>>(runs);
    }

    public Task<IReadOnlyList<JobRun>> GetRunsByJobTypeAsync(string jobTypeId, int limit = 100, int offset = 0, CancellationToken ct = default)
    {
        var runs = _runs.Values
            .Where(r => r.JobTypeId == jobTypeId)
            .OrderByDescending(r => r.CreatedAt)
            .Skip(offset)
            .Take(limit)
            .ToList();

        return Task.FromResult<IReadOnlyList<JobRun>>(runs);
    }

    public Task<bool> HasActiveRunAsync(string jobTypeId, CancellationToken ct = default)
    {
        var hasActive = _runs.Values.Any(r =>
            r.JobTypeId == jobTypeId &&
            (r.Status == JobRunStatus.Pending || r.Status == JobRunStatus.Running));

        return Task.FromResult(hasActive);
    }

    public Task UpdateRunAsync(JobRun run, CancellationToken ct = default)
    {
        _runs[run.Id] = run;
        return Task.CompletedTask;
    }

    public Task<bool> TryAcquireRunAsync(Guid runId, string workerId, CancellationToken ct = default)
    {
        lock (_lock)
        {
            if (!_runs.TryGetValue(runId, out var run))
                return Task.FromResult(false);

            if (run.Status != JobRunStatus.Pending)
                return Task.FromResult(false);

            run.Status = JobRunStatus.Running;
            run.WorkerId = workerId;
            run.StartedAt = DateTime.UtcNow;
            return Task.FromResult(true);
        }
    }

    // Scheduling

    public Task<IReadOnlyList<JobDefinition>> GetDueJobsAsync(DateTime asOf, CancellationToken ct = default)
    {
        var jobs = _definitions.Values
            .Where(d => d.IsEnabled && d.NextRunAt.HasValue && d.NextRunAt <= asOf)
            .ToList();

        return Task.FromResult<IReadOnlyList<JobDefinition>>(jobs);
    }

    public Task UpdateNextRunAsync(string jobTypeId, DateTime? nextRun, DateTime? lastRun = null, JobRunStatus? lastStatus = null, CancellationToken ct = default)
    {
        if (_definitions.TryGetValue(jobTypeId, out var definition))
        {
            definition.NextRunAt = nextRun;
            if (lastRun.HasValue)
                definition.LastRunAt = lastRun;
            if (lastStatus.HasValue)
                definition.LastRunStatus = lastStatus;
            definition.UpdatedAt = DateTime.UtcNow;
        }
        return Task.CompletedTask;
    }

    // Logs

    public Task AddLogAsync(JobLog log, CancellationToken ct = default)
    {
        var logs = _logs.GetOrAdd(log.RunId, _ => new List<JobLog>());
        lock (logs)
        {
            logs.Add(log);
        }
        return Task.CompletedTask;
    }

    public Task AddLogsAsync(IEnumerable<JobLog> logs, CancellationToken ct = default)
    {
        foreach (var log in logs)
        {
            var runLogs = _logs.GetOrAdd(log.RunId, _ => new List<JobLog>());
            lock (runLogs)
            {
                runLogs.Add(log);
            }
        }
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<JobLog>> GetLogsAsync(Guid runId, int limit = 500, CancellationToken ct = default)
    {
        if (_logs.TryGetValue(runId, out var logs))
        {
            lock (logs)
            {
                return Task.FromResult<IReadOnlyList<JobLog>>(
                    logs.OrderByDescending(l => l.Timestamp).Take(limit).ToList());
            }
        }
        return Task.FromResult<IReadOnlyList<JobLog>>(Array.Empty<JobLog>());
    }

    // Heartbeats

    public Task SendHeartbeatAsync(JobHeartbeat heartbeat, CancellationToken ct = default)
    {
        heartbeat.Timestamp = DateTime.UtcNow;
        _heartbeats[heartbeat.WorkerId] = heartbeat;
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<JobHeartbeat>> GetHeartbeatsAsync(CancellationToken ct = default)
    {
        return Task.FromResult<IReadOnlyList<JobHeartbeat>>(_heartbeats.Values.ToList());
    }

    public Task<IReadOnlyList<JobHeartbeat>> GetStaleHeartbeatsAsync(TimeSpan threshold, CancellationToken ct = default)
    {
        var cutoff = DateTime.UtcNow - threshold;
        var stale = _heartbeats.Values
            .Where(h => h.Timestamp < cutoff)
            .ToList();

        return Task.FromResult<IReadOnlyList<JobHeartbeat>>(stale);
    }

    public Task CleanupStaleHeartbeatsAsync(TimeSpan threshold, CancellationToken ct = default)
    {
        var cutoff = DateTime.UtcNow - threshold;
        var staleKeys = _heartbeats
            .Where(kvp => kvp.Value.Timestamp < cutoff)
            .Select(kvp => kvp.Key)
            .ToList();

        foreach (var key in staleKeys)
        {
            _heartbeats.TryRemove(key, out _);
        }

        return Task.CompletedTask;
    }

    // Continuations

    public Task AddContinuationAsync(JobContinuation continuation, CancellationToken ct = default)
    {
        continuation.CreatedAt = DateTime.UtcNow;
        _continuations[continuation.Id] = continuation;
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<JobContinuation>> GetContinuationsAsync(Guid parentRunId, CancellationToken ct = default)
    {
        var continuations = _continuations.Values
            .Where(c => c.ParentRunId == parentRunId)
            .OrderBy(c => c.CreatedAt)
            .ToList();

        return Task.FromResult<IReadOnlyList<JobContinuation>>(continuations);
    }

    public Task UpdateContinuationAsync(JobContinuation continuation, CancellationToken ct = default)
    {
        _continuations[continuation.Id] = continuation;
        return Task.CompletedTask;
    }

    // Dead Letter Queue

    public Task MoveToDeadLetterAsync(JobRun failedRun, CancellationToken ct = default)
    {
        var entry = new DeadLetterEntry
        {
            Id = Guid.NewGuid(),
            OriginalRunId = failedRun.Id,
            JobTypeId = failedRun.JobTypeId,
            Queue = failedRun.Queue,
            InputJson = failedRun.InputJson,
            ErrorMessage = failedRun.ErrorMessage ?? string.Empty,
            ErrorType = failedRun.ErrorType,
            StackTrace = failedRun.StackTrace,
            AttemptCount = failedRun.AttemptNumber,
            MovedAt = DateTime.UtcNow,
            Status = DeadLetterStatus.Pending
        };

        _deadLetterEntries[entry.Id] = entry;
        return Task.CompletedTask;
    }

    public Task<DeadLetterEntry?> GetDeadLetterEntryAsync(Guid id, CancellationToken ct = default)
    {
        _deadLetterEntries.TryGetValue(id, out var entry);
        return Task.FromResult(entry);
    }

    public Task<IReadOnlyList<DeadLetterEntry>> GetDeadLetterEntriesAsync(
        DeadLetterStatus? status = null,
        string? jobTypeId = null,
        int limit = 100,
        int offset = 0,
        CancellationToken ct = default)
    {
        var query = _deadLetterEntries.Values.AsEnumerable();

        if (status.HasValue)
            query = query.Where(e => e.Status == status.Value);

        if (!string.IsNullOrEmpty(jobTypeId))
            query = query.Where(e => e.JobTypeId == jobTypeId);

        var entries = query
            .OrderByDescending(e => e.MovedAt)
            .Skip(offset)
            .Take(limit)
            .ToList();

        return Task.FromResult<IReadOnlyList<DeadLetterEntry>>(entries);
    }

    public Task<int> GetDeadLetterCountAsync(DeadLetterStatus? status = null, CancellationToken ct = default)
    {
        var query = _deadLetterEntries.Values.AsEnumerable();

        if (status.HasValue)
            query = query.Where(e => e.Status == status.Value);

        return Task.FromResult(query.Count());
    }

    public Task<int> GetDeadLetterCountAsync(CancellationToken ct = default)
    {
        var count = _runs.Values.Count(r => r.Status == JobRunStatus.Failed);
        return Task.FromResult(count);
    }

    public Task UpdateDeadLetterEntryAsync(DeadLetterEntry entry, CancellationToken ct = default)
    {
        _deadLetterEntries[entry.Id] = entry;
        return Task.CompletedTask;
    }

    // Batches

    public Task CreateBatchAsync(JobBatch batch, CancellationToken ct = default)
    {
        batch.CreatedAt = DateTime.UtcNow;
        _batches[batch.Id] = batch;
        return Task.CompletedTask;
    }

    public Task<JobBatch?> GetBatchAsync(Guid batchId, CancellationToken ct = default)
    {
        _batches.TryGetValue(batchId, out var batch);
        return Task.FromResult(batch);
    }

    public Task<IReadOnlyList<JobBatch>> GetNestedBatchesAsync(Guid parentBatchId, CancellationToken ct = default)
    {
        var batches = _batches.Values
            .Where(b => b.ParentBatchId == parentBatchId)
            .OrderBy(b => b.CreatedAt)
            .ToList();

        return Task.FromResult<IReadOnlyList<JobBatch>>(batches);
    }

    public Task UpdateBatchAsync(JobBatch batch, CancellationToken ct = default)
    {
        _batches[batch.Id] = batch;
        return Task.CompletedTask;
    }

    public Task AddBatchJobAsync(BatchJob batchJob, CancellationToken ct = default)
    {
        var jobs = _batchJobs.GetOrAdd(batchJob.BatchId, _ => new List<BatchJob>());
        lock (jobs)
        {
            jobs.Add(batchJob);
        }
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<JobRun>> GetBatchJobsAsync(Guid batchId, CancellationToken ct = default)
    {
        if (_batchJobs.TryGetValue(batchId, out var batchJobs))
        {
            lock (batchJobs)
            {
                var runIds = batchJobs.OrderBy(bj => bj.Order).Select(bj => bj.RunId).ToList();
                var runs = runIds
                    .Select(id => _runs.TryGetValue(id, out var run) ? run : null)
                    .Where(r => r != null)
                    .Cast<JobRun>()
                    .ToList();

                return Task.FromResult<IReadOnlyList<JobRun>>(runs);
            }
        }
        return Task.FromResult<IReadOnlyList<JobRun>>(Array.Empty<JobRun>());
    }

    public Task AddBatchContinuationAsync(BatchContinuation continuation, CancellationToken ct = default)
    {
        continuation.CreatedAt = DateTime.UtcNow;
        _batchContinuations[continuation.Id] = continuation;
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<BatchContinuation>> GetBatchContinuationsAsync(Guid batchId, CancellationToken ct = default)
    {
        var continuations = _batchContinuations.Values
            .Where(c => c.BatchId == batchId)
            .OrderBy(c => c.CreatedAt)
            .ToList();

        return Task.FromResult<IReadOnlyList<BatchContinuation>>(continuations);
    }

    public Task UpdateBatchContinuationAsync(BatchContinuation continuation, CancellationToken ct = default)
    {
        _batchContinuations[continuation.Id] = continuation;
        return Task.CompletedTask;
    }

    // Rate Limiting

    public Task RecordRateLimitExecutionAsync(string key, DateTime executedAt, CancellationToken ct = default)
    {
        var executions = _rateLimitExecutions.GetOrAdd(key, _ => new List<DateTime>());
        lock (executions)
        {
            executions.Add(executedAt);
        }
        return Task.CompletedTask;
    }

    public Task<int> CountRateLimitExecutionsAsync(string key, DateTime windowStart, CancellationToken ct = default)
    {
        if (_rateLimitExecutions.TryGetValue(key, out var executions))
        {
            lock (executions)
            {
                return Task.FromResult(executions.Count(e => e >= windowStart));
            }
        }
        return Task.FromResult(0);
    }

    public Task<DateTime?> GetOldestRateLimitExecutionAsync(string key, DateTime windowStart, CancellationToken ct = default)
    {
        if (_rateLimitExecutions.TryGetValue(key, out var executions))
        {
            lock (executions)
            {
                var oldest = executions
                    .Where(e => e >= windowStart)
                    .OrderBy(e => e)
                    .FirstOrDefault();

                return Task.FromResult(oldest == default ? null : (DateTime?)oldest);
            }
        }
        return Task.FromResult<DateTime?>(null);
    }

    public Task<int> CleanupRateLimitExecutionsAsync(DateTime olderThan, CancellationToken ct = default)
    {
        var count = 0;
        foreach (var kvp in _rateLimitExecutions)
        {
            lock (kvp.Value)
            {
                var oldExecutions = kvp.Value.Where(e => e < olderThan).ToList();
                foreach (var execution in oldExecutions)
                {
                    kvp.Value.Remove(execution);
                    count++;
                }
            }
        }
        return Task.FromResult(count);
    }

    // Maintenance

    public Task<int> CleanupOldRunsAsync(TimeSpan retention, CancellationToken ct = default)
    {
        var cutoff = DateTime.UtcNow - retention;
        var oldRuns = _runs
            .Where(kvp => kvp.Value.CompletedAt.HasValue && kvp.Value.CompletedAt < cutoff)
            .Select(kvp => kvp.Key)
            .ToList();

        foreach (var key in oldRuns)
        {
            _runs.TryRemove(key, out _);
            _logs.TryRemove(key, out _);
        }

        return Task.FromResult(oldRuns.Count);
    }

    public Task<int> CleanupOldLogsAsync(TimeSpan retention, CancellationToken ct = default)
    {
        var cutoff = DateTime.UtcNow - retention;
        var count = 0;

        foreach (var kvp in _logs)
        {
            lock (kvp.Value)
            {
                var oldLogs = kvp.Value.Where(l => l.Timestamp < cutoff).ToList();
                foreach (var log in oldLogs)
                {
                    kvp.Value.Remove(log);
                    count++;
                }
            }
        }

        return Task.FromResult(count);
    }

    public Task<JobStorageStats> GetStatsAsync(CancellationToken ct = default)
    {
        var today = DateTime.UtcNow.Date;
        var allRuns = _runs.Values.ToList();
        var todayRuns = allRuns.Where(r => r.CreatedAt.Date == today).ToList();

        var stats = new JobStorageStats(
            TotalJobs: _definitions.Count,
            TotalRuns: allRuns.Count,
            PendingRuns: allRuns.Count(r => r.Status == JobRunStatus.Pending),
            RunningRuns: allRuns.Count(r => r.Status == JobRunStatus.Running),
            CompletedToday: todayRuns.Count(r => r.Status == JobRunStatus.Completed),
            FailedToday: todayRuns.Count(r => r.Status == JobRunStatus.Failed),
            ActiveWorkers: _heartbeats.Count(h => h.Value.Timestamp >= DateTime.UtcNow.AddMinutes(-2)),
            TotalLogEntries: _logs.Values.Sum(l => l.Count),
            DeadLetterCount: _deadLetterEntries.Count(e => e.Value.Status == DeadLetterStatus.Pending)
        );

        return Task.FromResult(stats);
    }

    /// <summary>
    /// Clear all data (useful for testing)
    /// </summary>
    public void Clear()
    {
        _definitions.Clear();
        _runs.Clear();
        _logs.Clear();
        _heartbeats.Clear();
        _continuations.Clear();
        _deadLetterEntries.Clear();
        _batches.Clear();
        _batchJobs.Clear();
        _batchContinuations.Clear();
        _rateLimitExecutions.Clear();
    }
}
