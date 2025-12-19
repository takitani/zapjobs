using System.Diagnostics;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using ZapJobs.Core;

namespace ZapJobs.Webhooks;

/// <summary>
/// Service for managing webhooks and delivering events
/// </summary>
public class WebhookService : IWebhookService
{
    private readonly IWebhookStorage _storage;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<WebhookService> _logger;
    private readonly WebhookOptions _options;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    public WebhookService(
        IWebhookStorage storage,
        IHttpClientFactory httpClientFactory,
        ILogger<WebhookService> logger,
        WebhookOptions? options = null)
    {
        _storage = storage;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
        _options = options ?? new WebhookOptions();
    }

    #region Subscription Management

    public async Task<Guid> CreateSubscriptionAsync(WebhookSubscription subscription, CancellationToken ct = default)
    {
        subscription.Id = Guid.NewGuid();
        subscription.CreatedAt = DateTime.UtcNow;
        subscription.UpdatedAt = DateTime.UtcNow;

        var id = await _storage.CreateSubscriptionAsync(subscription, ct);
        _logger.LogInformation("Created webhook subscription {SubscriptionId} for {Url}", id, subscription.Url);
        return id;
    }

    public Task<WebhookSubscription?> GetSubscriptionAsync(Guid id, CancellationToken ct = default)
        => _storage.GetSubscriptionAsync(id, ct);

    public Task<IReadOnlyList<WebhookSubscription>> GetSubscriptionsAsync(bool enabledOnly = false, CancellationToken ct = default)
        => _storage.GetSubscriptionsAsync(enabledOnly, ct);

    public async Task UpdateSubscriptionAsync(WebhookSubscription subscription, CancellationToken ct = default)
    {
        subscription.UpdatedAt = DateTime.UtcNow;
        await _storage.UpdateSubscriptionAsync(subscription, ct);
        _logger.LogInformation("Updated webhook subscription {SubscriptionId}", subscription.Id);
    }

    public async Task DeleteSubscriptionAsync(Guid id, CancellationToken ct = default)
    {
        await _storage.DeleteSubscriptionAsync(id, ct);
        _logger.LogInformation("Deleted webhook subscription {SubscriptionId}", id);
    }

    public async Task<WebhookDelivery> TestSubscriptionAsync(Guid id, CancellationToken ct = default)
    {
        var subscription = await _storage.GetSubscriptionAsync(id, ct)
            ?? throw new InvalidOperationException($"Subscription {id} not found");

        var testPayload = new WebhookPayload
        {
            Event = "Test",
            Timestamp = DateTimeOffset.UtcNow,
            JobTypeId = "test-job",
            RunId = Guid.NewGuid(),
            Queue = "default",
            Status = "Completed"
        };

        return await DeliverAsync(subscription, WebhookEventType.JobCompleted, testPayload, ct);
    }

    #endregion

    #region Event Publishing

    public async Task PublishEventAsync(WebhookEventType eventType, WebhookPayload payload, CancellationToken ct = default)
    {
        var subscriptions = await _storage.GetMatchingSubscriptionsAsync(
            eventType, payload.JobTypeId, payload.Queue, ct);

        if (subscriptions.Count == 0)
        {
            _logger.LogDebug("No webhook subscriptions match event {EventType} for job {JobTypeId}",
                eventType, payload.JobTypeId);
            return;
        }

        _logger.LogDebug("Publishing {EventType} to {Count} webhook subscriptions",
            eventType, subscriptions.Count);

        // Deliver to each matching subscription (fire-and-forget, errors logged)
        foreach (var subscription in subscriptions)
        {
            try
            {
                await DeliverAsync(subscription, eventType, payload, ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to deliver webhook to {Url} for subscription {SubscriptionId}",
                    subscription.Url, subscription.Id);
            }
        }
    }

    #endregion

    #region Delivery

