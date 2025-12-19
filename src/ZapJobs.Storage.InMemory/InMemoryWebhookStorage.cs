using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using ZapJobs.Core;

namespace ZapJobs.Storage.InMemory;

/// <summary>
/// In-memory implementation of webhook storage for development and testing
/// </summary>
public class InMemoryWebhookStorage : IWebhookStorage
{
    private readonly ConcurrentDictionary<Guid, WebhookSubscription> _subscriptions = new();
    private readonly ConcurrentDictionary<Guid, WebhookDelivery> _deliveries = new();

    #region Subscriptions

    public Task<Guid> CreateSubscriptionAsync(WebhookSubscription subscription, CancellationToken ct = default)
    {
        _subscriptions[subscription.Id] = subscription;
        return Task.FromResult(subscription.Id);
    }

    public Task<WebhookSubscription?> GetSubscriptionAsync(Guid id, CancellationToken ct = default)
    {
        _subscriptions.TryGetValue(id, out var subscription);
        return Task.FromResult(subscription);
    }

    public Task<IReadOnlyList<WebhookSubscription>> GetSubscriptionsAsync(bool enabledOnly = false, CancellationToken ct = default)
    {
        var query = _subscriptions.Values.AsEnumerable();

        if (enabledOnly)
        {
            query = query.Where(s => s.IsEnabled);
        }

        return Task.FromResult<IReadOnlyList<WebhookSubscription>>(
            query.OrderByDescending(s => s.CreatedAt).ToList());
    }

    public Task<IReadOnlyList<WebhookSubscription>> GetMatchingSubscriptionsAsync(
        WebhookEventType eventType,
        string? jobTypeId,
        string? queue,
        CancellationToken ct = default)
    {
        var matching = _subscriptions.Values
            .Where(s => s.IsEnabled)
            .Where(s => s.Events.HasFlag(eventType))
            .Where(s => MatchesFilter(s.JobTypeFilter, jobTypeId))
            .Where(s => MatchesFilter(s.QueueFilter, queue))
            .ToList();

        return Task.FromResult<IReadOnlyList<WebhookSubscription>>(matching);
    }

    public Task UpdateSubscriptionAsync(WebhookSubscription subscription, CancellationToken ct = default)
    {
        _subscriptions[subscription.Id] = subscription;
        return Task.CompletedTask;
    }

    public Task DeleteSubscriptionAsync(Guid id, CancellationToken ct = default)
    {
        _subscriptions.TryRemove(id, out _);

        // Remove associated deliveries
        var deliveryIds = _deliveries.Values
            .Where(d => d.SubscriptionId == id)
            .Select(d => d.Id)
            .ToList();

        foreach (var deliveryId in deliveryIds)
        {
            _deliveries.TryRemove(deliveryId, out _);
        }

        return Task.CompletedTask;
    }

    #endregion

    #region Deliveries

    public Task<Guid> CreateDeliveryAsync(WebhookDelivery delivery, CancellationToken ct = default)
    {
        _deliveries[delivery.Id] = delivery;
        return Task.FromResult(delivery.Id);
    }

    public Task<WebhookDelivery?> GetDeliveryAsync(Guid id, CancellationToken ct = default)
    {
        _deliveries.TryGetValue(id, out var delivery);
        return Task.FromResult(delivery);
    }

    public Task<IReadOnlyList<WebhookDelivery>> GetDeliveriesAsync(Guid subscriptionId, int limit = 50, CancellationToken ct = default)
    {
        var deliveries = _deliveries.Values
            .Where(d => d.SubscriptionId == subscriptionId)
            .OrderByDescending(d => d.CreatedAt)
            .Take(limit)
            .ToList();

        return Task.FromResult<IReadOnlyList<WebhookDelivery>>(deliveries);
    }

    public Task<IReadOnlyList<WebhookDelivery>> GetPendingDeliveriesAsync(int limit = 100, CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        var pending = _deliveries.Values
            .Where(d => !d.Success && d.NextRetryAt.HasValue && d.NextRetryAt.Value <= now)
            .OrderBy(d => d.NextRetryAt)
            .Take(limit)
            .ToList();

        return Task.FromResult<IReadOnlyList<WebhookDelivery>>(pending);
    }

    public Task UpdateDeliveryAsync(WebhookDelivery delivery, CancellationToken ct = default)
    {
        _deliveries[delivery.Id] = delivery;
        return Task.CompletedTask;
    }

    public Task<int> DeleteOldDeliveriesAsync(DateTime olderThan, CancellationToken ct = default)
    {
        var toDelete = _deliveries.Values
            .Where(d => d.CreatedAt < olderThan)
            .Select(d => d.Id)
            .ToList();

        foreach (var id in toDelete)
        {
            _deliveries.TryRemove(id, out _);
        }

        return Task.FromResult(toDelete.Count);
    }

    #endregion

    #region Helpers

    private static bool MatchesFilter(string? filter, string? value)
    {
        if (string.IsNullOrEmpty(filter))
            return true;

        if (string.IsNullOrEmpty(value))
            return false;

        // Convert wildcard pattern to regex
        var pattern = "^" + Regex.Escape(filter).Replace("\\*", ".*") + "$";
        return Regex.IsMatch(value, pattern, RegexOptions.IgnoreCase);
    }

    #endregion

    /// <summary>
    /// Clear all webhook data (for testing)
    /// </summary>
    public void Clear()
    {
        _subscriptions.Clear();
        _deliveries.Clear();
    }
}
