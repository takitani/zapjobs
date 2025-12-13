using System.Text.Json;
using ZapJobs.Core;

namespace ZapJobs.AspNetCore.Api.Dtos;

/// <summary>
/// Request to trigger a job for immediate execution
/// </summary>
public record TriggerRequest
{
    /// <summary>Optional input parameters for the job</summary>
    public JsonElement? Input { get; init; }

    /// <summary>Optional queue to place the job in (uses default if not specified)</summary>
    public string? Queue { get; init; }
}

/// <summary>
/// Response after triggering a job
/// </summary>
public record TriggerResponse
{
    /// <summary>Unique identifier for the created run</summary>
    public required Guid RunId { get; init; }

    /// <summary>The job type that was triggered</summary>
    public required string JobTypeId { get; init; }

    /// <summary>Current status of the run</summary>
    public required string Status { get; init; }
}

/// <summary>
/// Request to schedule a job for later execution
/// </summary>
public record ScheduleRequest
{
    /// <summary>Optional input parameters for the job</summary>
    public JsonElement? Input { get; init; }

    /// <summary>Optional queue to place the job in (uses default if not specified)</summary>
    public string? Queue { get; init; }

    /// <summary>Specific time to run the job (UTC). Either this or Delay must be specified.</summary>
    public DateTimeOffset? RunAt { get; init; }

    /// <summary>Time to delay before running the job. Either this or RunAt must be specified.</summary>
    public TimeSpan? Delay { get; init; }
}

/// <summary>
/// Response after scheduling a job
/// </summary>
public record ScheduleResponse
{
    /// <summary>Unique identifier for the created run</summary>
    public required Guid RunId { get; init; }

    /// <summary>The job type that was scheduled</summary>
    public required string JobTypeId { get; init; }

    /// <summary>When the job is scheduled to run</summary>
    public required DateTimeOffset ScheduledFor { get; init; }
}

/// <summary>
/// Request to update a job definition
/// </summary>
public record UpdateJobDefinitionRequest
{
    /// <summary>Human-readable name</summary>
    public string? DisplayName { get; init; }

    /// <summary>Description of what this job does</summary>
    public string? Description { get; init; }

    /// <summary>Queue name for prioritization</summary>
    public string? Queue { get; init; }

    /// <summary>Whether this job is enabled</summary>
    public bool? IsEnabled { get; init; }

    /// <summary>Maximum retry attempts on failure</summary>
    public int? MaxRetries { get; init; }

    /// <summary>Timeout in seconds</summary>
    public int? TimeoutSeconds { get; init; }

    /// <summary>Maximum concurrent executions allowed</summary>
    public int? MaxConcurrency { get; init; }

    /// <summary>CRON expression for scheduled execution</summary>
    public string? CronExpression { get; init; }

    /// <summary>Timezone ID for CRON (e.g., "America/Sao_Paulo")</summary>
    public string? TimeZoneId { get; init; }
}

/// <summary>
/// DTO representing a job definition
/// </summary>
public record JobDefinitionDto
{
    /// <summary>Unique identifier for this job type</summary>
    public required string JobTypeId { get; init; }

    /// <summary>Human-readable name</summary>
    public required string DisplayName { get; init; }

    /// <summary>Description of what this job does</summary>
    public string? Description { get; init; }

    /// <summary>Queue name for prioritization</summary>
    public required string Queue { get; init; }

    /// <summary>How this job is scheduled</summary>
    public required string ScheduleType { get; init; }

    /// <summary>CRON expression if using CRON scheduling</summary>
    public string? CronExpression { get; init; }

    /// <summary>Interval in minutes if using interval scheduling</summary>
    public int? IntervalMinutes { get; init; }

    /// <summary>Whether this job is enabled</summary>
    public required bool IsEnabled { get; init; }

    /// <summary>Maximum retry attempts on failure</summary>
    public required int MaxRetries { get; init; }

    /// <summary>Timeout in seconds</summary>
    public required int TimeoutSeconds { get; init; }

    /// <summary>Maximum concurrent executions allowed</summary>
    public required int MaxConcurrency { get; init; }

    /// <summary>When this job last ran</summary>
    public DateTime? LastRunAt { get; init; }

    /// <summary>When this job should run next</summary>
    public DateTime? NextRunAt { get; init; }

    /// <summary>Status of the last run</summary>
    public string? LastRunStatus { get; init; }

    /// <summary>When this job definition was created</summary>
    public required DateTime CreatedAt { get; init; }

    /// <summary>When this job definition was last updated</summary>
    public required DateTime UpdatedAt { get; init; }

    public static JobDefinitionDto FromEntity(JobDefinition d) => new()
    {
        JobTypeId = d.JobTypeId,
        DisplayName = d.DisplayName,
        Description = d.Description,
        Queue = d.Queue,
        ScheduleType = d.ScheduleType.ToString(),
        CronExpression = d.CronExpression,
        IntervalMinutes = d.IntervalMinutes,
        IsEnabled = d.IsEnabled,
        MaxRetries = d.MaxRetries,
        TimeoutSeconds = d.TimeoutSeconds,
        MaxConcurrency = d.MaxConcurrency,
        LastRunAt = d.LastRunAt,
        NextRunAt = d.NextRunAt,
        LastRunStatus = d.LastRunStatus?.ToString(),
        CreatedAt = d.CreatedAt,
        UpdatedAt = d.UpdatedAt
    };
}
