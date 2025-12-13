namespace ZapJobs.Core;

/// <summary>
/// Status of a job run
/// </summary>
public enum JobRunStatus
{
    /// <summary>Job is queued and waiting to be processed</summary>
    Pending = 0,

    /// <summary>Job is scheduled to run at a specific time</summary>
    Scheduled = 1,

    /// <summary>Job is currently being executed</summary>
    Running = 2,

    /// <summary>Job completed successfully</summary>
    Completed = 3,

    /// <summary>Job failed with an error</summary>
    Failed = 4,

    /// <summary>Job was cancelled before completion</summary>
    Cancelled = 5,

    /// <summary>Job failed but is waiting for retry</summary>
    AwaitingRetry = 6
}

/// <summary>
/// How the job was triggered
/// </summary>
public enum JobTriggerType
{
    /// <summary>Manually triggered by user/admin</summary>
    Manual = 0,

    /// <summary>Triggered by interval schedule</summary>
    Scheduled = 1,

    /// <summary>Triggered by CRON expression</summary>
    Cron = 2,

    /// <summary>Triggered via API</summary>
    Api = 3,

    /// <summary>Automatic retry after failure</summary>
    Retry = 4,

    /// <summary>Triggered as continuation of another job</summary>
    Continuation = 5
}

/// <summary>
/// Type of schedule for a job definition
/// </summary>
public enum ScheduleType
{
    /// <summary>No automatic scheduling, manual trigger only</summary>
    Manual = 0,

    /// <summary>Run at fixed intervals</summary>
    Interval = 1,

    /// <summary>Run based on CRON expression</summary>
    Cron = 2
}

/// <summary>
/// Log level for job logs
/// </summary>
public enum JobLogLevel
{
    Trace = 0,
    Debug = 1,
    Info = 2,
    Warning = 3,
    Error = 4,
    Critical = 5
}

/// <summary>
/// When a continuation should be triggered
/// </summary>
public enum ContinuationCondition
{
    /// <summary>Only if parent job succeeds</summary>
    OnSuccess = 0,

    /// <summary>Only if parent job fails</summary>
    OnFailure = 1,

    /// <summary>Regardless of parent job result</summary>
    Always = 2
}

/// <summary>
/// Status of a job continuation
/// </summary>
public enum ContinuationStatus
{
    /// <summary>Waiting for parent job to complete</summary>
    Pending = 0,

    /// <summary>Continuation job has been created and enqueued</summary>
    Triggered = 1,

    /// <summary>Continuation was skipped (condition not met)</summary>
    Skipped = 2
}
