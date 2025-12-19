using Microsoft.Extensions.Logging;
using ZapJobs.Core.Events;
using ZapJobs.Core.History;

namespace ZapJobs.History;

/// <summary>
/// Event handler that persists job lifecycle events to the event store.
/// Bridges the existing IJobEvent system with the persistent event history.
/// </summary>
/// <remarks>
/// Register this handler to automatically persist all job events to the event store
/// for auditing, time-travel debugging, and analytics.
/// </remarks>
public class EventHistoryHandler :
    IJobEventHandler<JobStartedEvent>,
    IJobEventHandler<JobCompletedEvent>,
    IJobEventHandler<JobFailedEvent>,
    IJobEventHandler<JobRetryingEvent>,
    IJobEventHandler<JobEnqueuedEvent>
{
    private readonly IEventPublisher _publisher;
    private readonly ILogger<EventHistoryHandler> _logger;

    public EventHistoryHandler(IEventPublisher publisher, ILogger<EventHistoryHandler> logger)
    {
        _publisher = publisher;
        _logger = logger;
    }

    public async Task HandleAsync(JobStartedEvent @event, CancellationToken ct = default)
    {
        try
        {
            var payload = new JobStartedPayload(
                WorkerId: Environment.MachineName,
                AttemptNumber: @event.AttemptNumber,
                IsRetry: @event.AttemptNumber > 1,
                IsResuming: false, // Could be passed from event if available
                ResumeReason: null
            );

            await _publisher.PublishJobStartedAsync(@event.RunId, payload, ct);

            _logger.LogDebug(
                "Persisted JobStarted event for run {RunId}, attempt {Attempt}",
                @event.RunId, @event.AttemptNumber);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to persist JobStarted event for run {RunId}", @event.RunId);
        }
    }

    public async Task HandleAsync(JobCompletedEvent @event, CancellationToken ct = default)
    {
        try
        {
            var payload = new JobCompletedPayload(
                DurationMs: (long)@event.Duration.TotalMilliseconds,
                OutputJson: @event.OutputJson,
                SucceededCount: @event.ItemsSucceeded,
                FailedCount: @event.ItemsFailed,
                ItemsProcessed: @event.ItemsProcessed
            );

            await _publisher.PublishJobCompletedAsync(@event.RunId, payload, ct);

            _logger.LogDebug(
                "Persisted JobCompleted event for run {RunId}, duration {Duration}ms",
                @event.RunId, payload.DurationMs);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to persist JobCompleted event for run {RunId}", @event.RunId);
        }
    }

    public async Task HandleAsync(JobFailedEvent @event, CancellationToken ct = default)
    {
        try
        {
            var payload = new JobFailedPayload(
                ErrorMessage: @event.ErrorMessage,
                ErrorType: @event.ErrorType,
                StackTrace: @event.StackTrace,
                AttemptNumber: @event.AttemptNumber,
                MaxRetries: 0, // Not available in current event
                WillRetry: @event.WillRetry,
                NextRetryAt: null, // Not available in current event
                MovedToDeadLetter: @event.MovedToDeadLetter
            );

            await _publisher.PublishJobFailedAsync(@event.RunId, payload, ct);

            _logger.LogDebug(
                "Persisted JobFailed event for run {RunId}, will retry: {WillRetry}",
                @event.RunId, @event.WillRetry);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to persist JobFailed event for run {RunId}", @event.RunId);
        }
    }

    public async Task HandleAsync(JobRetryingEvent @event, CancellationToken ct = default)
    {
        try
        {
            var payload = new JobRetryingPayload(
                FailedAttempt: @event.FailedAttempt,
                NextAttempt: @event.NextAttempt,
                MaxRetries: @event.MaxRetries,
                DelayMs: (long)@event.Delay.TotalMilliseconds,
                RetryAt: @event.RetryAt,
                ErrorMessage: @event.ErrorMessage
            );

            await _publisher.PublishJobRetryingAsync(@event.RunId, payload, ct);

            _logger.LogDebug(
                "Persisted JobRetrying event for run {RunId}, next attempt {NextAttempt}",
                @event.RunId, @event.NextAttempt);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to persist JobRetrying event for run {RunId}", @event.RunId);
        }
    }

    public async Task HandleAsync(JobEnqueuedEvent @event, CancellationToken ct = default)
    {
        try
        {
            var payload = new JobEnqueuedPayload(
                JobTypeId: @event.JobTypeId,
                Queue: @event.Queue,
                InputJson: @event.InputJson,
                Priority: null // Priority not available in current event
            );

            await _publisher.PublishJobEnqueuedAsync(@event.RunId, payload, ct);

            _logger.LogDebug(
                "Persisted JobEnqueued event for run {RunId}, job type {JobTypeId}",
                @event.RunId, @event.JobTypeId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to persist JobEnqueued event for run {RunId}", @event.RunId);
        }
    }
}
