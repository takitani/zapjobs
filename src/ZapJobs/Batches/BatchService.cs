using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ZapJobs.Core;

namespace ZapJobs.Batches;

/// <summary>
/// Service for creating and managing job batches
/// </summary>
public class BatchService : IBatchService
{
    private readonly IJobStorage _storage;
    private readonly ZapJobsOptions _options;
    private readonly ILogger<BatchService> _logger;
    private readonly JsonSerializerOptions _jsonOptions;

    public BatchService(
        IJobStorage storage,
        IOptions<ZapJobsOptions> options,
        ILogger<BatchService> logger)
    {
        _storage = storage;
        _options = options.Value;
        _logger = logger;
        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };
    }

    /// <inheritdoc />
    public async Task<Guid> CreateBatchAsync(
        string name,
        Action<IBatchBuilder> configure,
        string? createdBy = null,
        CancellationToken ct = default)
    {
        var builder = new BatchBuilder();
        configure(builder);

        if (builder.Jobs.Count == 0 && builder.NestedBatches.Count == 0)
        {
            throw new ArgumentException("Batch must contain at least one job or nested batch", nameof(configure));
        }

        var batch = new JobBatch
        {
            Name = name,
            TotalJobs = builder.Jobs.Count,
            CreatedBy = createdBy,
            CreatedAt = DateTime.UtcNow
        };

        // Create the batch
        await _storage.CreateBatchAsync(batch, ct);

        // Create all jobs
        var order = 0;
        foreach (var jobInfo in builder.Jobs)
        {
            var run = new JobRun
            {
                JobTypeId = jobInfo.JobTypeId,
                Status = JobRunStatus.Pending,
                TriggerType = JobTriggerType.Batch,
                Queue = jobInfo.Queue ?? _options.DefaultQueue,
                InputJson = jobInfo.Input != null
                    ? JsonSerializer.Serialize(jobInfo.Input, _jsonOptions)
                    : null,
                BatchId = batch.Id,
                CreatedAt = DateTime.UtcNow
            };

            var runId = await _storage.EnqueueAsync(run, ct);
            await _storage.AddBatchJobAsync(new BatchJob
            {
                BatchId = batch.Id,
                RunId = runId,
                Order = order++
            }, ct);
        }

        // Create nested batches recursively
        foreach (var (nestedName, nestedConfigure) in builder.NestedBatches)
        {
            await CreateNestedBatchAsync(batch.Id, nestedName, nestedConfigure, createdBy, ct);
        }

        // Store continuations
        if (builder.OnSuccessContinuation != null)
        {
            await _storage.AddBatchContinuationAsync(new BatchContinuation
            {
                BatchId = batch.Id,
                TriggerType = "success",
                JobTypeId = builder.OnSuccessContinuation.JobTypeId,
                InputJson = builder.OnSuccessContinuation.Input != null
                    ? JsonSerializer.Serialize(builder.OnSuccessContinuation.Input, _jsonOptions)
                    : null
            }, ct);
        }

        if (builder.OnFailureContinuation != null)
        {
            await _storage.AddBatchContinuationAsync(new BatchContinuation
            {
                BatchId = batch.Id,
                TriggerType = "failure",
                JobTypeId = builder.OnFailureContinuation.JobTypeId,
                InputJson = builder.OnFailureContinuation.Input != null
                    ? JsonSerializer.Serialize(builder.OnFailureContinuation.Input, _jsonOptions)
                    : null
            }, ct);
        }

        if (builder.OnCompleteContinuation != null)
        {
            await _storage.AddBatchContinuationAsync(new BatchContinuation
            {
                BatchId = batch.Id,
                TriggerType = "complete",
                JobTypeId = builder.OnCompleteContinuation.JobTypeId,
                InputJson = builder.OnCompleteContinuation.Input != null
                    ? JsonSerializer.Serialize(builder.OnCompleteContinuation.Input, _jsonOptions)
                    : null
            }, ct);
        }

        _logger.LogInformation(
            "Created batch {BatchId} '{BatchName}' with {JobCount} jobs",
            batch.Id, batch.Name, batch.TotalJobs);

        return batch.Id;
    }

    private async Task CreateNestedBatchAsync(
        Guid parentBatchId,
        string name,
        Action<IBatchBuilder> configure,
        string? createdBy,
        CancellationToken ct)
    {
        var builder = new BatchBuilder();
        configure(builder);

        var batch = new JobBatch
        {
            Name = name,
            ParentBatchId = parentBatchId,
            TotalJobs = builder.Jobs.Count,
            CreatedBy = createdBy,
            CreatedAt = DateTime.UtcNow
        };

        await _storage.CreateBatchAsync(batch, ct);

        // Create all jobs
        var order = 0;
        foreach (var jobInfo in builder.Jobs)
        {
            var run = new JobRun
            {
                JobTypeId = jobInfo.JobTypeId,
                Status = JobRunStatus.Pending,
                TriggerType = JobTriggerType.Batch,
                Queue = jobInfo.Queue ?? _options.DefaultQueue,
                InputJson = jobInfo.Input != null
                    ? JsonSerializer.Serialize(jobInfo.Input, _jsonOptions)
                    : null,
                BatchId = batch.Id,
                CreatedAt = DateTime.UtcNow
            };

            var runId = await _storage.EnqueueAsync(run, ct);
            await _storage.AddBatchJobAsync(new BatchJob
            {
                BatchId = batch.Id,
                RunId = runId,
                Order = order++
            }, ct);
        }

        // Update parent batch total to include nested batch jobs
        var parentBatch = await _storage.GetBatchAsync(parentBatchId, ct);
        if (parentBatch != null)
        {
            parentBatch.TotalJobs += builder.Jobs.Count;
            await _storage.UpdateBatchAsync(parentBatch, ct);
        }

        // Handle continuations for nested batch
        if (builder.OnSuccessContinuation != null)
        {
            await _storage.AddBatchContinuationAsync(new BatchContinuation
            {
                BatchId = batch.Id,
                TriggerType = "success",
                JobTypeId = builder.OnSuccessContinuation.JobTypeId,
                InputJson = builder.OnSuccessContinuation.Input != null
                    ? JsonSerializer.Serialize(builder.OnSuccessContinuation.Input, _jsonOptions)
                    : null
            }, ct);
        }

        if (builder.OnFailureContinuation != null)
        {
            await _storage.AddBatchContinuationAsync(new BatchContinuation
            {
                BatchId = batch.Id,
                TriggerType = "failure",
                JobTypeId = builder.OnFailureContinuation.JobTypeId,
                InputJson = builder.OnFailureContinuation.Input != null
                    ? JsonSerializer.Serialize(builder.OnFailureContinuation.Input, _jsonOptions)
                    : null
            }, ct);
        }

        if (builder.OnCompleteContinuation != null)
        {
            await _storage.AddBatchContinuationAsync(new BatchContinuation
            {
                BatchId = batch.Id,
                TriggerType = "complete",
                JobTypeId = builder.OnCompleteContinuation.JobTypeId,
                InputJson = builder.OnCompleteContinuation.Input != null
                    ? JsonSerializer.Serialize(builder.OnCompleteContinuation.Input, _jsonOptions)
                    : null
            }, ct);
        }

        // Recursively create nested batches
        foreach (var (nestedName, nestedConfigure) in builder.NestedBatches)
        {
            await CreateNestedBatchAsync(batch.Id, nestedName, nestedConfigure, createdBy, ct);
        }

        _logger.LogInformation(
            "Created nested batch {BatchId} '{BatchName}' under parent {ParentBatchId} with {JobCount} jobs",
            batch.Id, batch.Name, parentBatchId, batch.TotalJobs);
    }

    /// <inheritdoc />
    public Task<JobBatch?> GetBatchAsync(Guid batchId, CancellationToken ct = default)
    {
        return _storage.GetBatchAsync(batchId, ct);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<JobRun>> GetBatchJobsAsync(Guid batchId, CancellationToken ct = default)
    {
        return _storage.GetBatchJobsAsync(batchId, ct);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<JobBatch>> GetNestedBatchesAsync(Guid parentBatchId, CancellationToken ct = default)
    {
        return _storage.GetNestedBatchesAsync(parentBatchId, ct);
    }

    /// <inheritdoc />
    public async Task AddJobsToBatchAsync(
        Guid batchId,
        Action<IBatchBuilder> configure,
        CancellationToken ct = default)
    {
        var batch = await _storage.GetBatchAsync(batchId, ct);
        if (batch == null)
        {
            throw new InvalidOperationException($"Batch {batchId} not found");
        }

        if (batch.Status == BatchStatus.Completed || batch.Status == BatchStatus.Failed || batch.Status == BatchStatus.Cancelled)
        {
            throw new InvalidOperationException($"Cannot add jobs to batch {batchId} with status {batch.Status}");
        }

        var builder = new BatchBuilder();
        configure(builder);

        if (builder.Jobs.Count == 0)
        {
            return;
        }

        // Get current max order
        var existingJobs = await _storage.GetBatchJobsAsync(batchId, ct);
        var order = existingJobs.Count;

        // Create new jobs
        foreach (var jobInfo in builder.Jobs)
        {
            var run = new JobRun
            {
                JobTypeId = jobInfo.JobTypeId,
                Status = JobRunStatus.Pending,
                TriggerType = JobTriggerType.Batch,
                Queue = jobInfo.Queue ?? _options.DefaultQueue,
                InputJson = jobInfo.Input != null
                    ? JsonSerializer.Serialize(jobInfo.Input, _jsonOptions)
                    : null,
                BatchId = batch.Id,
                CreatedAt = DateTime.UtcNow
            };

            var runId = await _storage.EnqueueAsync(run, ct);
            await _storage.AddBatchJobAsync(new BatchJob
            {
                BatchId = batch.Id,
                RunId = runId,
                Order = order++
            }, ct);
        }

        // Update batch total
        batch.TotalJobs += builder.Jobs.Count;
        await _storage.UpdateBatchAsync(batch, ct);

        _logger.LogInformation(
            "Added {JobCount} jobs to batch {BatchId}",
            builder.Jobs.Count, batchId);
    }

    /// <inheritdoc />
    public async Task CancelBatchAsync(Guid batchId, CancellationToken ct = default)
    {
        var batch = await _storage.GetBatchAsync(batchId, ct);
        if (batch == null)
        {
            throw new InvalidOperationException($"Batch {batchId} not found");
        }

        // Cancel all pending jobs
        var jobs = await _storage.GetBatchJobsAsync(batchId, ct);
        foreach (var job in jobs.Where(j => j.Status == JobRunStatus.Pending))
        {
            job.Status = JobRunStatus.Cancelled;
            job.CompletedAt = DateTime.UtcNow;
            await _storage.UpdateRunAsync(job, ct);
        }

        // Update batch status
        batch.Status = BatchStatus.Cancelled;
        batch.CompletedAt = DateTime.UtcNow;
        await _storage.UpdateBatchAsync(batch, ct);

        // Cancel nested batches
        var nestedBatches = await _storage.GetNestedBatchesAsync(batchId, ct);
        foreach (var nested in nestedBatches.Where(b => b.Status != BatchStatus.Completed && b.Status != BatchStatus.Cancelled))
        {
            await CancelBatchAsync(nested.Id, ct);
        }

        _logger.LogInformation("Cancelled batch {BatchId}", batchId);
    }

    /// <inheritdoc />
    public async Task<Guid> ContinueBatchWithAsync(
        Guid batchId,
        string name,
        Action<IBatchBuilder> configure,
        CancellationToken ct = default)
    {
        var parentBatch = await _storage.GetBatchAsync(batchId, ct);
        if (parentBatch == null)
        {
            throw new InvalidOperationException($"Batch {batchId} not found");
        }

        // Create continuation batch
        var builder = new BatchBuilder();
        configure(builder);

        var batch = new JobBatch
        {
            Name = name,
            ParentBatchId = batchId, // Link to parent
            TotalJobs = builder.Jobs.Count,
            CreatedBy = parentBatch.CreatedBy,
            CreatedAt = DateTime.UtcNow
        };

        // If parent is complete, start immediately
        // Otherwise, create as pending and will be triggered later
        if (parentBatch.Status == BatchStatus.Completed || parentBatch.Status == BatchStatus.Failed)
        {
            return await CreateBatchAsync(name, configure, parentBatch.CreatedBy, ct);
        }

        // For pending continuation, store as batch continuation
        await _storage.AddBatchContinuationAsync(new BatchContinuation
        {
            BatchId = batchId,
            TriggerType = "complete",
            JobTypeId = "batch:" + name, // Special marker for batch continuation
            InputJson = null // Configuration will need to be recreated
        }, ct);

        return Guid.Empty; // Return empty guid since batch not yet created
    }

    /// <summary>
    /// Check and update batch completion status when a job completes
    /// Called from JobExecutor after job execution
    /// </summary>
    public async Task CheckBatchCompletionAsync(JobRun run, CancellationToken ct = default)
    {
        if (!run.BatchId.HasValue)
            return;

        var batch = await _storage.GetBatchAsync(run.BatchId.Value, ct);
        if (batch == null)
            return;

        // Update batch status if first job started
        if (batch.Status == BatchStatus.Created)
        {
            batch.Status = BatchStatus.Started;
            batch.StartedAt = DateTime.UtcNow;
        }

        // Update counters based on job status
        if (run.Status == JobRunStatus.Completed)
        {
            batch.CompletedJobs++;
        }
        else if (run.Status == JobRunStatus.Failed)
        {
            batch.FailedJobs++;
        }

        // Check if all jobs are done
        var totalDone = batch.CompletedJobs + batch.FailedJobs;
        if (totalDone >= batch.TotalJobs)
        {
            batch.Status = batch.FailedJobs > 0 ? BatchStatus.Failed : BatchStatus.Completed;
            batch.CompletedAt = DateTime.UtcNow;
            batch.ExpiresAt = DateTime.UtcNow.AddDays(7);

            _logger.LogInformation(
                "Batch {BatchId} '{BatchName}' completed with status {Status} ({CompletedJobs} completed, {FailedJobs} failed)",
                batch.Id, batch.Name, batch.Status, batch.CompletedJobs, batch.FailedJobs);

            // Execute continuations
            await ExecuteBatchContinuationsAsync(batch, ct);
        }

        await _storage.UpdateBatchAsync(batch, ct);

        // Also update parent batch if this is a nested batch
        if (batch.ParentBatchId.HasValue && batch.Status == BatchStatus.Completed || batch.Status == BatchStatus.Failed)
        {
            var parentBatch = await _storage.GetBatchAsync(batch.ParentBatchId.Value, ct);
            if (parentBatch != null)
            {
                // Propagate completion to parent
                if (batch.Status == BatchStatus.Completed)
                {
                    parentBatch.CompletedJobs += batch.CompletedJobs;
                }
                else if (batch.Status == BatchStatus.Failed)
                {
                    parentBatch.FailedJobs += batch.FailedJobs;
                    parentBatch.CompletedJobs += batch.CompletedJobs;
                }

                var parentTotalDone = parentBatch.CompletedJobs + parentBatch.FailedJobs;
                if (parentTotalDone >= parentBatch.TotalJobs)
                {
                    parentBatch.Status = parentBatch.FailedJobs > 0 ? BatchStatus.Failed : BatchStatus.Completed;
                    parentBatch.CompletedAt = DateTime.UtcNow;
                    parentBatch.ExpiresAt = DateTime.UtcNow.AddDays(7);

                    await ExecuteBatchContinuationsAsync(parentBatch, ct);
                }

                await _storage.UpdateBatchAsync(parentBatch, ct);
            }
        }
    }

    private async Task ExecuteBatchContinuationsAsync(JobBatch batch, CancellationToken ct)
    {
        var continuations = await _storage.GetBatchContinuationsAsync(batch.Id, ct);

        foreach (var continuation in continuations.Where(c => c.Status == ContinuationStatus.Pending))
        {
            var shouldTrigger = continuation.TriggerType switch
            {
                "success" => batch.Status == BatchStatus.Completed,
                "failure" => batch.Status == BatchStatus.Failed,
                "complete" => true,
                _ => false
            };

            if (shouldTrigger)
            {
                // Create continuation job
                var run = new JobRun
                {
                    JobTypeId = continuation.JobTypeId,
                    Status = JobRunStatus.Pending,
                    TriggerType = JobTriggerType.Continuation,
                    TriggeredBy = $"batch:{batch.Id}",
                    Queue = _options.DefaultQueue,
                    InputJson = continuation.InputJson,
                    CreatedAt = DateTime.UtcNow
                };

                var runId = await _storage.EnqueueAsync(run, ct);
                continuation.ContinuationRunId = runId;
                continuation.Status = ContinuationStatus.Triggered;

                _logger.LogInformation(
                    "Triggered batch continuation {ContinuationId} -> job {JobTypeId} run {RunId}",
                    continuation.Id, continuation.JobTypeId, runId);
            }
            else
            {
                continuation.Status = ContinuationStatus.Skipped;
            }

            await _storage.UpdateBatchContinuationAsync(continuation, ct);
        }
    }
}
