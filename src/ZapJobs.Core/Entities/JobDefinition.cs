namespace ZapJobs.Core;

/// <summary>
/// Configuration for a job type - stored in database
/// </summary>
public class JobDefinition
{
    /// <summary>Unique identifier for this job type (e.g., "email-sender", "report-generator")</summary>
    public string JobTypeId { get; set; } = string.Empty;

    /// <summary>Human-readable name</summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>Description of what this job does</summary>
    public string? Description { get; set; }

    /// <summary>Queue name for prioritization (default: "default")</summary>
    public string Queue { get; set; } = "default";

    // Scheduling

    /// <summary>How this job is scheduled</summary>
    public ScheduleType ScheduleType { get; set; } = ScheduleType.Manual;

    /// <summary>Interval in minutes for ScheduleType.Interval</summary>
    public int? IntervalMinutes { get; set; }

    /// <summary>CRON expression for ScheduleType.Cron</summary>
    public string? CronExpression { get; set; }

    /// <summary>Timezone ID for CRON (e.g., "America/Sao_Paulo")</summary>
    public string? TimeZoneId { get; set; }

    // Behavior

    /// <summary>Whether this job is enabled</summary>
    public bool IsEnabled { get; set; } = true;

    /// <summary>Maximum retry attempts on failure</summary>
    public int MaxRetries { get; set; } = 3;

    /// <summary>Timeout in seconds</summary>
    public int TimeoutSeconds { get; set; } = 3600;

    /// <summary>Maximum concurrent executions allowed</summary>
    public int MaxConcurrency { get; set; } = 1;

    /// <summary>
    /// If true, a new run won't be created while another is still running or pending.
    /// Useful for long-running jobs where overlapping executions are undesirable.
    /// </summary>
    public bool PreventOverlapping { get; set; }

    // State

    /// <summary>When this job last ran</summary>
    public DateTime? LastRunAt { get; set; }

    /// <summary>When this job should run next</summary>
    public DateTime? NextRunAt { get; set; }

    /// <summary>Status of the last run</summary>
    public JobRunStatus? LastRunStatus { get; set; }

    // Metadata

    /// <summary>Additional configuration as JSON</summary>
    public string? ConfigJson { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