    public Task<IReadOnlyList<WebhookDelivery>> GetDeliveriesAsync(Guid subscriptionId, int limit = 50, CancellationToken ct = default)
        => _storage.GetDeliveriesAsync(subscriptionId, limit, ct);

    public async Task<WebhookDelivery> RetryDeliveryAsync(Guid deliveryId, CancellationToken ct = default)
    {
        var delivery = await _storage.GetDeliveryAsync(deliveryId, ct)
            ?? throw new InvalidOperationException($"Delivery {deliveryId} not found");

        var subscription = await _storage.GetSubscriptionAsync(delivery.SubscriptionId, ct)
            ?? throw new InvalidOperationException($"Subscription {delivery.SubscriptionId} not found");

        // Parse the original payload
        var payload = JsonSerializer.Deserialize<WebhookPayload>(delivery.PayloadJson, JsonOptions)
            ?? throw new InvalidOperationException("Failed to deserialize delivery payload");

        return await DeliverAsync(subscription, delivery.Event, payload, ct);
    }

    internal async Task<WebhookDelivery> DeliverAsync(
        WebhookSubscription subscription,
        WebhookEventType eventType,
        WebhookPayload payload,
        CancellationToken ct)
    {
        var payloadJson = JsonSerializer.Serialize(payload, JsonOptions);

        var delivery = new WebhookDelivery
        {
            SubscriptionId = subscription.Id,
            Event = eventType,
            PayloadJson = payloadJson,
            AttemptNumber = 1,
            CreatedAt = DateTime.UtcNow
        };

        var stopwatch = Stopwatch.StartNew();

        try
        {
            using var client = _httpClientFactory.CreateClient("ZapJobsWebhook");
            client.Timeout = _options.RequestTimeout;

            using var request = new HttpRequestMessage(HttpMethod.Post, subscription.Url);
            request.Content = new StringContent(payloadJson, Encoding.UTF8, "application/json");

            // Add standard headers
            request.Headers.Add("X-ZapJobs-Event", eventType.ToString());
            request.Headers.Add("X-ZapJobs-Delivery", delivery.Id.ToString());
            request.Headers.Add("X-ZapJobs-Timestamp", payload.Timestamp.ToUnixTimeSeconds().ToString());

            // Add HMAC signature if secret is configured
            if (!string.IsNullOrEmpty(subscription.Secret))
            {
                var signature = ComputeSignature(payloadJson, subscription.Secret);
                request.Headers.Add("X-ZapJobs-Signature", signature);
            }

            // Add custom headers
            if (!string.IsNullOrEmpty(subscription.HeadersJson))
            {
                try
                {
                    var customHeaders = JsonSerializer.Deserialize<Dictionary<string, string>>(subscription.HeadersJson);
                    if (customHeaders != null)
                    {
                        foreach (var (key, value) in customHeaders)
                        {
                            request.Headers.TryAddWithoutValidation(key, value);
                        }
                    }
                }
                catch (JsonException ex)
                {
                    _logger.LogWarning(ex, "Failed to parse custom headers for subscription {SubscriptionId}",
                        subscription.Id);
                }
            }

            using var response = await client.SendAsync(request, ct);

            stopwatch.Stop();
            delivery.DurationMs = (int)stopwatch.ElapsedMilliseconds;
            delivery.StatusCode = (int)response.StatusCode;
            delivery.Success = response.IsSuccessStatusCode;

            // Read response body (truncated)
            var responseBody = await response.Content.ReadAsStringAsync(ct);
            delivery.ResponseBody = responseBody.Length > _options.MaxResponseBodyLength
                ? responseBody[.._options.MaxResponseBodyLength] + "..."
                : responseBody;

            if (delivery.Success)
            {
                _logger.LogDebug("Webhook delivery {DeliveryId} succeeded with status {StatusCode} in {Duration}ms",
                    delivery.Id, delivery.StatusCode, delivery.DurationMs);

                // Update subscription success timestamp
                subscription.LastSuccessAt = DateTime.UtcNow;
                subscription.FailureCount = 0;
                await _storage.UpdateSubscriptionAsync(subscription, ct);
            }
            else
            {
                _logger.LogWarning("Webhook delivery {DeliveryId} failed with status {StatusCode}: {Response}",
                    delivery.Id, delivery.StatusCode, delivery.ResponseBody);

                delivery.ErrorMessage = $"HTTP {delivery.StatusCode}";
                await HandleDeliveryFailureAsync(subscription, delivery, ct);
            }
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            delivery.DurationMs = (int)stopwatch.ElapsedMilliseconds;
            delivery.Success = false;
            delivery.StatusCode = 0;
            delivery.ErrorMessage = ex.Message;

            _logger.LogWarning(ex, "Webhook delivery {DeliveryId} failed with exception", delivery.Id);

            await HandleDeliveryFailureAsync(subscription, delivery, ct);
        }

        await _storage.CreateDeliveryAsync(delivery, ct);
        return delivery;
    }

