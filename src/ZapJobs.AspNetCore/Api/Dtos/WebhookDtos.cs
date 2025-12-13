namespace ZapJobs.AspNetCore.Api.Dtos;

/// <summary>
/// DTO representing a webhook subscription
/// </summary>
public record WebhookDto
{
    /// <summary>Webhook ID</summary>
    public required Guid Id { get; init; }

    /// <summary>URL to send webhook events to</summary>
    public required string Url { get; init; }

    /// <summary>Events to subscribe to (e.g., "job.completed", "job.failed")</summary>
    public required string[] Events { get; init; }

    /// <summary>Optional job type filter (null means all jobs)</summary>
    public string? JobTypeId { get; init; }

    /// <summary>Whether this webhook is enabled</summary>
    public required bool IsEnabled { get; init; }

    /// <summary>Optional secret for signing payloads</summary>
    public string? Secret { get; init; }

    /// <summary>When this webhook was created</summary>
    public required DateTime CreatedAt { get; init; }

    /// <summary>When this webhook was last updated</summary>
    public required DateTime UpdatedAt { get; init; }

    /// <summary>Last time a webhook was successfully delivered</summary>
    public DateTime? LastDeliveryAt { get; init; }

    /// <summary>Number of successful deliveries</summary>
    public int SuccessCount { get; init; }

    /// <summary>Number of failed deliveries</summary>
    public int FailureCount { get; init; }
}

/// <summary>
/// Request to create a new webhook
/// </summary>
public record CreateWebhookRequest
{
    /// <summary>URL to send webhook events to</summary>
    public required string Url { get; init; }

    /// <summary>Events to subscribe to (e.g., "job.completed", "job.failed")</summary>
    public required string[] Events { get; init; }

    /// <summary>Optional job type filter (null means all jobs)</summary>
    public string? JobTypeId { get; init; }

    /// <summary>Optional secret for signing payloads</summary>
    public string? Secret { get; init; }

    /// <summary>Whether this webhook is enabled (default: true)</summary>
    public bool IsEnabled { get; init; } = true;
}

/// <summary>
/// Request to update an existing webhook
/// </summary>
public record UpdateWebhookRequest
{
    /// <summary>URL to send webhook events to</summary>
    public string? Url { get; init; }

    /// <summary>Events to subscribe to</summary>
    public string[]? Events { get; init; }

    /// <summary>Optional job type filter</summary>
    public string? JobTypeId { get; init; }

    /// <summary>Optional secret for signing payloads</summary>
    public string? Secret { get; init; }

    /// <summary>Whether this webhook is enabled</summary>
    public bool? IsEnabled { get; init; }
}
