namespace ZapJobs.Core;

/// <summary>
/// Record of a webhook delivery attempt
/// </summary>
public class WebhookDelivery
{
    /// <summary>
    /// Unique identifier for the delivery
    /// </summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>
    /// The subscription this delivery belongs to
    /// </summary>
    public Guid SubscriptionId { get; set; }

    /// <summary>
    /// The event type that triggered this delivery
    /// </summary>
    public WebhookEventType Event { get; set; }

    /// <summary>
    /// The JSON payload that was sent
    /// </summary>
    public string PayloadJson { get; set; } = string.Empty;

    /// <summary>
    /// HTTP status code returned by the endpoint
    /// </summary>
    public int StatusCode { get; set; }

    /// <summary>
    /// Response body from the endpoint (truncated if too long)
    /// </summary>
    public string? ResponseBody { get; set; }

    /// <summary>
    /// Error message if the request failed
    /// </summary>
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// Which attempt number this was (1-based)
    /// </summary>
    public int AttemptNumber { get; set; }

    /// <summary>
    /// Whether the delivery was successful (2xx status code)
    /// </summary>
    public bool Success { get; set; }

    /// <summary>
    /// Duration of the HTTP request in milliseconds
    /// </summary>
    public int DurationMs { get; set; }

    /// <summary>
    /// When the delivery attempt was made
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// When to retry this delivery (null if no retry needed)
    /// </summary>
    public DateTime? NextRetryAt { get; set; }
}

/// <summary>
/// Status of a webhook delivery
/// </summary>
public enum WebhookDeliveryStatus
{
    /// <summary>Waiting to be delivered</summary>
    Pending,

    /// <summary>Delivery succeeded</summary>
    Delivered,

    /// <summary>Delivery failed, will retry</summary>
    Failed,

    /// <summary>Permanently failed after all retries</summary>
    Abandoned
}
