namespace ZapJobs.Core;

/// <summary>
/// Storage interface for webhook subscriptions and deliveries
/// </summary>
public interface IWebhookStorage
{
    #region Subscriptions

    /// <summary>
    /// Create a webhook subscription
    /// </summary>
    Task<Guid> CreateSubscriptionAsync(WebhookSubscription subscription, CancellationToken ct = default);

    /// <summary>
    /// Get a subscription by ID
    /// </summary>
    Task<WebhookSubscription?> GetSubscriptionAsync(Guid id, CancellationToken ct = default);

    /// <summary>
    /// Get all subscriptions
    /// </summary>
    /// <param name="enabledOnly">If true, only return enabled subscriptions</param>
    /// <param name="ct">Cancellation token</param>
    Task<IReadOnlyList<WebhookSubscription>> GetSubscriptionsAsync(bool enabledOnly = false, CancellationToken ct = default);

    /// <summary>
    /// Get subscriptions that match the given event type and filters
    /// </summary>
    /// <param name="eventType">The event type to match</param>
    /// <param name="jobTypeId">The job type ID (for filtering)</param>
    /// <param name="queue">The queue (for filtering)</param>
    /// <param name="ct">Cancellation token</param>
    Task<IReadOnlyList<WebhookSubscription>> GetMatchingSubscriptionsAsync(
        WebhookEventType eventType,
        string? jobTypeId,
        string? queue,
        CancellationToken ct = default);

    /// <summary>
    /// Update a subscription
    /// </summary>
    Task UpdateSubscriptionAsync(WebhookSubscription subscription, CancellationToken ct = default);

    /// <summary>
    /// Delete a subscription and all its deliveries
    /// </summary>
    Task DeleteSubscriptionAsync(Guid id, CancellationToken ct = default);

    #endregion

    #region Deliveries

    /// <summary>
    /// Create a delivery record
    /// </summary>
    Task<Guid> CreateDeliveryAsync(WebhookDelivery delivery, CancellationToken ct = default);

    /// <summary>
    /// Get a delivery by ID
    /// </summary>
    Task<WebhookDelivery?> GetDeliveryAsync(Guid id, CancellationToken ct = default);

    /// <summary>
    /// Get deliveries for a subscription
    /// </summary>
    /// <param name="subscriptionId">The subscription ID</param>
    /// <param name="limit">Maximum deliveries to return</param>
    /// <param name="ct">Cancellation token</param>
    Task<IReadOnlyList<WebhookDelivery>> GetDeliveriesAsync(Guid subscriptionId, int limit = 50, CancellationToken ct = default);

    /// <summary>
    /// Get pending deliveries that need to be retried
    /// </summary>
    /// <param name="limit">Maximum deliveries to return</param>
    /// <param name="ct">Cancellation token</param>
    Task<IReadOnlyList<WebhookDelivery>> GetPendingDeliveriesAsync(int limit = 100, CancellationToken ct = default);

    /// <summary>
    /// Update a delivery record
    /// </summary>
    Task UpdateDeliveryAsync(WebhookDelivery delivery, CancellationToken ct = default);

    /// <summary>
    /// Delete old deliveries for cleanup
    /// </summary>
    /// <param name="olderThan">Delete deliveries older than this</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>Number of deliveries deleted</returns>
    Task<int> DeleteOldDeliveriesAsync(DateTime olderThan, CancellationToken ct = default);

    #endregion
}
