namespace ZapJobs.AspNetCore.Api.Dtos;

/// <summary>
/// DTO representing a webhook subscription
/// </summary>
public record WebhookDto
{
    /// <summary>Webhook ID</summary>
    public required Guid Id { get; init; }

    /// <summary>Human-readable name for the webhook</summary>
    public required string Name { get; init; }

    /// <summary>URL to send webhook events to</summary>
    public required string Url { get; init; }

    /// <summary>Events to subscribe to (e.g., "JobCompleted", "JobFailed")</summary>
    public required string[] Events { get; init; }

    /// <summary>Optional job type filter with wildcard support (e.g., "send-*")</summary>
    public string? JobTypeFilter { get; init; }

    /// <summary>Optional queue filter with wildcard support</summary>
    public string? QueueFilter { get; init; }

    /// <summary>Whether a secret is configured for HMAC signing</summary>
    public bool HasSecret { get; init; }

    /// <summary>Custom HTTP headers to send with webhook requests</summary>
    public Dictionary<string, string>? Headers { get; init; }

    /// <summary>Whether this webhook is enabled</summary>
    public required bool IsEnabled { get; init; }

    /// <summary>When this webhook was created</summary>
    public required DateTime CreatedAt { get; init; }

    /// <summary>When this webhook was last updated</summary>
    public required DateTime UpdatedAt { get; init; }

    /// <summary>Last time a webhook was successfully delivered</summary>
    public DateTime? LastSuccessAt { get; init; }

    /// <summary>Last time a webhook delivery failed</summary>
    public DateTime? LastFailureAt { get; init; }

    /// <summary>Number of consecutive failures</summary>
    public int FailureCount { get; init; }
}

/// <summary>
/// Request to create a new webhook
/// </summary>
public record CreateWebhookRequest
{
    /// <summary>Human-readable name for the webhook</summary>
    public required string Name { get; init; }

    /// <summary>URL to send webhook events to</summary>
    public required string Url { get; init; }

    /// <summary>Events to subscribe to (e.g., "JobCompleted", "JobFailed", "All")</summary>
    public required string[] Events { get; init; }

    /// <summary>Optional job type filter with wildcard support (e.g., "send-*")</summary>
    public string? JobTypeFilter { get; init; }

    /// <summary>Optional queue filter with wildcard support</summary>
    public string? QueueFilter { get; init; }

    /// <summary>Optional secret for HMAC-SHA256 signing</summary>
    public string? Secret { get; init; }

    /// <summary>Custom HTTP headers to send with webhook requests</summary>
    public Dictionary<string, string>? Headers { get; init; }

    /// <summary>Whether this webhook is enabled (default: true)</summary>
    public bool IsEnabled { get; init; } = true;
}

/// <summary>
/// Request to update an existing webhook
/// </summary>
public record UpdateWebhookRequest
{
    /// <summary>Human-readable name for the webhook</summary>
    public string? Name { get; init; }

    /// <summary>URL to send webhook events to</summary>
    public string? Url { get; init; }

    /// <summary>Events to subscribe to</summary>
    public string[]? Events { get; init; }

    /// <summary>Optional job type filter with wildcard support</summary>
    public string? JobTypeFilter { get; init; }

    /// <summary>Optional queue filter with wildcard support</summary>
    public string? QueueFilter { get; init; }

    /// <summary>Optional secret for HMAC-SHA256 signing</summary>
    public string? Secret { get; init; }

    /// <summary>Custom HTTP headers to send with webhook requests</summary>
    public Dictionary<string, string>? Headers { get; init; }

    /// <summary>Whether this webhook is enabled</summary>
    public bool? IsEnabled { get; init; }
}

/// <summary>
/// DTO representing a webhook delivery attempt
/// </summary>
public record WebhookDeliveryDto
{
    /// <summary>Delivery ID</summary>
    public required Guid Id { get; init; }

    /// <summary>Subscription ID</summary>
    public required Guid SubscriptionId { get; init; }

    /// <summary>Event type</summary>
    public required string Event { get; init; }

    /// <summary>HTTP status code (0 if request failed before receiving response)</summary>
    public int StatusCode { get; init; }

    /// <summary>Response body (truncated)</summary>
    public string? ResponseBody { get; init; }

    /// <summary>Error message if delivery failed</summary>
    public string? ErrorMessage { get; init; }

    /// <summary>Attempt number (1-based)</summary>
    public int AttemptNumber { get; init; }

    /// <summary>Whether delivery was successful</summary>
    public bool Success { get; init; }

    /// <summary>Request duration in milliseconds</summary>
    public int DurationMs { get; init; }

    /// <summary>When this delivery was attempted</summary>
    public required DateTime CreatedAt { get; init; }

    /// <summary>When next retry will be attempted (if scheduled)</summary>
    public DateTime? NextRetryAt { get; init; }
}