    private async Task HandleDeliveryFailureAsync(
        WebhookSubscription subscription,
        WebhookDelivery delivery,
        CancellationToken ct)
    {
        subscription.LastFailureAt = DateTime.UtcNow;
        subscription.FailureCount++;

        // Schedule retry if under max attempts
        if (delivery.AttemptNumber < _options.MaxRetryAttempts)
        {
            var delay = CalculateRetryDelay(delivery.AttemptNumber);
            delivery.NextRetryAt = DateTime.UtcNow.Add(delay);
        }

        // Disable subscription if too many consecutive failures
        if (subscription.FailureCount >= _options.MaxConsecutiveFailures)
        {
            subscription.IsEnabled = false;
            _logger.LogWarning(
                "Webhook subscription {SubscriptionId} disabled after {FailureCount} consecutive failures",
                subscription.Id, subscription.FailureCount);
        }

        await _storage.UpdateSubscriptionAsync(subscription, ct);
    }

    private TimeSpan CalculateRetryDelay(int attemptNumber)
    {
        // Exponential backoff: 10s, 30s, 1m, 5m, 15m
        var delays = new[] { 10, 30, 60, 300, 900 };
        var index = Math.Min(attemptNumber - 1, delays.Length - 1);
        return TimeSpan.FromSeconds(delays[index]);
    }

    private static string ComputeSignature(string payload, string secret)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(payload));
        return $"sha256={Convert.ToHexString(hash).ToLowerInvariant()}";
    }

    #endregion

    /// <summary>
    /// Check if a job type matches a filter pattern (supports wildcards)
    /// </summary>
    internal static bool MatchesFilter(string? filter, string? value)
    {
        if (string.IsNullOrEmpty(filter))
            return true;

        if (string.IsNullOrEmpty(value))
            return false;

        // Convert wildcard pattern to regex
        var pattern = "^" + Regex.Escape(filter).Replace("\\*", ".*") + "$";
        return Regex.IsMatch(value, pattern, RegexOptions.IgnoreCase);
    }
}

/// <summary>
/// Options for webhook delivery
/// </summary>
public class WebhookOptions
{
    /// <summary>
    /// Timeout for webhook HTTP requests
    /// </summary>
    public TimeSpan RequestTimeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Maximum number of retry attempts for failed deliveries
    /// </summary>
    public int MaxRetryAttempts { get; set; } = 5;

    /// <summary>
    /// Maximum consecutive failures before disabling a subscription
    /// </summary>
    public int MaxConsecutiveFailures { get; set; } = 10;

    /// <summary>
    /// Maximum response body length to store
    /// </summary>
    public int MaxResponseBodyLength { get; set; } = 1000;

    /// <summary>
    /// How long to keep delivery records
    /// </summary>
    public TimeSpan DeliveryRetention { get; set; } = TimeSpan.FromDays(7);
}
