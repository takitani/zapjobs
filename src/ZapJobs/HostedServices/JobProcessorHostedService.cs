using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ZapJobs.Core;
using ZapJobs.Execution;
using ZapJobs.RateLimiting;
using ZapJobs.Scheduling;
using ZapJobs.Tracking;

namespace ZapJobs.HostedServices;

/// <summary>
/// Background service that processes jobs from the queue
/// </summary>
public class JobProcessorHostedService : BackgroundService
{
    private readonly IJobStorage _storage;
    private readonly IJobExecutor _executor;
    private readonly ICronScheduler _cronScheduler;
    private readonly HeartbeatService _heartbeatService;
    private readonly IRateLimiter _rateLimiter;
    private readonly ZapJobsOptions _options;
    private readonly ILogger<JobProcessorHostedService> _logger;
    private readonly SemaphoreSlim _workerSemaphore;

    public JobProcessorHostedService(
        IJobStorage storage,
        IJobExecutor executor,
        ICronScheduler cronScheduler,
        HeartbeatService heartbeatService,
        IRateLimiter rateLimiter,
        IOptions<ZapJobsOptions> options,
        ILogger<JobProcessorHostedService> logger)
    {
        _storage = storage;
        _executor = executor;
        _cronScheduler = cronScheduler;
        _heartbeatService = heartbeatService;
        _rateLimiter = rateLimiter;
        _options = options.Value;
        _logger = logger;
        _workerSemaphore = new SemaphoreSlim(_options.WorkerCount, _options.WorkerCount);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "Job processor started with {WorkerCount} workers on queues [{Queues}]",
            _options.WorkerCount,
            string.Join(", ", _options.Queues));

        var tasks = new List<Task>();

        // Start scheduler task if enabled
        if (_options.EnableScheduler)
        {
            tasks.Add(RunSchedulerAsync(stoppingToken));
        }

        // Start processor task if enabled
        if (_options.EnableProcessing)
        {
            tasks.Add(RunProcessorAsync(stoppingToken));
        }

        await Task.WhenAll(tasks);

