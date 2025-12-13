using ZapJobs.Core;

namespace ZapJobs.AspNetCore.Api.Dtos;

/// <summary>
/// DTO representing a job run
/// </summary>
public record JobRunDto
{
    /// <summary>Unique identifier for this run</summary>
    public required Guid Id { get; init; }

    /// <summary>The job type this run belongs to</summary>
    public required string JobTypeId { get; init; }

    /// <summary>Current status of the run</summary>
    public required string Status { get; init; }

    /// <summary>How this run was triggered</summary>
    public required string TriggerType { get; init; }

    /// <summary>Queue this run was placed in</summary>
    public required string Queue { get; init; }

    /// <summary>When the run was created/enqueued</summary>
    public required DateTime CreatedAt { get; init; }

    /// <summary>When the run is scheduled to execute</summary>
    public DateTime? ScheduledAt { get; init; }

    /// <summary>When execution started</summary>
    public DateTime? StartedAt { get; init; }

    /// <summary>When execution completed</summary>
    public DateTime? CompletedAt { get; init; }

    /// <summary>Execution duration in milliseconds</summary>
    public int? DurationMs { get; init; }

    /// <summary>Current attempt number</summary>
    public required int AttemptNumber { get; init; }

    /// <summary>Error message if failed</summary>
    public string? ErrorMessage { get; init; }

    /// <summary>Worker instance processing this run</summary>
    public string? WorkerId { get; init; }

    /// <summary>Current progress (0-100)</summary>
    public int Progress { get; init; }

    /// <summary>Current progress message</summary>
    public string? ProgressMessage { get; init; }

    public static JobRunDto FromEntity(JobRun r) => new()
    {
        Id = r.Id,
        JobTypeId = r.JobTypeId,
        Status = r.Status.ToString(),
        TriggerType = r.TriggerType.ToString(),
        Queue = r.Queue,
        CreatedAt = r.CreatedAt,
        ScheduledAt = r.ScheduledAt,
        StartedAt = r.StartedAt,
        CompletedAt = r.CompletedAt,
        DurationMs = r.DurationMs,
        AttemptNumber = r.AttemptNumber,
        ErrorMessage = r.ErrorMessage,
        WorkerId = r.WorkerId,
        Progress = r.Progress,
        ProgressMessage = r.ProgressMessage
    };
}

/// <summary>
/// Detailed DTO for a job run including logs
/// </summary>
public record JobRunDetailDto
{
    /// <summary>Unique identifier for this run</summary>
    public required Guid Id { get; init; }

    /// <summary>The job type this run belongs to</summary>
    public required string JobTypeId { get; init; }

    /// <summary>Current status of the run</summary>
    public required string Status { get; init; }

    /// <summary>How this run was triggered</summary>
    public required string TriggerType { get; init; }

    /// <summary>Who/what triggered this run</summary>
    public string? TriggeredBy { get; init; }

    /// <summary>Queue this run was placed in</summary>
    public required string Queue { get; init; }

    /// <summary>Worker instance processing this run</summary>
    public string? WorkerId { get; init; }

    /// <summary>When the run was created/enqueued</summary>
    public required DateTime CreatedAt { get; init; }

    /// <summary>When the run is scheduled to execute</summary>
    public DateTime? ScheduledAt { get; init; }

    /// <summary>When execution started</summary>
    public DateTime? StartedAt { get; init; }

    /// <summary>When execution completed</summary>
    public DateTime? CompletedAt { get; init; }

    /// <summary>Execution duration in milliseconds</summary>
    public int? DurationMs { get; init; }

    /// <summary>Current progress (0-100)</summary>
    public int Progress { get; init; }

    /// <summary>Total items to process</summary>
    public int Total { get; init; }

    /// <summary>Current progress message</summary>
    public string? ProgressMessage { get; init; }

    /// <summary>Number of items processed</summary>
    public int ItemsProcessed { get; init; }

    /// <summary>Number of items that succeeded</summary>
    public int ItemsSucceeded { get; init; }

    /// <summary>Number of items that failed</summary>
    public int ItemsFailed { get; init; }

    /// <summary>Current attempt number</summary>
    public required int AttemptNumber { get; init; }

    /// <summary>When the next retry should occur</summary>
    public DateTime? NextRetryAt { get; init; }

    /// <summary>Error message if failed</summary>
    public string? ErrorMessage { get; init; }

    /// <summary>Error stack trace if failed</summary>
    public string? StackTrace { get; init; }

    /// <summary>Exception type name</summary>
    public string? ErrorType { get; init; }

    /// <summary>Input parameters as JSON string</summary>
    public string? InputJson { get; init; }

    /// <summary>Output/result as JSON string</summary>
    public string? OutputJson { get; init; }

    /// <summary>Execution logs</summary>
    public IReadOnlyList<JobLogDto> Logs { get; init; } = [];

    public static JobRunDetailDto FromEntity(JobRun r, IReadOnlyList<JobLog>? logs = null) => new()
    {
        Id = r.Id,
        JobTypeId = r.JobTypeId,
        Status = r.Status.ToString(),
        TriggerType = r.TriggerType.ToString(),
        TriggeredBy = r.TriggeredBy,
        Queue = r.Queue,
        WorkerId = r.WorkerId,
        CreatedAt = r.CreatedAt,
        ScheduledAt = r.ScheduledAt,
        StartedAt = r.StartedAt,
        CompletedAt = r.CompletedAt,
        DurationMs = r.DurationMs,
        Progress = r.Progress,
        Total = r.Total,
        ProgressMessage = r.ProgressMessage,
        ItemsProcessed = r.ItemsProcessed,
        ItemsSucceeded = r.ItemsSucceeded,
        ItemsFailed = r.ItemsFailed,
        AttemptNumber = r.AttemptNumber,
        NextRetryAt = r.NextRetryAt,
        ErrorMessage = r.ErrorMessage,
        StackTrace = r.StackTrace,
        ErrorType = r.ErrorType,
        InputJson = r.InputJson,
        OutputJson = r.OutputJson,
        Logs = logs?.Select(JobLogDto.FromEntity).ToList() ?? []
    };
}

/// <summary>
/// DTO representing a job log entry
/// </summary>
public record JobLogDto
{
    /// <summary>Log entry ID</summary>
    public required Guid Id { get; init; }

    /// <summary>Log level</summary>
    public required string Level { get; init; }

    /// <summary>Log message</summary>
    public required string Message { get; init; }

    /// <summary>Category/phase</summary>
    public string? Category { get; init; }

    /// <summary>Additional context data as JSON</summary>
    public string? ContextJson { get; init; }

    /// <summary>Duration of the operation in milliseconds</summary>
    public int? DurationMs { get; init; }

    /// <summary>Exception details if any</summary>
    public string? Exception { get; init; }

    /// <summary>When this log was created</summary>
    public required DateTime Timestamp { get; init; }

    public static JobLogDto FromEntity(JobLog log) => new()
    {
        Id = log.Id,
        Level = log.Level.ToString(),
        Message = log.Message,
        Category = log.Category,
        ContextJson = log.ContextJson,
        DurationMs = log.DurationMs,
        Exception = log.Exception,
        Timestamp = log.Timestamp
    };
}
