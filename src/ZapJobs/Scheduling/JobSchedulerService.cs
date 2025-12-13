using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ZapJobs.Core;

namespace ZapJobs.Scheduling;

/// <summary>
/// Implementation of IJobScheduler
/// </summary>
public class JobSchedulerService : IJobScheduler
{
    private readonly IJobStorage _storage;
    private readonly ICronScheduler _cronScheduler;
    private readonly ZapJobsOptions _options;
    private readonly ILogger<JobSchedulerService> _logger;
    private readonly Dictionary<string, Type> _jobTypes = new();

    public JobSchedulerService(
        IJobStorage storage,
        ICronScheduler cronScheduler,
        IOptions<ZapJobsOptions> options,
        ILogger<JobSchedulerService> logger)
    {
        _storage = storage;
        _cronScheduler = cronScheduler;
        _options = options.Value;
        _logger = logger;
    }

    /// <summary>
    /// Register a job type for EnqueueAsync&lt;TJob&gt;
    /// </summary>
    public void RegisterJobType<TJob>() where TJob : IJob
    {
        var job = Activator.CreateInstance<TJob>();
        _jobTypes[job.JobTypeId] = typeof(TJob);
    }

    public async Task<Guid> EnqueueAsync(string jobTypeId, object? input = null, string? queue = null, CancellationToken ct = default)
    {
        var run = new JobRun
        {
            JobTypeId = jobTypeId,
            Status = JobRunStatus.Pending,
            TriggerType = JobTriggerType.Api,
            Queue = queue ?? _options.DefaultQueue,
            InputJson = input != null ? JsonSerializer.Serialize(input) : null,
            CreatedAt = DateTime.UtcNow
        };

        var runId = await _storage.EnqueueAsync(run, ct);
        _logger.LogInformation("Enqueued job {JobTypeId} with run ID {RunId}", jobTypeId, runId);
        return runId;
    }

    public Task<Guid> EnqueueAsync<TJob>(object? input = null, string? queue = null, CancellationToken ct = default) where TJob : IJob
    {
        var job = Activator.CreateInstance<TJob>();
        return EnqueueAsync(job.JobTypeId, input, queue, ct);
    }

    public async Task<Guid> ScheduleAsync(string jobTypeId, TimeSpan delay, object? input = null, string? queue = null, CancellationToken ct = default)
    {
        return await ScheduleAsync(jobTypeId, DateTimeOffset.UtcNow.Add(delay), input, queue, ct);
    }

    public async Task<Guid> ScheduleAsync(string jobTypeId, DateTimeOffset runAt, object? input = null, string? queue = null, CancellationToken ct = default)
    {
        var run = new JobRun
        {
            JobTypeId = jobTypeId,
            Status = JobRunStatus.Scheduled,
            TriggerType = JobTriggerType.Scheduled,
            Queue = queue ?? _options.DefaultQueue,
            ScheduledAt = runAt.UtcDateTime,
            InputJson = input != null ? JsonSerializer.Serialize(input) : null,
            CreatedAt = DateTime.UtcNow
        };

        var runId = await _storage.EnqueueAsync(run, ct);
        _logger.LogInformation("Scheduled job {JobTypeId} with run ID {RunId} for {ScheduledAt}", jobTypeId, runId, runAt);
        return runId;
    }

    public async Task<string> RecurringAsync(string jobTypeId, TimeSpan interval, object? input = null, CancellationToken ct = default)
    {
        var definition = await _storage.GetJobDefinitionAsync(jobTypeId, ct);
        if (definition == null)
        {
            definition = new JobDefinition
            {
                JobTypeId = jobTypeId,
                DisplayName = jobTypeId,
                ScheduleType = ScheduleType.Interval,
                IntervalMinutes = (int)interval.TotalMinutes,
                IsEnabled = true
            };
        }
        else
        {
            definition.ScheduleType = ScheduleType.Interval;
            definition.IntervalMinutes = (int)interval.TotalMinutes;
            definition.IsEnabled = true;
        }

        definition.NextRunAt = DateTime.UtcNow.Add(interval);
        definition.ConfigJson = input != null ? JsonSerializer.Serialize(input) : null;
        definition.UpdatedAt = DateTime.UtcNow;

        await _storage.UpsertDefinitionAsync(definition, ct);
        _logger.LogInformation("Registered recurring job {JobTypeId} with interval {Interval}", jobTypeId, interval);
        return jobTypeId;
    }

