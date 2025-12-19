using System.Text.Json;
using ZapJobs.Core.History;

namespace ZapJobs.History;

/// <summary>
/// Default implementation of IEventPublisher that provides convenient
/// methods for publishing typed events to the event store.
/// </summary>
public class EventPublisher : IEventPublisher
{
    private readonly IEventStore _eventStore;
    private readonly JsonSerializerOptions _jsonOptions;

    // Context for correlation and tracing
    private string? _correlationId;
    private string? _causationId;
    private string? _actor;
    private string? _source;
    private Guid? _workflowId;

    public EventPublisher(IEventStore eventStore)
    {
        _eventStore = eventStore;
        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false
        };
    }

    #region Generic Publishing

    public async Task<Guid> PublishAsync<TPayload>(
        Guid runId,
        string eventType,
        TPayload payload,
        EventPublishOptions? options = null,
        CancellationToken ct = default) where TPayload : class
    {
        var @event = CreateEvent(runId, eventType, options);
        @event.PayloadJson = JsonSerializer.Serialize(payload, _jsonOptions);

        await _eventStore.AppendAsync(@event, ct);
        return @event.Id;
    }

    public async Task<Guid> PublishAsync(
        Guid runId,
        string eventType,
        EventPublishOptions? options = null,
        CancellationToken ct = default)
    {
        var @event = CreateEvent(runId, eventType, options);
        await _eventStore.AppendAsync(@event, ct);
        return @event.Id;
    }

    public Task PublishAsync(JobEvent @event, CancellationToken ct = default)
    {
        // Apply context if not set
        @event.CorrelationId ??= _correlationId;
        @event.CausationId ??= _causationId;
        @event.Actor ??= _actor;
        @event.Source ??= _source;
        @event.WorkflowId ??= _workflowId;

        return _eventStore.AppendAsync(@event, ct);
    }

    public Task PublishBatchAsync(IEnumerable<JobEvent> events, CancellationToken ct = default)
    {
        var eventList = events.ToList();

        // Apply context to all events
        foreach (var @event in eventList)
        {
            @event.CorrelationId ??= _correlationId;
            @event.CausationId ??= _causationId;
            @event.Actor ??= _actor;
            @event.Source ??= _source;
            @event.WorkflowId ??= _workflowId;
        }

        return _eventStore.AppendBatchAsync(eventList, ct);
    }

    #endregion

    #region Job Lifecycle Events

    public Task<Guid> PublishJobScheduledAsync(
        Guid runId,
        JobScheduledPayload payload,
        CancellationToken ct = default)
    {
        return PublishAsync(runId, EventTypes.JobScheduled, payload,
            new EventPublishOptions { Category = EventCategories.Job }, ct);
    }

    public Task<Guid> PublishJobEnqueuedAsync(
        Guid runId,
        JobEnqueuedPayload payload,
        CancellationToken ct = default)
    {
        return PublishAsync(runId, EventTypes.JobEnqueued, payload,
            new EventPublishOptions { Category = EventCategories.Job }, ct);
    }

    public Task<Guid> PublishJobStartedAsync(
        Guid runId,
        JobStartedPayload payload,
        CancellationToken ct = default)
    {
        return PublishAsync(runId, EventTypes.JobStarted, payload,
            new EventPublishOptions { Category = EventCategories.Job }, ct);
    }

    public Task<Guid> PublishJobCompletedAsync(
        Guid runId,
        JobCompletedPayload payload,
        CancellationToken ct = default)
    {
        return PublishAsync(runId, EventTypes.JobCompleted, payload,
            new EventPublishOptions
            {
                Category = EventCategories.Job,
                IsSuccess = true,
                DurationMs = payload.DurationMs
            }, ct);
    }

    public Task<Guid> PublishJobFailedAsync(
        Guid runId,
        JobFailedPayload payload,
        CancellationToken ct = default)
    {
        return PublishAsync(runId, EventTypes.JobFailed, payload,
            new EventPublishOptions
            {
                Category = EventCategories.Job,
                IsSuccess = false
            }, ct);
    }

    public Task<Guid> PublishJobRetryingAsync(
        Guid runId,
        JobRetryingPayload payload,
        CancellationToken ct = default)
    {
        return PublishAsync(runId, EventTypes.JobRetrying, payload,
            new EventPublishOptions
            {
                Category = EventCategories.Job,
                DurationMs = payload.DelayMs
            }, ct);
    }

    #endregion

    #region Progress Events

    public Task<Guid> PublishProgressAsync(
        Guid runId,
        int current,
        int total,
        string? message = null,
        CancellationToken ct = default)
    {
        var percentage = total > 0 ? (double)current / total * 100 : 0;
        var payload = new ProgressUpdatedPayload(
            current,
            total,
            percentage,
            message,
            null // EstimatedRemainingMs - could be calculated if we had rate info
        );

        return PublishAsync(runId, EventTypes.ProgressUpdated, payload,
            new EventPublishOptions { Category = EventCategories.Progress }, ct);
    }

    public Task<Guid> PublishItemProcessedAsync(
        Guid runId,
        string? itemId,
        int index,
        bool success,
        long processingTimeMs,
        string? errorMessage = null,
        CancellationToken ct = default)
    {
        var payload = new ItemProcessedPayload(
            itemId,
            index,
            success,
            errorMessage,
            processingTimeMs
        );

        return PublishAsync(runId, EventTypes.ItemProcessed, payload,
            new EventPublishOptions
            {
                Category = EventCategories.Progress,
                IsSuccess = success,
                DurationMs = processingTimeMs
            }, ct);
    }

    public Task<Guid> PublishMilestoneAsync(
        Guid runId,
        string milestoneName,
        int itemsProcessed,
        string? description = null,
        CancellationToken ct = default)
    {
        // Calculate elapsed time since first event would require querying
        // For now, we'll set it in the caller or leave it unset
        var payload = new MilestoneReachedPayload(
            milestoneName,
            itemsProcessed,
            0, // ElapsedMs would need to be provided
            description
        );

        return PublishAsync(runId, EventTypes.MilestoneReached, payload,
            new EventPublishOptions { Category = EventCategories.Progress }, ct);
    }

    #endregion

    #region State Events

    public Task<Guid> PublishCheckpointSavedAsync(
        Guid runId,
        string key,
        int sequenceNumber,
        int dataSizeBytes,
        bool wasCompressed,
        CancellationToken ct = default)
    {
        var payload = new CheckpointSavedPayload(
            key,
            sequenceNumber,
            dataSizeBytes,
            wasCompressed,
            null // Version
        );

        return PublishAsync(runId, EventTypes.CheckpointSaved, payload,
            new EventPublishOptions { Category = EventCategories.State }, ct);
    }

    public Task<Guid> PublishCheckpointRestoredAsync(
        Guid runId,
        string key,
        int sequenceNumber,
        DateTimeOffset createdAt,
        CancellationToken ct = default)
    {
        var payload = new CheckpointRestoredPayload(
            key,
            sequenceNumber,
            createdAt,
            null // ItemsProcessedBefore
        );

        return PublishAsync(runId, EventTypes.CheckpointRestored, payload,
            new EventPublishOptions { Category = EventCategories.State }, ct);
    }

    #endregion

    #region Custom Events

    public Task<Guid> PublishLogAsync(
        Guid runId,
        string message,
        EventSeverity severity = EventSeverity.Info,
        Dictionary<string, object?>? properties = null,
        CancellationToken ct = default)
    {
        var payload = new LogMessagePayload(message, severity, properties);

        return PublishAsync(runId, EventTypes.LogMessage, payload,
            new EventPublishOptions { Category = EventCategories.Custom }, ct);
    }

    public Task<Guid> PublishMetricAsync(
        Guid runId,
        string metricName,
        double value,
        string? unit = null,
        Dictionary<string, string>? tags = null,
        CancellationToken ct = default)
    {
        var payload = new MetricRecordedPayload(metricName, value, unit, tags);

        return PublishAsync(runId, EventTypes.MetricRecorded, payload,
            new EventPublishOptions { Category = EventCategories.Custom }, ct);
    }

    public Task<Guid> PublishBusinessEventAsync(
        Guid runId,
        string eventName,
        string? entityType = null,
        string? entityId = null,
        Dictionary<string, object?>? data = null,
        CancellationToken ct = default)
    {
        var payload = new BusinessEventPayload(eventName, entityType, entityId, data);

        return PublishAsync(runId, EventTypes.BusinessEvent, payload,
            new EventPublishOptions { Category = EventCategories.Custom }, ct);
    }

    #endregion

    #region Context Management

    public void SetCorrelationId(string correlationId)
    {
        _correlationId = correlationId;
    }

    public void SetCausationId(string causationId)
    {
        _causationId = causationId;
    }

    public void SetActor(string actor)
    {
        _actor = actor;
    }

    public void SetSource(string source)
    {
        _source = source;
    }

    public void SetWorkflowId(Guid workflowId)
    {
        _workflowId = workflowId;
    }

    public void ClearContext()
    {
        _correlationId = null;
        _causationId = null;
        _actor = null;
        _source = null;
        _workflowId = null;
    }

    #endregion

    #region Private Helpers

    private JobEvent CreateEvent(Guid runId, string eventType, EventPublishOptions? options)
    {
        var @event = new JobEvent
        {
            Id = Guid.NewGuid(),
            RunId = runId,
            EventType = eventType,
            Category = options?.Category ?? InferCategory(eventType),
            Timestamp = DateTimeOffset.UtcNow,
            PayloadJson = "{}",
            Version = options?.Version ?? 1,
            DurationMs = options?.DurationMs,
            IsSuccess = options?.IsSuccess,
            Tags = options?.Tags,
            CorrelationId = options?.CorrelationId ?? _correlationId,
            CausationId = options?.CausationId ?? _causationId,
            Actor = options?.Actor ?? _actor,
            Source = options?.Source ?? _source,
            WorkflowId = options?.WorkflowId ?? _workflowId
        };

        if (options?.Metadata != null)
        {
            @event.MetadataJson = JsonSerializer.Serialize(options.Metadata, _jsonOptions);
        }

        return @event;
    }

    private static string InferCategory(string eventType)
    {
        if (eventType.StartsWith("job.")) return EventCategories.Job;
        if (eventType.StartsWith("activity.")) return EventCategories.Activity;
        if (eventType.StartsWith("workflow.")) return EventCategories.Workflow;
        if (eventType.StartsWith("saga.")) return EventCategories.Workflow;
        if (eventType.StartsWith("state.")) return EventCategories.State;
        if (eventType.StartsWith("progress.")) return EventCategories.Progress;
        if (eventType.StartsWith("system.")) return EventCategories.System;
        if (eventType.StartsWith("custom.")) return EventCategories.Custom;
        return EventCategories.Custom;
    }

    #endregion
}

