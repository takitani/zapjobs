using Microsoft.Extensions.Logging;
using ZapJobs.Core;
using ZapJobs.Core.Events;

namespace ZapJobs.Webhooks;

/// <summary>
/// Event handler that publishes job events to webhook subscribers
/// </summary>
public class WebhookEventHandler :
    IJobEventHandler<JobEnqueuedEvent>,
    IJobEventHandler<JobStartedEvent>,
    IJobEventHandler<JobCompletedEvent>,
    IJobEventHandler<JobFailedEvent>,
    IJobEventHandler<JobRetryingEvent>
{
    private readonly IWebhookService _webhookService;
    private readonly ILogger<WebhookEventHandler> _logger;

    public WebhookEventHandler(IWebhookService webhookService, ILogger<WebhookEventHandler> logger)
    {
        _webhookService = webhookService;
        _logger = logger;
    }

    public async Task HandleAsync(JobEnqueuedEvent @event, CancellationToken ct = default)
    {
        var payload = new WebhookPayload
        {
            Event = "JobEnqueued",
            Timestamp = @event.Timestamp,
            JobTypeId = @event.JobTypeId,
            RunId = @event.RunId,
            Queue = @event.Queue
        };

        await PublishSafelyAsync(WebhookEventType.JobEnqueued, payload, ct);
    }

    public async Task HandleAsync(JobStartedEvent @event, CancellationToken ct = default)
    {
        var payload = new WebhookPayload
        {
            Event = "JobStarted",
            Timestamp = @event.Timestamp,
            JobTypeId = @event.JobTypeId,
            RunId = @event.RunId,
            Queue = @event.Queue,
            AttemptNumber = @event.AttemptNumber
        };

        await PublishSafelyAsync(WebhookEventType.JobStarted, payload, ct);
    }

    public async Task HandleAsync(JobCompletedEvent @event, CancellationToken ct = default)
    {
        var payload = new WebhookPayload
        {
            Event = "JobCompleted",
            Timestamp = @event.Timestamp,
            JobTypeId = @event.JobTypeId,
            RunId = @event.RunId,
            Queue = @event.Queue,
            Status = "Completed",
            DurationMs = (int)@event.Duration.TotalMilliseconds,
            AttemptNumber = @event.AttemptNumber,
            ItemsProcessed = @event.ItemsProcessed,
            ItemsSucceeded = @event.ItemsSucceeded,
            ItemsFailed = @event.ItemsFailed
        };

        // Parse output if available
        if (!string.IsNullOrEmpty(@event.OutputJson))
        {
            try
            {
                payload.Output = System.Text.Json.JsonSerializer.Deserialize<object>(@event.OutputJson);
            }
            catch
            {
                // Ignore parse errors
            }
        }

        await PublishSafelyAsync(WebhookEventType.JobCompleted, payload, ct);
    }

    public async Task HandleAsync(JobFailedEvent @event, CancellationToken ct = default)
    {
        var payload = new WebhookPayload
        {
            Event = "JobFailed",
            Timestamp = @event.Timestamp,
            JobTypeId = @event.JobTypeId,
            RunId = @event.RunId,
            Queue = @event.Queue,
            Status = "Failed",
            ErrorMessage = @event.ErrorMessage,
            ErrorType = @event.ErrorType,
            AttemptNumber = @event.AttemptNumber,
            WillRetry = @event.WillRetry,
            MovedToDeadLetter = @event.MovedToDeadLetter,
            DurationMs = (int)@event.Duration.TotalMilliseconds
        };

        await PublishSafelyAsync(WebhookEventType.JobFailed, payload, ct);
    }

    public async Task HandleAsync(JobRetryingEvent @event, CancellationToken ct = default)
    {
        var payload = new WebhookPayload
        {
            Event = "JobRetrying",
            Timestamp = @event.Timestamp,
            JobTypeId = @event.JobTypeId,
            RunId = @event.RunId,
            Queue = @event.Queue,
            Status = "Retrying",
            ErrorMessage = @event.ErrorMessage,
            AttemptNumber = @event.FailedAttempt,
            MaxRetries = @event.MaxRetries,
            WillRetry = true,
            NextRetryAt = @event.RetryAt
        };

        await PublishSafelyAsync(WebhookEventType.JobRetrying, payload, ct);
    }

    private async Task PublishSafelyAsync(WebhookEventType eventType, WebhookPayload payload, CancellationToken ct)
    {
        try
        {
            await _webhookService.PublishEventAsync(eventType, payload, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to publish webhook event {EventType} for job {JobTypeId}",
                eventType, payload.JobTypeId);
        }
    }
}