    public async Task<string> RecurringAsync(string jobTypeId, TimeSpan interval, object? input, RecurringJobOptions options, CancellationToken ct = default)
    {
        var definition = await _storage.GetJobDefinitionAsync(jobTypeId, ct);
        if (definition == null)
        {
            definition = new JobDefinition
            {
                JobTypeId = jobTypeId,
                DisplayName = jobTypeId,
                ScheduleType = ScheduleType.Interval,
                IntervalMinutes = (int)interval.TotalMinutes,
                IsEnabled = true
            };
        }
        else
        {
            definition.ScheduleType = ScheduleType.Interval;
            definition.IntervalMinutes = (int)interval.TotalMinutes;
            definition.IsEnabled = true;
        }

        // Apply options
        definition.PreventOverlapping = options.PreventOverlapping;
        if (options.Queue != null) definition.Queue = options.Queue;
        if (options.MaxRetries.HasValue) definition.MaxRetries = options.MaxRetries.Value;
        if (options.Timeout.HasValue) definition.TimeoutSeconds = (int)options.Timeout.Value.TotalSeconds;

        definition.NextRunAt = DateTime.UtcNow.Add(interval);
        definition.ConfigJson = input != null ? JsonSerializer.Serialize(input) : null;
        definition.UpdatedAt = DateTime.UtcNow;

        await _storage.UpsertDefinitionAsync(definition, ct);
        _logger.LogInformation(
            "Registered recurring job {JobTypeId} with interval {Interval}, PreventOverlapping={PreventOverlapping}",
            jobTypeId, interval, options.PreventOverlapping);
        return jobTypeId;
    }

    public async Task<string> RecurringAsync(string jobTypeId, string cronExpression, object? input = null, TimeZoneInfo? timeZone = null, CancellationToken ct = default)
    {
        if (!_cronScheduler.IsValidExpression(cronExpression))
            throw new ArgumentException($"Invalid CRON expression: {cronExpression}", nameof(cronExpression));

        var tz = timeZone ?? _options.GetDefaultTimeZone();

        var definition = await _storage.GetJobDefinitionAsync(jobTypeId, ct);
        if (definition == null)
        {
            definition = new JobDefinition
            {
                JobTypeId = jobTypeId,
                DisplayName = jobTypeId,
                ScheduleType = ScheduleType.Cron,
                CronExpression = cronExpression,
                TimeZoneId = tz.Id,
                IsEnabled = true
            };
        }
        else
        {
            definition.ScheduleType = ScheduleType.Cron;
            definition.CronExpression = cronExpression;
            definition.TimeZoneId = tz.Id;
            definition.IsEnabled = true;
        }

        definition.NextRunAt = _cronScheduler.GetNextOccurrence(cronExpression, DateTime.UtcNow, tz);
        definition.ConfigJson = input != null ? JsonSerializer.Serialize(input) : null;
        definition.UpdatedAt = DateTime.UtcNow;

        await _storage.UpsertDefinitionAsync(definition, ct);
        _logger.LogInformation("Registered recurring job {JobTypeId} with CRON {Cron}", jobTypeId, cronExpression);
        return jobTypeId;
    }

    public async Task<string> RecurringAsync(string jobTypeId, string cronExpression, object? input, RecurringJobOptions options, CancellationToken ct = default)
    {
        if (!_cronScheduler.IsValidExpression(cronExpression))
            throw new ArgumentException($"Invalid CRON expression: {cronExpression}", nameof(cronExpression));

        var tz = options.TimeZone ?? _options.GetDefaultTimeZone();

        var definition = await _storage.GetJobDefinitionAsync(jobTypeId, ct);
        if (definition == null)
        {
            definition = new JobDefinition
            {
                JobTypeId = jobTypeId,
                DisplayName = jobTypeId,
                ScheduleType = ScheduleType.Cron,
                CronExpression = cronExpression,
                TimeZoneId = tz.Id,
                IsEnabled = true
            };
        }
        else
        {
            definition.ScheduleType = ScheduleType.Cron;
            definition.CronExpression = cronExpression;
            definition.TimeZoneId = tz.Id;
            definition.IsEnabled = true;
        }

        // Apply options
        definition.PreventOverlapping = options.PreventOverlapping;
        if (options.Queue != null) definition.Queue = options.Queue;
        if (options.MaxRetries.HasValue) definition.MaxRetries = options.MaxRetries.Value;
        if (options.Timeout.HasValue) definition.TimeoutSeconds = (int)options.Timeout.Value.TotalSeconds;

        definition.NextRunAt = _cronScheduler.GetNextOccurrence(cronExpression, DateTime.UtcNow, tz);
        definition.ConfigJson = input != null ? JsonSerializer.Serialize(input) : null;
        definition.UpdatedAt = DateTime.UtcNow;

        await _storage.UpsertDefinitionAsync(definition, ct);
        _logger.LogInformation(
            "Registered recurring job {JobTypeId} with CRON {Cron}, PreventOverlapping={PreventOverlapping}",
            jobTypeId, cronExpression, options.PreventOverlapping);
        return jobTypeId;
    }

