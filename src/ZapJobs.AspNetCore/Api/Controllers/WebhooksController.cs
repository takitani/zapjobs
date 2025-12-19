using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using ZapJobs.AspNetCore.Api.Dtos;
using ZapJobs.Core;

namespace ZapJobs.AspNetCore.Api.Controllers;

/// <summary>
/// API endpoints for managing webhooks
/// </summary>
/// <remarks>
/// Webhooks allow external systems to be notified when job events occur.
/// Supports HMAC-SHA256 signing, custom headers, and wildcard filters for job types and queues.
/// </remarks>
[ApiController]
[Route("api/v1/webhooks")]
[Produces("application/json")]
[Authorize]
public class WebhooksController : ControllerBase
{
    private readonly IWebhookService _webhookService;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public WebhooksController(IWebhookService webhookService)
    {
        _webhookService = webhookService;
    }

    /// <summary>
    /// List all webhooks
    /// </summary>
    /// <param name="enabledOnly">Filter to only enabled webhooks</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>List of webhooks</returns>
    /// <response code="200">List of webhooks</response>
    [HttpGet]
    [ProducesResponseType(typeof(IEnumerable<WebhookDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> List(
        [FromQuery] bool enabledOnly = false,
        CancellationToken ct = default)
    {
        var subscriptions = await _webhookService.GetSubscriptionsAsync(enabledOnly, ct);
        var dtos = subscriptions.Select(MapToDto).ToList();
        return Ok(dtos);
    }

    /// <summary>
    /// Get a specific webhook
    /// </summary>
    /// <param name="id">The webhook ID</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>The webhook details</returns>
    /// <response code="200">Webhook found</response>
    /// <response code="404">Webhook not found</response>
    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(WebhookDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct = default)
    {
        var subscription = await _webhookService.GetSubscriptionAsync(id, ct);
        if (subscription == null)
        {
            return NotFound(new ErrorResponse
            {
                Error = $"Webhook {id} not found",
                Code = "NOT_FOUND"
            });
        }

        return Ok(MapToDto(subscription));
    }

    /// <summary>
    /// Create a new webhook
    /// </summary>
    /// <param name="request">The webhook configuration</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>The created webhook</returns>
    /// <response code="201">Webhook created</response>
    /// <response code="400">Invalid request</response>
    [HttpPost]
    [ProducesResponseType(typeof(WebhookDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Create(
        [FromBody] CreateWebhookRequest request,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return BadRequest(new ErrorResponse
            {
                Error = "Name is required",
                Code = "INVALID_REQUEST"
            });
        }

        if (string.IsNullOrWhiteSpace(request.Url))
        {
            return BadRequest(new ErrorResponse
            {
                Error = "URL is required",
                Code = "INVALID_REQUEST"
            });
        }

        if (!Uri.TryCreate(request.Url, UriKind.Absolute, out var uri) ||
            (uri.Scheme != "http" && uri.Scheme != "https"))
        {
            return BadRequest(new ErrorResponse
            {
                Error = "URL must be a valid HTTP or HTTPS URL",
                Code = "INVALID_REQUEST"
            });
        }

        if (request.Events == null || request.Events.Length == 0)
        {
            return BadRequest(new ErrorResponse
            {
                Error = "At least one event is required",
                Code = "INVALID_REQUEST"
            });
        }

        var events = ParseEventTypes(request.Events);
        if (events == WebhookEventType.None)
        {
            return BadRequest(new ErrorResponse
            {
                Error = "Invalid event types. Valid values: JobStarted, JobCompleted, JobFailed, JobRetrying, JobCancelled, JobEnqueued, AllJobEvents, All",
                Code = "INVALID_REQUEST"
            });
        }

        var subscription = new WebhookSubscription
        {
            Name = request.Name,
            Url = request.Url,
            Events = events,
            JobTypeFilter = request.JobTypeFilter,
            QueueFilter = request.QueueFilter,
            Secret = request.Secret,
            HeadersJson = request.Headers != null ? JsonSerializer.Serialize(request.Headers, JsonOptions) : null,
            IsEnabled = request.IsEnabled
        };

        await _webhookService.CreateSubscriptionAsync(subscription, ct);
        var created = await _webhookService.GetSubscriptionAsync(subscription.Id, ct);

        return CreatedAtAction(nameof(Get), new { id = subscription.Id }, MapToDto(created!));
    }

    /// <summary>
    /// Update an existing webhook
    /// </summary>
    /// <param name="id">The webhook ID</param>
    /// <param name="request">The fields to update</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>The updated webhook</returns>
    /// <response code="200">Webhook updated</response>
    /// <response code="404">Webhook not found</response>
    /// <response code="400">Invalid request</response>
    [HttpPut("{id:guid}")]
    [ProducesResponseType(typeof(WebhookDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Update(
        Guid id,
        [FromBody] UpdateWebhookRequest request,
        CancellationToken ct = default)
    {
        var subscription = await _webhookService.GetSubscriptionAsync(id, ct);
        if (subscription == null)
        {
            return NotFound(new ErrorResponse
            {
                Error = $"Webhook {id} not found",
                Code = "NOT_FOUND"
            });
        }

        if (request.Name != null)
            subscription.Name = request.Name;

        if (request.Url != null)
        {
            if (!Uri.TryCreate(request.Url, UriKind.Absolute, out var uri) ||
                (uri.Scheme != "http" && uri.Scheme != "https"))
            {
                return BadRequest(new ErrorResponse
                {
                    Error = "URL must be a valid HTTP or HTTPS URL",
                    Code = "INVALID_REQUEST"
                });
            }
            subscription.Url = request.Url;
        }

        if (request.Events != null)
        {
            var events = ParseEventTypes(request.Events);
            if (events == WebhookEventType.None)
            {
                return BadRequest(new ErrorResponse
                {
                    Error = "Invalid event types",
                    Code = "INVALID_REQUEST"
                });
            }
            subscription.Events = events;
        }

        if (request.JobTypeFilter != null)
            subscription.JobTypeFilter = request.JobTypeFilter;

        if (request.QueueFilter != null)
            subscription.QueueFilter = request.QueueFilter;

        if (request.Secret != null)
            subscription.Secret = request.Secret;

        if (request.Headers != null)
            subscription.HeadersJson = JsonSerializer.Serialize(request.Headers, JsonOptions);

        if (request.IsEnabled.HasValue)
            subscription.IsEnabled = request.IsEnabled.Value;

        await _webhookService.UpdateSubscriptionAsync(subscription, ct);

        var updated = await _webhookService.GetSubscriptionAsync(id, ct);
        return Ok(MapToDto(updated!));
    }

    /// <summary>
    /// Delete a webhook
    /// </summary>
    /// <param name="id">The webhook ID</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>No content on success</returns>
    /// <response code="204">Webhook deleted</response>
    /// <response code="404">Webhook not found</response>
    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct = default)
    {
        var subscription = await _webhookService.GetSubscriptionAsync(id, ct);
        if (subscription == null)
        {
            return NotFound(new ErrorResponse
            {
                Error = $"Webhook {id} not found",
                Code = "NOT_FOUND"
            });
        }

        await _webhookService.DeleteSubscriptionAsync(id, ct);
        return NoContent();
    }

    /// <summary>
    /// Test a webhook by sending a test payload
    /// </summary>
    /// <param name="id">The webhook ID</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>The test delivery result</returns>
    /// <response code="200">Test delivery completed</response>
    /// <response code="404">Webhook not found</response>
    [HttpPost("{id:guid}/test")]
    [ProducesResponseType(typeof(WebhookDeliveryDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Test(Guid id, CancellationToken ct = default)
    {
        try
        {
            var delivery = await _webhookService.TestSubscriptionAsync(id, ct);
            return Ok(MapDeliveryToDto(delivery));
        }
        catch (InvalidOperationException ex)
        {
            return NotFound(new ErrorResponse
            {
                Error = ex.Message,
                Code = "NOT_FOUND"
            });
        }
    }

    /// <summary>
    /// Get delivery history for a webhook
    /// </summary>
    /// <param name="id">The webhook ID</param>
    /// <param name="limit">Maximum number of deliveries to return (default: 50)</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>List of deliveries</returns>
    /// <response code="200">List of deliveries</response>
    /// <response code="404">Webhook not found</response>
    [HttpGet("{id:guid}/deliveries")]
    [ProducesResponseType(typeof(IEnumerable<WebhookDeliveryDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetDeliveries(
        Guid id,
        [FromQuery] int limit = 50,
        CancellationToken ct = default)
    {
        var subscription = await _webhookService.GetSubscriptionAsync(id, ct);
        if (subscription == null)
        {
            return NotFound(new ErrorResponse
            {
                Error = $"Webhook {id} not found",
                Code = "NOT_FOUND"
            });
        }

        var deliveries = await _webhookService.GetDeliveriesAsync(id, limit, ct);
        var dtos = deliveries.Select(MapDeliveryToDto).ToList();
        return Ok(dtos);
    }

    /// <summary>
    /// Retry a failed delivery
    /// </summary>
    /// <param name="id">The webhook ID</param>
    /// <param name="deliveryId">The delivery ID to retry</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>The new delivery result</returns>
    /// <response code="200">Retry completed</response>
    /// <response code="404">Webhook or delivery not found</response>
    [HttpPost("{id:guid}/deliveries/{deliveryId:guid}/retry")]
    [ProducesResponseType(typeof(WebhookDeliveryDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> RetryDelivery(
        Guid id,
        Guid deliveryId,
        CancellationToken ct = default)
    {
        try
        {
            var delivery = await _webhookService.RetryDeliveryAsync(deliveryId, ct);
            return Ok(MapDeliveryToDto(delivery));
        }
        catch (InvalidOperationException ex)
        {
            return NotFound(new ErrorResponse
            {
                Error = ex.Message,
                Code = "NOT_FOUND"
            });
        }
    }

    #region Helpers

    private static WebhookEventType ParseEventTypes(string[] events)
    {
        var result = WebhookEventType.None;
        foreach (var e in events)
        {
            if (Enum.TryParse<WebhookEventType>(e, ignoreCase: true, out var eventType))
            {
                result |= eventType;
            }
        }
        return result;
    }

    private static string[] GetEventTypeNames(WebhookEventType events)
    {
        var names = new List<string>();

        if (events.HasFlag(WebhookEventType.JobStarted))
            names.Add("JobStarted");
        if (events.HasFlag(WebhookEventType.JobCompleted))
            names.Add("JobCompleted");
        if (events.HasFlag(WebhookEventType.JobFailed))
            names.Add("JobFailed");
        if (events.HasFlag(WebhookEventType.JobRetrying))
            names.Add("JobRetrying");
        if (events.HasFlag(WebhookEventType.JobCancelled))
            names.Add("JobCancelled");
        if (events.HasFlag(WebhookEventType.JobEnqueued))
            names.Add("JobEnqueued");

        return names.ToArray();
    }

    private static WebhookDto MapToDto(WebhookSubscription subscription)
    {
        Dictionary<string, string>? headers = null;
        if (!string.IsNullOrEmpty(subscription.HeadersJson))
        {
            try
            {
                headers = JsonSerializer.Deserialize<Dictionary<string, string>>(subscription.HeadersJson);
            }
            catch
            {
                // Ignore parse errors
            }
        }

        return new WebhookDto
        {
            Id = subscription.Id,
            Name = subscription.Name,
            Url = subscription.Url,
            Events = GetEventTypeNames(subscription.Events),
            JobTypeFilter = subscription.JobTypeFilter,
            QueueFilter = subscription.QueueFilter,
            HasSecret = !string.IsNullOrEmpty(subscription.Secret),
            Headers = headers,
            IsEnabled = subscription.IsEnabled,
            CreatedAt = subscription.CreatedAt,
            UpdatedAt = subscription.UpdatedAt,
            LastSuccessAt = subscription.LastSuccessAt,
            LastFailureAt = subscription.LastFailureAt,
            FailureCount = subscription.FailureCount
        };
    }

    private static WebhookDeliveryDto MapDeliveryToDto(WebhookDelivery delivery)
    {
        return new WebhookDeliveryDto
        {
            Id = delivery.Id,
            SubscriptionId = delivery.SubscriptionId,
            Event = delivery.Event.ToString(),
            StatusCode = delivery.StatusCode,
            ResponseBody = delivery.ResponseBody,
            ErrorMessage = delivery.ErrorMessage,
            AttemptNumber = delivery.AttemptNumber,
            Success = delivery.Success,
            DurationMs = delivery.DurationMs,
            CreatedAt = delivery.CreatedAt,
            NextRetryAt = delivery.NextRetryAt
        };
    }

    #endregion
}
