namespace ZapJobs.Core;

/// <summary>
/// Service for managing webhook subscriptions and publishing events
/// </summary>
public interface IWebhookService
{
    #region Subscription Management

    /// <summary>
    /// Create a new webhook subscription
    /// </summary>
    /// <param name="subscription">The subscription to create</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>The ID of the created subscription</returns>
    Task<Guid> CreateSubscriptionAsync(WebhookSubscription subscription, CancellationToken ct = default);

    /// <summary>
    /// Get a webhook subscription by ID
    /// </summary>
    /// <param name="id">The subscription ID</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>The subscription or null if not found</returns>
    Task<WebhookSubscription?> GetSubscriptionAsync(Guid id, CancellationToken ct = default);

    /// <summary>
    /// Get all webhook subscriptions
    /// </summary>
    /// <param name="enabledOnly">If true, only return enabled subscriptions</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>List of subscriptions</returns>
    Task<IReadOnlyList<WebhookSubscription>> GetSubscriptionsAsync(bool enabledOnly = false, CancellationToken ct = default);

    /// <summary>
    /// Update an existing webhook subscription
    /// </summary>
    /// <param name="subscription">The subscription with updated values</param>
    /// <param name="ct">Cancellation token</param>
    Task UpdateSubscriptionAsync(WebhookSubscription subscription, CancellationToken ct = default);

    /// <summary>
    /// Delete a webhook subscription
    /// </summary>
    /// <param name="id">The subscription ID to delete</param>
    /// <param name="ct">Cancellation token</param>
    Task DeleteSubscriptionAsync(Guid id, CancellationToken ct = default);

    /// <summary>
    /// Test a webhook subscription by sending a test payload
    /// </summary>
    /// <param name="id">The subscription ID to test</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>The delivery result</returns>
    Task<WebhookDelivery> TestSubscriptionAsync(Guid id, CancellationToken ct = default);

    #endregion

    #region Event Publishing

    /// <summary>
    /// Publish an event to all matching webhook subscriptions
    /// </summary>
    /// <param name="eventType">The type of event</param>
    /// <param name="payload">The event payload</param>
    /// <param name="ct">Cancellation token</param>
    Task PublishEventAsync(WebhookEventType eventType, WebhookPayload payload, CancellationToken ct = default);

    #endregion

    #region Delivery History

    /// <summary>
    /// Get delivery history for a subscription
    /// </summary>
    /// <param name="subscriptionId">The subscription ID</param>
    /// <param name="limit">Maximum number of deliveries to return</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>List of deliveries, newest first</returns>
    Task<IReadOnlyList<WebhookDelivery>> GetDeliveriesAsync(Guid subscriptionId, int limit = 50, CancellationToken ct = default);

    /// <summary>
    /// Manually retry a failed delivery
    /// </summary>
    /// <param name="deliveryId">The delivery ID to retry</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>The new delivery attempt</returns>
    Task<WebhookDelivery> RetryDeliveryAsync(Guid deliveryId, CancellationToken ct = default);

    #endregion
}
