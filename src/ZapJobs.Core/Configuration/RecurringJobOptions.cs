namespace ZapJobs.Core;

/// <summary>
/// Options for configuring a recurring job schedule
/// </summary>
public class RecurringJobOptions
{
    /// <summary>
    /// If true, prevent a new run from starting while another instance is running or pending
    /// </summary>
    public bool PreventOverlapping { get; set; }

    /// <summary>
    /// Timezone for CRON expression evaluation (default: UTC)
    /// </summary>
    public TimeZoneInfo? TimeZone { get; set; }

    /// <summary>
    /// Queue name for the recurring job
    /// </summary>
    public string? Queue { get; set; }

    /// <summary>
    /// Maximum retry attempts for this job
    /// </summary>
    public int? MaxRetries { get; set; }

    /// <summary>
    /// Timeout for job execution
    /// </summary>
    public TimeSpan? Timeout { get; set; }
}
