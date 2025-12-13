using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ZapJobs.Core;
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
    private readonly Dictionary<string, Type> _jobTypes = new();

    public JobExecutor(
        IServiceProvider services,
        IJobStorage storage,
        IJobLoggerFactory loggerFactory,
        RetryHandler retryHandler,
        IOptions<ZapJobsOptions> options,
        ILogger<JobExecutor> logger)
    {
        _services = services;
        _storage = storage;
        _loggerFactory = loggerFactory;
        _retryHandler = retryHandler;
        _options = options.Value;
        _logger = logger;
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

        // Create execution context with scoped provider
        var context = new JobExecutionContext(
            runId: run.Id,
            jobTypeId: run.JobTypeId,
            triggerType: run.TriggerType,
            triggeredBy: run.TriggeredBy,
            services: scopedServices,
            logger: jobLogger,
            inputDocument: inputDoc);

        try
        {
            // Create job instance using scoped provider
            var job = (IJob)ActivatorUtilities.CreateInstance(scopedServices, jobType);

            // Execute with timeout
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(timeout);

            await jobLogger.InfoAsync($"Starting job execution (attempt {run.AttemptNumber})");
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

        await _storage.UpdateRunAsync(run, default);
        await logger.ErrorAsync("Job timed out");

        result.Success = false;
        result.ErrorMessage = "Job timed out";
        result.DurationMs = (int)durationMs;

        _logger.LogWarning("Job {JobTypeId} run {RunId} timed out after {Duration}ms",
            run.JobTypeId, run.Id, durationMs);
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

            _logger.LogError(ex,
                "Job {JobTypeId} run {RunId} failed permanently after {Attempts} attempts",
                run.JobTypeId, run.Id, run.AttemptNumber);

            result.Success = false;
            result.ErrorMessage = ex.Message;
            result.WillRetry = false;
        }

        result.DurationMs = (int)durationMs;
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
