namespace ZapJobs.Core;

/// <summary>
/// Configuration options for ZapJobs
/// </summary>
public class ZapJobsOptions
{
    public const string SectionName = "ZapJobs";

    /// <summary>Number of worker threads (default: ProcessorCount)</summary>
    public int WorkerCount { get; set; } = Environment.ProcessorCount;

    /// <summary>Default queue name</summary>
    public string DefaultQueue { get; set; } = "default";

    /// <summary>Queues to process in priority order</summary>
    public string[] Queues { get; set; } = ["critical", "default", "low"];

    /// <summary>Polling interval for checking new jobs</summary>
    public TimeSpan PollingInterval { get; set; } = TimeSpan.FromSeconds(15);

    /// <summary>Heartbeat interval</summary>
    public TimeSpan HeartbeatInterval { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>Worker considered stale after this threshold</summary>
    public TimeSpan StaleWorkerThreshold { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>Default retry policy</summary>
    public RetryPolicy DefaultRetryPolicy { get; set; } = new();

    /// <summary>Default job timeout</summary>
    public TimeSpan DefaultTimeout { get; set; } = TimeSpan.FromMinutes(60);

    /// <summary>Default timezone for CRON expressions (null = UTC)</summary>
    public string? DefaultTimeZoneId { get; set; }

    /// <summary>How long to keep completed job runs</summary>
    public TimeSpan JobRetention { get; set; } = TimeSpan.FromDays(30);

    /// <summary>How long to keep job logs</summary>
    public TimeSpan LogRetention { get; set; } = TimeSpan.FromDays(7);

    /// <summary>Worker instance ID (auto-generated if null)</summary>
    public string? WorkerId { get; set; }

    /// <summary>Enable/disable the scheduler (useful for worker-only instances)</summary>
    public bool EnableScheduler { get; set; } = true;

    /// <summary>Enable/disable job processing (useful for scheduler-only instances)</summary>
    public bool EnableProcessing { get; set; } = true;

    /// <summary>Maximum number of retries for transient storage failures</summary>
    public int StorageRetryCount { get; set; } = 3;

    /// <summary>Delay between storage retry attempts</summary>
    public TimeSpan StorageRetryDelay { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>
    /// Global rate limit applied to all job executions.
    /// If null, no global rate limit is applied.
    /// </summary>
    public RateLimitPolicy? GlobalRateLimit { get; set; }

    /// <summary>
    /// Rate limits per queue. Key is the queue name.
    /// </summary>
    public Dictionary<string, RateLimitPolicy> QueueRateLimits { get; set; } = new();

    /// <summary>Get default timezone</summary>
    public TimeZoneInfo GetDefaultTimeZone()
    {
        if (string.IsNullOrEmpty(DefaultTimeZoneId))
            return TimeZoneInfo.Utc;

        return TimeZoneInfo.FindSystemTimeZoneById(DefaultTimeZoneId);
    }
}
