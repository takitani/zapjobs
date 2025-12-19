namespace ZapJobs.Core;

/// <summary>
/// A webhook subscription that receives HTTP notifications for job events
/// </summary>
public class WebhookSubscription
{
    /// <summary>
    /// Unique identifier for the subscription
    /// </summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>
    /// Human-readable name for identification
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// URL to POST webhook payloads to
    /// </summary>
    public string Url { get; set; } = string.Empty;

    /// <summary>
    /// Events to subscribe to (flags enum)
    /// </summary>
    public WebhookEventType Events { get; set; }

    /// <summary>
    /// Filter by job type (null = all, supports wildcards like "send-*")
    /// </summary>
    public string? JobTypeFilter { get; set; }

    /// <summary>
    /// Filter by queue (null = all)
    /// </summary>
    public string? QueueFilter { get; set; }

    /// <summary>
    /// Secret key for HMAC-SHA256 signature (sent in X-ZapJobs-Signature header)
    /// </summary>
    public string? Secret { get; set; }

    /// <summary>
    /// Custom headers to include in webhook requests (JSON object)
    /// </summary>
    public string? HeadersJson { get; set; }

    /// <summary>
    /// Whether this subscription is active
    /// </summary>
    public bool IsEnabled { get; set; } = true;

    /// <summary>
    /// Timestamp of last successful delivery
    /// </summary>
    public DateTime? LastSuccessAt { get; set; }

    /// <summary>
    /// Timestamp of last failed delivery
    /// </summary>
    public DateTime? LastFailureAt { get; set; }

    /// <summary>
    /// Number of consecutive delivery failures
    /// </summary>
    public int FailureCount { get; set; }

    /// <summary>
    /// When the subscription was created
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// When the subscription was last updated
    /// </summary>
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Event types that can trigger webhooks
/// </summary>
[Flags]
public enum WebhookEventType
{
    /// <summary>No events</summary>
    None = 0,

    /// <summary>Job execution started</summary>
    JobStarted = 1,

    /// <summary>Job completed successfully</summary>
    JobCompleted = 2,

    /// <summary>Job failed (final failure or will retry)</summary>
    JobFailed = 4,

    /// <summary>Job is being retried</summary>
    JobRetrying = 8,

    /// <summary>Job was cancelled</summary>
    JobCancelled = 16,

    /// <summary>Job was enqueued</summary>
    JobEnqueued = 32,

    /// <summary>All job-related events</summary>
    AllJobEvents = JobStarted | JobCompleted | JobFailed | JobRetrying | JobCancelled | JobEnqueued,

    /// <summary>All events</summary>
    All = AllJobEvents
}
