using System.Text.Json;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ZapJobs.Core;

namespace ZapJobs.Webhooks;

/// <summary>
/// Background service that processes pending webhook deliveries and retries failed ones
/// </summary>
public class WebhookDeliveryService : BackgroundService
{
    private readonly IWebhookStorage _storage;
    private readonly WebhookService _webhookService;
    private readonly ILogger<WebhookDeliveryService> _logger;
    private readonly WebhookOptions _options;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public WebhookDeliveryService(
        IWebhookStorage storage,
        WebhookService webhookService,
        ILogger<WebhookDeliveryService> logger,
        WebhookOptions? options = null)
    {
        _storage = storage;
        _webhookService = webhookService;
        _logger = logger;
        _options = options ?? new WebhookOptions();
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Webhook delivery service started");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessPendingDeliveriesAsync(stoppingToken);
                await CleanupOldDeliveriesAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in webhook delivery service");
            }

            await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
        }

        _logger.LogInformation("Webhook delivery service stopped");
    }

    private async Task ProcessPendingDeliveriesAsync(CancellationToken ct)
    {
        var pendingDeliveries = await _storage.GetPendingDeliveriesAsync(100, ct);

        if (pendingDeliveries.Count == 0)
            return;

        _logger.LogDebug("Processing {Count} pending webhook deliveries", pendingDeliveries.Count);

        foreach (var delivery in pendingDeliveries)
        {
            if (ct.IsCancellationRequested)
                break;

            try
            {
                await RetryDeliveryAsync(delivery, ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to retry delivery {DeliveryId}", delivery.Id);
            }
        }
    }

    private async Task RetryDeliveryAsync(WebhookDelivery originalDelivery, CancellationToken ct)
    {
        var subscription = await _storage.GetSubscriptionAsync(originalDelivery.SubscriptionId, ct);
        if (subscription == null)
        {
            _logger.LogWarning("Subscription {SubscriptionId} not found for delivery {DeliveryId}",
                originalDelivery.SubscriptionId, originalDelivery.Id);

            // Mark the delivery as abandoned
            originalDelivery.NextRetryAt = null;
            originalDelivery.ErrorMessage = "Subscription not found";
            await _storage.UpdateDeliveryAsync(originalDelivery, ct);
            return;
        }

        if (!subscription.IsEnabled)
        {
            _logger.LogDebug("Skipping retry for disabled subscription {SubscriptionId}", subscription.Id);
            originalDelivery.NextRetryAt = null;
            originalDelivery.ErrorMessage = "Subscription disabled";
            await _storage.UpdateDeliveryAsync(originalDelivery, ct);
            return;
        }

        // Parse the original payload
        WebhookPayload? payload;
        try
        {
            payload = JsonSerializer.Deserialize<WebhookPayload>(originalDelivery.PayloadJson, JsonOptions);
        }
        catch (JsonException)
        {
            _logger.LogWarning("Failed to parse payload for delivery {DeliveryId}", originalDelivery.Id);
            originalDelivery.NextRetryAt = null;
            originalDelivery.ErrorMessage = "Invalid payload";
            await _storage.UpdateDeliveryAsync(originalDelivery, ct);
            return;
        }

        if (payload == null)
        {
            originalDelivery.NextRetryAt = null;
            await _storage.UpdateDeliveryAsync(originalDelivery, ct);
            return;
        }

        // Clear retry time on original (we'll create a new delivery record)
        originalDelivery.NextRetryAt = null;
        await _storage.UpdateDeliveryAsync(originalDelivery, ct);

        // Create new delivery attempt
        var newDelivery = await _webhookService.DeliverAsync(
            subscription,
            originalDelivery.Event,
            payload,
            ct);

        newDelivery.AttemptNumber = originalDelivery.AttemptNumber + 1;
        await _storage.UpdateDeliveryAsync(newDelivery, ct);

        _logger.LogDebug("Retry attempt {Attempt} for delivery to {Url}: {Success}",
            newDelivery.AttemptNumber, subscription.Url, newDelivery.Success ? "success" : "failed");
    }

    private async Task CleanupOldDeliveriesAsync(CancellationToken ct)
    {
        var cutoff = DateTime.UtcNow.Subtract(_options.DeliveryRetention);
        var deleted = await _storage.DeleteOldDeliveriesAsync(cutoff, ct);

        if (deleted > 0)
        {
            _logger.LogInformation("Cleaned up {Count} old webhook deliveries", deleted);
        }
    }
}
