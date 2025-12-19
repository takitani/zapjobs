using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ZapJobs.Batches;
using ZapJobs.Core;
using ZapJobs.Core.Checkpoints;
using ZapJobs.Core.Events;
using ZapJobs.Tracking;

namespace ZapJobs.Execution;

/// <summary>
/// Executes jobs with timeout, cancellation, and error handling
/// </summary>
public class JobExecutor : IJobExecutor
{
    private readonly IServiceProvider _services;
    private readonly IJobStorage _storage;
    private readonly IJobLoggerFactory _loggerFactory;
    private readonly RetryHandler _retryHandler;
    private readonly ZapJobsOptions _options;
    private readonly ILogger<JobExecutor> _logger;
    private readonly IJobEventDispatcher? _eventDispatcher;
    private readonly ICheckpointStore? _checkpointStore;
    private readonly Dictionary<string, Type> _jobTypes = new();
    private BatchService? _batchService;

    public JobExecutor(
        IServiceProvider services,
        IJobStorage storage,
        IJobLoggerFactory loggerFactory,
        RetryHandler retryHandler,
        IOptions<ZapJobsOptions> options,
        ILogger<JobExecutor> logger,
        IJobEventDispatcher? eventDispatcher = null,
        ICheckpointStore? checkpointStore = null)
    {
        _services = services;
        _storage = storage;
        _loggerFactory = loggerFactory;
        _retryHandler = retryHandler;
        _options = options.Value;
        _logger = logger;
        _eventDispatcher = eventDispatcher;
        _checkpointStore = checkpointStore;
    }

    /// <summary>
    /// Set the batch service for batch completion tracking
    /// </summary>
    internal void SetBatchService(BatchService batchService)
    {
        _batchService = batchService;
    }

    /// <summary>
    /// Register a job type for execution
    /// </summary>
    public void RegisterJobType<TJob>() where TJob : IJob
    {
        var job = ActivatorUtilities.CreateInstance<TJob>(_services);
        _jobTypes[job.JobTypeId] = typeof(TJob);
    }

    /// <summary>
    /// Register a job type by type
    /// </summary>
    public void RegisterJobType(Type jobType)
    {
        if (!typeof(IJob).IsAssignableFrom(jobType))
            throw new ArgumentException($"Type {jobType.Name} does not implement IJob", nameof(jobType));

        var job = (IJob)ActivatorUtilities.CreateInstance(_services, jobType);
        _jobTypes[job.JobTypeId] = jobType;
    }

    /// <summary>
    /// Get all registered job type IDs
    /// </summary>
    public IReadOnlyCollection<string> GetRegisteredJobTypes() => _jobTypes.Keys.ToList().AsReadOnly();