    public async Task<bool> CancelAsync(Guid runId, CancellationToken ct = default)
    {
        var run = await _storage.GetRunAsync(runId, ct);
        if (run == null)
            return false;

        if (run.Status is JobRunStatus.Completed or JobRunStatus.Failed or JobRunStatus.Cancelled)
            return false;

        run.Status = JobRunStatus.Cancelled;
        run.CompletedAt = DateTime.UtcNow;
        await _storage.UpdateRunAsync(run, ct);

        _logger.LogInformation("Cancelled job run {RunId}", runId);
        return true;
    }

    public async Task<bool> RemoveRecurringAsync(string jobTypeId, CancellationToken ct = default)
    {
        var definition = await _storage.GetJobDefinitionAsync(jobTypeId, ct);
        if (definition == null)
            return false;

        definition.IsEnabled = false;
        definition.ScheduleType = ScheduleType.Manual;
        definition.NextRunAt = null;
        definition.UpdatedAt = DateTime.UtcNow;

        await _storage.UpsertDefinitionAsync(definition, ct);
        _logger.LogInformation("Removed recurring schedule for job {JobTypeId}", jobTypeId);
        return true;
    }

    public async Task<Guid> TriggerAsync(string jobTypeId, object? input = null, CancellationToken ct = default)
    {
        var definition = await _storage.GetJobDefinitionAsync(jobTypeId, ct);

        // Use definition's default input if none provided
        string? inputJson = null;
        if (input != null)
            inputJson = JsonSerializer.Serialize(input);
        else if (definition?.ConfigJson != null)
            inputJson = definition.ConfigJson;

        var run = new JobRun
        {
            JobTypeId = jobTypeId,
            Status = JobRunStatus.Pending,
            TriggerType = JobTriggerType.Manual,
            Queue = _options.DefaultQueue,
            InputJson = inputJson,
            TriggeredBy = "manual",
            CreatedAt = DateTime.UtcNow
        };

        var runId = await _storage.EnqueueAsync(run, ct);
        _logger.LogInformation("Manually triggered job {JobTypeId} with run ID {RunId}", jobTypeId, runId);
        return runId;
    }

    public async Task<Guid> ContinueWithAsync(
        Guid parentRunId,
        string continuationJobTypeId,
        object? input = null,
        ContinuationCondition condition = ContinuationCondition.OnSuccess,
        bool passParentOutput = false,
        string? queue = null,
        CancellationToken ct = default)
    {
        var continuation = new JobContinuation
        {
            ParentRunId = parentRunId,
            ContinuationJobTypeId = continuationJobTypeId,
            Condition = condition,
            InputJson = input != null ? JsonSerializer.Serialize(input) : null,
            PassParentOutput = passParentOutput,
            Queue = queue,
            Status = ContinuationStatus.Pending,
            CreatedAt = DateTime.UtcNow
        };

        await _storage.AddContinuationAsync(continuation, ct);
        _logger.LogInformation(
            "Created continuation {ContinuationId} for parent run {ParentRunId} -> {ContinuationJobTypeId} ({Condition})",
            continuation.Id, parentRunId, continuationJobTypeId, condition);

        return continuation.Id;
    }

    public Task<Guid> ContinueWithAsync<TJob>(
        Guid parentRunId,
        object? input = null,
        ContinuationCondition condition = ContinuationCondition.OnSuccess,
        bool passParentOutput = false,
        string? queue = null,
        CancellationToken ct = default) where TJob : IJob
    {
        var job = Activator.CreateInstance<TJob>();
        return ContinueWithAsync(parentRunId, job.JobTypeId, input, condition, passParentOutput, queue, ct);
    }
}
