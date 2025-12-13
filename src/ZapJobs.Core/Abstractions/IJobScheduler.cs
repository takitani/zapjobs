namespace ZapJobs.Core;

/// <summary>
/// Public API for scheduling jobs
/// </summary>
public interface IJobScheduler
{
    /// <summary>Enqueue a job for immediate execution</summary>
    /// <param name="jobTypeId">Job type identifier</param>
    /// <param name="input">Optional input data</param>
    /// <param name="queue">Optional queue name (default: "default")</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>Run ID</returns>
    Task<Guid> EnqueueAsync(string jobTypeId, object? input = null, string? queue = null, CancellationToken ct = default);

    /// <summary>Enqueue a typed job for immediate execution</summary>
    Task<Guid> EnqueueAsync<TJob>(object? input = null, string? queue = null, CancellationToken ct = default) where TJob : IJob;

    /// <summary>Schedule a job to run after a delay</summary>
    /// <param name="jobTypeId">Job type identifier</param>
    /// <param name="delay">Time to wait before execution</param>
    /// <param name="input">Optional input data</param>
    /// <param name="queue">Optional queue name</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>Run ID</returns>
    Task<Guid> ScheduleAsync(string jobTypeId, TimeSpan delay, object? input = null, string? queue = null, CancellationToken ct = default);

    /// <summary>Schedule a job to run at a specific time</summary>
    /// <param name="jobTypeId">Job type identifier</param>
    /// <param name="runAt">When to execute</param>
    /// <param name="input">Optional input data</param>
    /// <param name="queue">Optional queue name</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>Run ID</returns>
    Task<Guid> ScheduleAsync(string jobTypeId, DateTimeOffset runAt, object? input = null, string? queue = null, CancellationToken ct = default);

    /// <summary>Schedule a recurring job with fixed interval</summary>
    /// <param name="jobTypeId">Job type identifier</param>
    /// <param name="interval">Time between executions</param>
    /// <param name="input">Optional input data</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>Job type ID for reference</returns>
    Task<string> RecurringAsync(string jobTypeId, TimeSpan interval, object? input = null, CancellationToken ct = default);

    /// <summary>Schedule a recurring job with CRON expression</summary>
    /// <param name="jobTypeId">Job type identifier</param>
    /// <param name="cronExpression">CRON expression (5 or 6 parts)</param>
    /// <param name="input">Optional input data</param>
    /// <param name="timeZone">Optional timezone (default: UTC)</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>Job type ID for reference</returns>
    Task<string> RecurringAsync(string jobTypeId, string cronExpression, object? input = null, TimeZoneInfo? timeZone = null, CancellationToken ct = default);

    /// <summary>Cancel a pending or running job</summary>
    /// <param name="runId">Run ID to cancel</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>True if cancelled, false if not found or already completed</returns>
    Task<bool> CancelAsync(Guid runId, CancellationToken ct = default);

    /// <summary>Remove a recurring job schedule</summary>
    /// <param name="jobTypeId">Job type identifier</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>True if removed</returns>
    Task<bool> RemoveRecurringAsync(string jobTypeId, CancellationToken ct = default);

    /// <summary>Trigger a recurring job to run immediately (outside normal schedule)</summary>
    /// <param name="jobTypeId">Job type identifier</param>
    /// <param name="input">Optional input data (overrides default)</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>Run ID</returns>
    Task<Guid> TriggerAsync(string jobTypeId, object? input = null, CancellationToken ct = default);

    // Continuations

    /// <summary>Create a continuation that runs when parentRunId completes</summary>
    /// <param name="parentRunId">Run ID that triggers this continuation</param>
    /// <param name="continuationJobTypeId">Job type to execute as continuation</param>
    /// <param name="input">Optional input data for continuation</param>
    /// <param name="condition">When to trigger (OnSuccess, OnFailure, Always)</param>
    /// <param name="passParentOutput">If true, pass parent's output as continuation input</param>
    /// <param name="queue">Optional queue name</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>Continuation ID</returns>
    Task<Guid> ContinueWithAsync(
        Guid parentRunId,
        string continuationJobTypeId,
        object? input = null,
        ContinuationCondition condition = ContinuationCondition.OnSuccess,
        bool passParentOutput = false,
        string? queue = null,
        CancellationToken ct = default);

    /// <summary>Create a continuation using generic job type</summary>
    /// <typeparam name="TJob">Job type to execute as continuation</typeparam>
    /// <param name="parentRunId">Run ID that triggers this continuation</param>
    /// <param name="input">Optional input data for continuation</param>
    /// <param name="condition">When to trigger (OnSuccess, OnFailure, Always)</param>
    /// <param name="passParentOutput">If true, pass parent's output as continuation input</param>
    /// <param name="queue">Optional queue name</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>Continuation ID</returns>
    Task<Guid> ContinueWithAsync<TJob>(
        Guid parentRunId,
        object? input = null,
        ContinuationCondition condition = ContinuationCondition.OnSuccess,
        bool passParentOutput = false,
        string? queue = null,
        CancellationToken ct = default) where TJob : IJob;
}
