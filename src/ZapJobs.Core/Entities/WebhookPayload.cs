using System.Text.Json.Serialization;

namespace ZapJobs.Core;

/// <summary>
/// Payload sent to webhook endpoints
/// </summary>
public class WebhookPayload
{
    /// <summary>
    /// The event type (e.g., "JobCompleted", "JobFailed")
    /// </summary>
    [JsonPropertyName("event")]
    public string Event { get; set; } = string.Empty;

    /// <summary>
    /// When the event occurred
    /// </summary>
    [JsonPropertyName("timestamp")]
    public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// The job type ID (e.g., "send-email")
    /// </summary>
    [JsonPropertyName("jobTypeId")]
    public string? JobTypeId { get; set; }

    /// <summary>
    /// The unique run ID
    /// </summary>
    [JsonPropertyName("runId")]
    public Guid? RunId { get; set; }

    /// <summary>
    /// The queue the job ran on
    /// </summary>
    [JsonPropertyName("queue")]
    public string? Queue { get; set; }

    /// <summary>
    /// Current status of the job
    /// </summary>
    [JsonPropertyName("status")]
    public string? Status { get; set; }

    /// <summary>
    /// Error message if the job failed
    /// </summary>
    [JsonPropertyName("errorMessage")]
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// Error type if the job failed
    /// </summary>
    [JsonPropertyName("errorType")]
    public string? ErrorType { get; set; }

    /// <summary>
    /// Duration of the job execution in milliseconds
    /// </summary>
    [JsonPropertyName("durationMs")]
    public int? DurationMs { get; set; }

    /// <summary>
    /// Current attempt number
    /// </summary>
    [JsonPropertyName("attemptNumber")]
    public int? AttemptNumber { get; set; }

    /// <summary>
    /// Maximum retry attempts configured
    /// </summary>
    [JsonPropertyName("maxRetries")]
    public int? MaxRetries { get; set; }

    /// <summary>
    /// Whether the job will be retried
    /// </summary>
    [JsonPropertyName("willRetry")]
    public bool? WillRetry { get; set; }

    /// <summary>
    /// When the next retry will occur
    /// </summary>
    [JsonPropertyName("nextRetryAt")]
    public DateTimeOffset? NextRetryAt { get; set; }

    /// <summary>
    /// Whether the job was moved to dead letter queue
    /// </summary>
    [JsonPropertyName("movedToDeadLetter")]
    public bool? MovedToDeadLetter { get; set; }

    /// <summary>
    /// Job output (if completed successfully)
    /// </summary>
    [JsonPropertyName("output")]
    public object? Output { get; set; }

    /// <summary>
    /// Number of items processed
    /// </summary>
    [JsonPropertyName("itemsProcessed")]
    public int? ItemsProcessed { get; set; }

    /// <summary>
    /// Number of items that succeeded
    /// </summary>
    [JsonPropertyName("itemsSucceeded")]
    public int? ItemsSucceeded { get; set; }

    /// <summary>
    /// Number of items that failed
    /// </summary>
    [JsonPropertyName("itemsFailed")]
    public int? ItemsFailed { get; set; }
}