/// <summary>
/// Factory for creating scoped EventPublisher instances with pre-set context.
/// </summary>
public class EventPublisherFactory
{
    private readonly IEventStore _eventStore;

    public EventPublisherFactory(IEventStore eventStore)
    {
        _eventStore = eventStore;
    }

    /// <summary>
    /// Create a new EventPublisher instance.
    /// </summary>
    public IEventPublisher Create()
    {
        return new EventPublisher(_eventStore);
    }

    /// <summary>
    /// Create a new EventPublisher instance with pre-set context.
    /// </summary>
    public IEventPublisher Create(
        string? correlationId = null,
        string? causationId = null,
        string? actor = null,
        string? source = null,
        Guid? workflowId = null)
    {
        var publisher = new EventPublisher(_eventStore);

        if (correlationId != null) publisher.SetCorrelationId(correlationId);
        if (causationId != null) publisher.SetCausationId(causationId);
        if (actor != null) publisher.SetActor(actor);
        if (source != null) publisher.SetSource(source);
        if (workflowId.HasValue) publisher.SetWorkflowId(workflowId.Value);

        return publisher;
    }

    /// <summary>
    /// Create a new EventPublisher for a specific workflow.
    /// </summary>
    public IEventPublisher CreateForWorkflow(Guid workflowId, string? correlationId = null)
    {
        var publisher = new EventPublisher(_eventStore);
        publisher.SetWorkflowId(workflowId);

        if (correlationId != null)
            publisher.SetCorrelationId(correlationId);
        else
            publisher.SetCorrelationId(workflowId.ToString());

        return publisher;
    }

    /// <summary>
    /// Create a new EventPublisher for a specific worker.
    /// </summary>
    public IEventPublisher CreateForWorker(string workerId, string? source = null)
    {
        var publisher = new EventPublisher(_eventStore);
        publisher.SetActor(workerId);
        publisher.SetSource(source ?? Environment.MachineName);
        return publisher;
    }
}