    /// <summary>
    /// Execute a job run
    /// </summary>
    public async Task<JobRunResult> ExecuteAsync(JobRun run, CancellationToken ct = default)
    {
        var stopwatch = Stopwatch.StartNew();
        var result = new JobRunResult { RunId = run.Id };

        // Find job type
        if (!_jobTypes.TryGetValue(run.JobTypeId, out var jobType))
        {
            _logger.LogError("Unknown job type: {JobTypeId}", run.JobTypeId);
            result.Success = false;
            result.ErrorMessage = $"Unknown job type: {run.JobTypeId}";
            return result;
        }

        // Get job definition for timeout
        var definition = await _storage.GetJobDefinitionAsync(run.JobTypeId, ct);
        var timeout = definition != null
            ? TimeSpan.FromSeconds(definition.TimeoutSeconds)
            : _options.DefaultTimeout;

        // Mark as running
        run.Status = JobRunStatus.Running;
        run.StartedAt = DateTime.UtcNow;
        run.AttemptNumber++;
        await _storage.UpdateRunAsync(run, ct);

        // Create job logger
        var jobLogger = _loggerFactory.CreateLogger(run.Id);

        // Parse input JSON
        JsonDocument? inputDoc = null;
        if (!string.IsNullOrEmpty(run.InputJson))
        {
            try
            {
                inputDoc = JsonDocument.Parse(run.InputJson);
            }
            catch (JsonException ex)
            {
                _logger.LogWarning(ex, "Failed to parse input JSON for run {RunId}", run.Id);
            }
        }

        // Create a scope for scoped services (like DbContext)
        using var scope = _services.CreateScope();
        var scopedServices = scope.ServiceProvider;

        // Build resume context if this is a retry
        var resumeContext = await BuildResumeContextAsync(run, ct);

        // Create execution context with scoped provider
        var context = new JobExecutionContext(
            runId: run.Id,
            jobTypeId: run.JobTypeId,
            triggerType: run.TriggerType,
            triggeredBy: run.TriggeredBy,
            services: scopedServices,
            logger: jobLogger,
            inputDocument: inputDoc,
            attemptNumber: run.AttemptNumber,
            resumeContext: resumeContext,
            checkpointStore: _checkpointStore);

        try
        {
            // Create job instance using scoped provider
            var job = (IJob)ActivatorUtilities.CreateInstance(scopedServices, jobType);

            // Execute with timeout
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(timeout);

            await jobLogger.InfoAsync($"Starting job execution (attempt {run.AttemptNumber})");

            // Dispatch JobStarted event
            if (_eventDispatcher != null)
            {
                await _eventDispatcher.DispatchAsync(new JobStartedEvent
                {
                    RunId = run.Id,
                    JobTypeId = run.JobTypeId,
                    Timestamp = DateTimeOffset.UtcNow,
                    AttemptNumber = run.AttemptNumber,
                    InputJson = run.InputJson,
                    Queue = run.Queue
                });
            }

            await job.ExecuteAsync(context, timeoutCts.Token);

            stopwatch.Stop();

            // Get metrics from context
            var metrics = context.GetMetrics();

            // Success
            run.Status = JobRunStatus.Completed;
            run.CompletedAt = DateTime.UtcNow;
            run.DurationMs = (int)stopwatch.ElapsedMilliseconds;
            run.ItemsProcessed = metrics.ItemsProcessed;
            run.ItemsSucceeded = metrics.ItemsSucceeded;
            run.ItemsFailed = metrics.ItemsFailed;
            run.Progress = 100;

            var output = context.GetOutput();
            if (output != null)
            {
                run.OutputJson = JsonSerializer.Serialize(output);
            }

            await _storage.UpdateRunAsync(run, ct);
            await jobLogger.InfoAsync($"Job completed in {stopwatch.ElapsedMilliseconds}ms");

            result.Success = true;
            result.DurationMs = (int)stopwatch.ElapsedMilliseconds;
            result.OutputJson = run.OutputJson;

            _logger.LogInformation(
                "Job {JobTypeId} run {RunId} completed in {Duration}ms",
                run.JobTypeId, run.Id, stopwatch.ElapsedMilliseconds);

            // Dispatch JobCompleted event
            if (_eventDispatcher != null)
            {
                await _eventDispatcher.DispatchAsync(new JobCompletedEvent
                {
                    RunId = run.Id,
                    JobTypeId = run.JobTypeId,
                    Timestamp = DateTimeOffset.UtcNow,
                    Duration = stopwatch.Elapsed,
                    OutputJson = run.OutputJson,
                    ItemsSucceeded = metrics.ItemsSucceeded,
                    ItemsFailed = metrics.ItemsFailed,
                    ItemsProcessed = metrics.ItemsProcessed,
                    AttemptNumber = run.AttemptNumber,
                    Queue = run.Queue
                });
            }

            // Process continuations after successful completion
            await ProcessContinuationsAsync(run, ct);

            // Check batch completion
            await CheckBatchCompletionAsync(run, ct);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            // Timeout
            stopwatch.Stop();
            await HandleTimeoutAsync(run, jobLogger, stopwatch.ElapsedMilliseconds, result);
        }
        catch (Exception ex)
        {
            // Error
            stopwatch.Stop();
            await HandleErrorAsync(run, ex, jobLogger, stopwatch.ElapsedMilliseconds, result, definition, ct);
        }

        return result;
    }

    private async Task HandleTimeoutAsync(JobRun run, IJobLogger logger, long durationMs, JobRunResult result)
    {
        run.Status = JobRunStatus.Failed;
        run.CompletedAt = DateTime.UtcNow;
        run.DurationMs = (int)durationMs;
        run.ErrorMessage = "Job timed out";
        run.ErrorType = nameof(OperationCanceledException);

        await _storage.UpdateRunAsync(run, default);
        await logger.ErrorAsync("Job timed out");

        // Move to dead letter queue
        await _storage.MoveToDeadLetterAsync(run, default);

        result.Success = false;
        result.ErrorMessage = "Job timed out";
        result.DurationMs = (int)durationMs;

        _logger.LogWarning("Job {JobTypeId} run {RunId} timed out and moved to dead letter queue after {Duration}ms",
            run.JobTypeId, run.Id, durationMs);

        // Dispatch JobFailed event
        if (_eventDispatcher != null)
        {
            await _eventDispatcher.DispatchAsync(new JobFailedEvent
            {
                RunId = run.Id,
                JobTypeId = run.JobTypeId,
                Timestamp = DateTimeOffset.UtcNow,
                ErrorMessage = "Job timed out",
                ErrorType = nameof(OperationCanceledException),
                AttemptNumber = run.AttemptNumber,
                WillRetry = false,
                MovedToDeadLetter = true,
                Queue = run.Queue,
                Duration = TimeSpan.FromMilliseconds(durationMs)
            });
        }

        // Process continuations after timeout (treated as failure)
        await ProcessContinuationsAsync(run, default);

        // Check batch completion
        await CheckBatchCompletionAsync(run, default);
    }