        _logger.LogInformation("Job processor stopped");
    }

    private async Task RunSchedulerAsync(CancellationToken ct)
    {
        _logger.LogInformation("Scheduler started");

        while (!ct.IsCancellationRequested)
        {
            try
            {
                await ProcessDueJobsAsync(ct);
                await ProcessRetryJobsAsync(ct);
                await Task.Delay(_options.PollingInterval, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in scheduler loop");
                await Task.Delay(TimeSpan.FromSeconds(5), ct);
            }
        }
    }

    private async Task ProcessDueJobsAsync(CancellationToken ct)
    {
        // Get recurring jobs that are due
        var dueJobs = await _storage.GetDueJobsAsync(DateTime.UtcNow, ct);

        foreach (var definition in dueJobs)
        {
            if (ct.IsCancellationRequested)
                break;

            try
            {
                // Check for prevent overlapping
                if (definition.PreventOverlapping)
                {
                    var hasActiveRun = await _storage.HasActiveRunAsync(definition.JobTypeId, ct);
                    if (hasActiveRun)
                    {
                        _logger.LogDebug(
                            "Skipping job {JobTypeId} due to prevent overlapping - instance already running or pending",
                            definition.JobTypeId);

                        // Still calculate and update next run time
                        await UpdateNextRunTimeAsync(definition, ct);
                        continue;
                    }
                }

                // Create a run for this recurring job
                var run = new JobRun
                {
                    JobTypeId = definition.JobTypeId,
                    Status = JobRunStatus.Pending,
                    TriggerType = definition.ScheduleType == ScheduleType.Cron
                        ? JobTriggerType.Cron
                        : JobTriggerType.Scheduled,
                    Queue = definition.Queue,
                    InputJson = definition.ConfigJson,
                    CreatedAt = DateTime.UtcNow
                };

                await _storage.EnqueueAsync(run, ct);

                // Update next run time
                var nextRun = await UpdateNextRunTimeAsync(definition, ct);

                _logger.LogInformation(
                    "Enqueued recurring job {JobTypeId}, next run at {NextRun}",
                    definition.JobTypeId,
                    nextRun);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error enqueueing recurring job {JobTypeId}", definition.JobTypeId);
            }
        }
    }

    private async Task ProcessRetryJobsAsync(CancellationToken ct)
    {
        // Get jobs awaiting retry that are due
        var retryRuns = await _storage.GetRunsForRetryAsync(ct);

        foreach (var run in retryRuns.Where(r => r.NextRetryAt <= DateTime.UtcNow))
        {
            if (ct.IsCancellationRequested)
                break;

            // Mark as pending for processing
            run.Status = JobRunStatus.Pending;
            run.TriggerType = JobTriggerType.Retry;
            await _storage.UpdateRunAsync(run, ct);

            _logger.LogDebug("Retry job {RunId} is now pending", run.Id);
        }
    }

    private async Task<DateTime?> UpdateNextRunTimeAsync(JobDefinition definition, CancellationToken ct)
    {
        DateTime? nextRun = null;

        if (definition.ScheduleType == ScheduleType.Cron && !string.IsNullOrEmpty(definition.CronExpression))
        {
            var tz = !string.IsNullOrEmpty(definition.TimeZoneId)
                ? TimeZoneInfo.FindSystemTimeZoneById(definition.TimeZoneId)
                : TimeZoneInfo.Utc;

            nextRun = _cronScheduler.GetNextOccurrence(
                definition.CronExpression,
                DateTime.UtcNow,
                tz);
        }
        else if (definition.ScheduleType == ScheduleType.Interval && definition.IntervalMinutes.HasValue)
        {
            nextRun = DateTime.UtcNow.AddMinutes(definition.IntervalMinutes.Value);
        }

        await _storage.UpdateNextRunAsync(
            definition.JobTypeId,
            nextRun,
            DateTime.UtcNow,
            null,
            ct);

        return nextRun;
    }

    private async Task RunProcessorAsync(CancellationToken ct)
    {
        _logger.LogInformation("Processor started");

        while (!ct.IsCancellationRequested)
        {
            try
            {
                // Get pending jobs
                var pendingRuns = await _storage.GetPendingRunsAsync(_options.Queues, _options.WorkerCount, ct);

                if (pendingRuns.Count == 0)
                {
                    // Also check for scheduled jobs that are due
                    var scheduledRuns = await _storage.GetRunsByStatusAsync(JobRunStatus.Scheduled, _options.WorkerCount, 0, ct);
                    pendingRuns = scheduledRuns
                        .Where(r => r.ScheduledAt <= DateTime.UtcNow)
                        .ToList();

                    // Mark them as pending
                    foreach (var run in pendingRuns)
                    {
                        run.Status = JobRunStatus.Pending;
                        await _storage.UpdateRunAsync(run, ct);
                    }
                }

                if (pendingRuns.Count == 0)
                {
                    await Task.Delay(_options.PollingInterval, ct);
                    continue;
                }

                // Process jobs concurrently up to worker count
                var processTasks = pendingRuns.Select(run => ProcessJobAsync(run, ct));
                await Task.WhenAll(processTasks);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in processor loop");
                await Task.Delay(TimeSpan.FromSeconds(5), ct);
            }
        }
    }

    private async Task ProcessJobAsync(JobRun run, CancellationToken ct)
    {
        await _workerSemaphore.WaitAsync(ct);

        try
        {
            // Try to acquire the run
            var acquired = await _storage.TryAcquireRunAsync(run.Id, _heartbeatService.WorkerId, ct);
            if (!acquired)
            {
                _logger.LogDebug("Could not acquire run {RunId}, another worker may have taken it", run.Id);
                return;
            }

            // Check rate limits before execution
            var rateLimitResult = await TryAcquireRateLimitAsync(run, ct);
            if (!rateLimitResult.Allowed)
            {
                // Rate limit was handled (delayed, rejected, or skipped)
                return;
            }

            _logger.LogDebug("Processing job {JobTypeId} run {RunId}", run.JobTypeId, run.Id);

            var result = await _executor.ExecuteAsync(run, ct);

            if (result.Success)
            {
                _heartbeatService.IncrementProcessed();
            }
            else if (!result.WillRetry)
            {
                _heartbeatService.IncrementFailed();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error processing job {RunId}", run.Id);
            _heartbeatService.IncrementFailed();
        }
        finally
        {
            _workerSemaphore.Release();
        }
    }

    private async Task<RateLimitResult> TryAcquireRateLimitAsync(JobRun run, CancellationToken ct)
    {
        // Get job definition to check for rate limit policy
        var definition = await _storage.GetJobDefinitionAsync(run.JobTypeId, ct);

        // Check global rate limit first
        if (_options.GlobalRateLimit != null)
        {
            var globalResult = await _rateLimiter.AcquireAsync(
                SlidingWindowRateLimiter.GlobalKey,
                _options.GlobalRateLimit,
                ct);

            if (!globalResult.Allowed)
            {
                await HandleRateLimitExceededAsync(run, _options.GlobalRateLimit, globalResult, "global", ct);
                return globalResult;
            }
        }

        // Check queue rate limit
        if (_options.QueueRateLimits.TryGetValue(run.Queue, out var queuePolicy))
        {
            var queueResult = await _rateLimiter.AcquireAsync(
                SlidingWindowRateLimiter.GetQueueKey(run.Queue),
                queuePolicy,
                ct);

            if (!queueResult.Allowed)
            {
                await HandleRateLimitExceededAsync(run, queuePolicy, queueResult, $"queue:{run.Queue}", ct);
                return queueResult;
            }
        }

        // Check job type rate limit
        if (definition?.RateLimit != null)
        {
            var jobResult = await _rateLimiter.AcquireAsync(
                SlidingWindowRateLimiter.GetJobTypeKey(run.JobTypeId),
                definition.RateLimit,
                ct);

            if (!jobResult.Allowed)
            {
                await HandleRateLimitExceededAsync(run, definition.RateLimit, jobResult, $"job:{run.JobTypeId}", ct);
                return jobResult;
            }
        }

        return RateLimitResult.Allow();
    }

    private async Task HandleRateLimitExceededAsync(
        JobRun run,
        RateLimitPolicy policy,
        RateLimitResult result,
        string limitKey,
        CancellationToken ct)
    {
        switch (policy.Behavior)
        {
            case RateLimitBehavior.Delay:
                // Reschedule for later
                var delay = result.RetryAfter ?? TimeSpan.FromSeconds(30);
                if (delay > policy.MaxDelay)
                    delay = policy.MaxDelay;

                run.Status = JobRunStatus.Scheduled;
                run.ScheduledAt = DateTime.UtcNow.Add(delay);
                await _storage.UpdateRunAsync(run, ct);

                _logger.LogDebug(
                    "Rate limited {LimitKey}, job {JobTypeId} run {RunId} delayed by {Delay}",
                    limitKey, run.JobTypeId, run.Id, delay);
                break;

            case RateLimitBehavior.Reject:
                // Mark as failed
                run.Status = JobRunStatus.Failed;
                run.ErrorMessage = $"Rate limit exceeded for {limitKey}";
                run.CompletedAt = DateTime.UtcNow;
                await _storage.UpdateRunAsync(run, ct);
                _heartbeatService.IncrementFailed();

                _logger.LogWarning(
                    "Rate limited {LimitKey}, job {JobTypeId} run {RunId} rejected",
                    limitKey, run.JobTypeId, run.Id);
                break;

            case RateLimitBehavior.Skip:
                // Mark as cancelled (skipped)
                run.Status = JobRunStatus.Cancelled;
                run.CompletedAt = DateTime.UtcNow;
                await _storage.UpdateRunAsync(run, ct);

                _logger.LogDebug(
                    "Rate limited {LimitKey}, job {JobTypeId} run {RunId} skipped",
                    limitKey, run.JobTypeId, run.Id);
                break;
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Job processor stopping...");

        // Wait for current jobs to complete (with timeout)
        var timeout = TimeSpan.FromSeconds(30);
        var waitTask = Task.Run(async () =>
        {
            while (_workerSemaphore.CurrentCount < _options.WorkerCount)
            {
                await Task.Delay(100);
            }
        });

        if (await Task.WhenAny(waitTask, Task.Delay(timeout)) != waitTask)
        {
            _logger.LogWarning("Timeout waiting for jobs to complete");
        }

        await base.StopAsync(cancellationToken);
    }
}