    private async Task HandleErrorAsync(
        JobRun run,
        Exception ex,
        IJobLogger logger,
        long durationMs,
        JobRunResult result,
        JobDefinition? definition,
        CancellationToken ct)
    {
        await logger.ErrorAsync($"Job failed: {ex.Message}", exception: ex);

        // Build retry policy from definition or defaults
        var retryPolicy = new RetryPolicy
        {
            MaxRetries = definition?.MaxRetries ?? _options.DefaultRetryPolicy.MaxRetries,
            InitialDelay = _options.DefaultRetryPolicy.InitialDelay,
            MaxDelay = _options.DefaultRetryPolicy.MaxDelay,
            BackoffMultiplier = _options.DefaultRetryPolicy.BackoffMultiplier,
            UseJitter = _options.DefaultRetryPolicy.UseJitter
        };

        if (_retryHandler.ShouldRetry(ex, run.AttemptNumber, retryPolicy))
        {
            // Schedule retry
            var delay = retryPolicy.CalculateDelay(run.AttemptNumber);
            run.Status = JobRunStatus.AwaitingRetry;
            run.NextRetryAt = DateTime.UtcNow.Add(delay);
            run.ErrorMessage = ex.Message;
            run.DurationMs = (int)durationMs;

            await _storage.UpdateRunAsync(run, ct);
            await logger.WarningAsync($"Scheduling retry in {delay.TotalSeconds:F0}s (attempt {run.AttemptNumber + 1})");

            _logger.LogWarning(ex,
                "Job {JobTypeId} run {RunId} failed, scheduling retry {Attempt} in {Delay}s",
                run.JobTypeId, run.Id, run.AttemptNumber + 1, delay.TotalSeconds);

            result.Success = false;
            result.ErrorMessage = ex.Message;
            result.WillRetry = true;
            result.NextRetryAt = run.NextRetryAt;

            // Dispatch JobRetrying event
            if (_eventDispatcher != null)
            {
                await _eventDispatcher.DispatchAsync(new JobRetryingEvent
                {
                    RunId = run.Id,
                    JobTypeId = run.JobTypeId,
                    Timestamp = DateTimeOffset.UtcNow,
                    FailedAttempt = run.AttemptNumber,
                    NextAttempt = run.AttemptNumber + 1,
                    MaxRetries = retryPolicy.MaxRetries,
                    RetryAt = new DateTimeOffset(run.NextRetryAt.Value, TimeSpan.Zero),
                    Delay = delay,
                    ErrorMessage = ex.Message,
                    Queue = run.Queue
                });
            }
        }
        else
        {
            // Final failure
            run.Status = JobRunStatus.Failed;
            run.CompletedAt = DateTime.UtcNow;
            run.DurationMs = (int)durationMs;
            run.ErrorMessage = ex.Message;
            run.StackTrace = ex.StackTrace;
            run.ErrorType = ex.GetType().FullName;

            await _storage.UpdateRunAsync(run, ct);

            // Move to dead letter queue
            await _storage.MoveToDeadLetterAsync(run, ct);

            _logger.LogWarning(
                "Job {JobTypeId} run {RunId} moved to dead letter queue after {Attempts} attempts",
                run.JobTypeId, run.Id, run.AttemptNumber);

            _logger.LogError(ex,
                "Job {JobTypeId} run {RunId} failed permanently after {Attempts} attempts",
                run.JobTypeId, run.Id, run.AttemptNumber);

            result.Success = false;
            result.ErrorMessage = ex.Message;
            result.WillRetry = false;

            // Dispatch JobFailed event
            if (_eventDispatcher != null)
            {
                await _eventDispatcher.DispatchAsync(new JobFailedEvent
                {
                    RunId = run.Id,
                    JobTypeId = run.JobTypeId,
                    Timestamp = DateTimeOffset.UtcNow,
                    ErrorMessage = ex.Message,
                    ErrorType = ex.GetType().FullName,
                    StackTrace = ex.StackTrace,
                    AttemptNumber = run.AttemptNumber,
                    WillRetry = false,
                    MovedToDeadLetter = true,
                    Queue = run.Queue,
                    Duration = TimeSpan.FromMilliseconds(durationMs)
                });
            }

            // Process continuations after final failure
            await ProcessContinuationsAsync(run, ct);

            // Check batch completion
            await CheckBatchCompletionAsync(run, ct);
        }

        result.DurationMs = (int)durationMs;
    }

    private async Task ProcessContinuationsAsync(JobRun parentRun, CancellationToken ct)
    {
        var continuations = await _storage.GetContinuationsAsync(parentRun.Id, ct);
        if (continuations.Count == 0)
            return;

        foreach (var continuation in continuations.Where(c => c.Status == ContinuationStatus.Pending))
        {
            var shouldTrigger = continuation.Condition switch
            {
                ContinuationCondition.OnSuccess => parentRun.Status == JobRunStatus.Completed,
                ContinuationCondition.OnFailure => parentRun.Status == JobRunStatus.Failed,
                ContinuationCondition.Always => true,
                _ => false
            };

            if (shouldTrigger)
            {
                // Determine input for continuation
                var input = continuation.PassParentOutput
                    ? parentRun.OutputJson
                    : continuation.InputJson;

                var run = new JobRun
                {
                    JobTypeId = continuation.ContinuationJobTypeId,
                    Status = JobRunStatus.Pending,
                    TriggerType = JobTriggerType.Continuation,
                    TriggeredBy = $"continuation:{parentRun.Id}",
                    Queue = continuation.Queue ?? _options.DefaultQueue,
                    InputJson = input,
                    CreatedAt = DateTime.UtcNow
                };

                var runId = await _storage.EnqueueAsync(run, ct);
                continuation.ContinuationRunId = runId;
                continuation.Status = ContinuationStatus.Triggered;

                _logger.LogInformation(
                    "Triggered continuation {ContinuationId} -> job {JobTypeId} run {RunId}",
                    continuation.Id, continuation.ContinuationJobTypeId, runId);
            }
            else
            {
                continuation.Status = ContinuationStatus.Skipped;

                _logger.LogDebug(
                    "Skipped continuation {ContinuationId} (condition {Condition} not met, parent status {Status})",
                    continuation.Id, continuation.Condition, parentRun.Status);
            }

            await _storage.UpdateContinuationAsync(continuation, ct);
        }
    }

    private async Task CheckBatchCompletionAsync(JobRun run, CancellationToken ct)
    {
        if (_batchService == null || !run.BatchId.HasValue)
            return;

        try
        {
            await _batchService.CheckBatchCompletionAsync(run, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to check batch completion for run {RunId}", run.Id);
        }
    }

    private async Task<ResumeContext> BuildResumeContextAsync(JobRun run, CancellationToken ct)
    {
        // First attempt - no resumption
        if (run.AttemptNumber <= 1)
        {
            return new ResumeContext
            {
                IsResuming = false,
                Reason = ResumeReason.None
            };
        }

        // Check if we have checkpoints from previous attempts
        if (_checkpointStore == null)
        {
            return new ResumeContext
            {
                IsResuming = true,
                PreviousAttempt = run.AttemptNumber - 1,
                Reason = ResumeReason.RetryAfterFailure
            };
        }

        try
        {
            // Get the most recent checkpoint to provide context
            var checkpoints = await _checkpointStore.GetAllAsync(run.Id, ct);
            var latestCheckpoint = checkpoints.FirstOrDefault();

            if (latestCheckpoint != null)
            {
                return new ResumeContext
                {
                    IsResuming = true,
                    PreviousAttempt = run.AttemptNumber - 1,
                    CheckpointCreatedAt = latestCheckpoint.CreatedAt,
                    LastCheckpointKey = latestCheckpoint.Key,
                    Reason = ResumeReason.RetryAfterFailure
                };
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get checkpoints for resume context, run {RunId}", run.Id);
        }

        // No checkpoints found, but still resuming
        return new ResumeContext
        {
            IsResuming = true,
            PreviousAttempt = run.AttemptNumber - 1,
            Reason = ResumeReason.RetryAfterFailure
        };
    }
}

/// <summary>
/// Interface for job execution
/// </summary>
public interface IJobExecutor
{
    void RegisterJobType<TJob>() where TJob : IJob;
    void RegisterJobType(Type jobType);
    IReadOnlyCollection<string> GetRegisteredJobTypes();
    Task<JobRunResult> ExecuteAsync(JobRun run, CancellationToken ct = default);
}

/// <summary>
/// Result of a job execution
/// </summary>
public class JobRunResult
{
    public Guid RunId { get; set; }
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
    public string? OutputJson { get; set; }
    public int DurationMs { get; set; }
    public bool WillRetry { get; set; }
    public DateTime? NextRetryAt { get; set; }
}
