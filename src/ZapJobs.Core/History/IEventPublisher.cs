namespace ZapJobs.Core.History;

/// <summary>
/// Interface for publishing events to the event store.
/// Provides typed methods for common events and a generic method for custom events.
/// </summary>
public interface IEventPublisher
{
    #region Generic Publishing

    /// <summary>
    /// Publish an event with a typed payload.
    /// </summary>
    Task<Guid> PublishAsync<TPayload>(
        Guid runId,
        string eventType,
        TPayload payload,
        EventPublishOptions? options = null,
        CancellationToken ct = default) where TPayload : class;

    /// <summary>
    /// Publish an event without a payload.
    /// </summary>
    Task<Guid> PublishAsync(
        Guid runId,
        string eventType,
        EventPublishOptions? options = null,
        CancellationToken ct = default);

    /// <summary>
    /// Publish a pre-built event.
    /// </summary>
    Task PublishAsync(
        JobEvent @event,
        CancellationToken ct = default);

    /// <summary>
    /// Publish multiple events atomically.
    /// </summary>
    Task PublishBatchAsync(
        IEnumerable<JobEvent> events,
        CancellationToken ct = default);

    #endregion

    #region Job Lifecycle Events

    /// <summary>Publish JobScheduled event</summary>
    Task<Guid> PublishJobScheduledAsync(
        Guid runId,
        JobScheduledPayload payload,
        CancellationToken ct = default);

    /// <summary>Publish JobEnqueued event</summary>
    Task<Guid> PublishJobEnqueuedAsync(
        Guid runId,
        JobEnqueuedPayload payload,
        CancellationToken ct = default);

    /// <summary>Publish JobStarted event</summary>
    Task<Guid> PublishJobStartedAsync(
        Guid runId,
        JobStartedPayload payload,
        CancellationToken ct = default);

    /// <summary>Publish JobCompleted event</summary>
    Task<Guid> PublishJobCompletedAsync(
        Guid runId,
        JobCompletedPayload payload,
        CancellationToken ct = default);

    /// <summary>Publish JobFailed event</summary>
    Task<Guid> PublishJobFailedAsync(
        Guid runId,
        JobFailedPayload payload,
        CancellationToken ct = default);

    /// <summary>Publish JobRetrying event</summary>
    Task<Guid> PublishJobRetryingAsync(
        Guid runId,
        JobRetryingPayload payload,
        CancellationToken ct = default);

    #endregion

    #region Progress Events

    /// <summary>Publish ProgressUpdated event</summary>
    Task<Guid> PublishProgressAsync(
        Guid runId,
        int current,
        int total,
        string? message = null,
        CancellationToken ct = default);

    /// <summary>Publish ItemProcessed event</summary>
    Task<Guid> PublishItemProcessedAsync(
        Guid runId,
        string? itemId,
        int index,
        bool success,
        long processingTimeMs,
        string? errorMessage = null,
        CancellationToken ct = default);

    /// <summary>Publish MilestoneReached event</summary>
    Task<Guid> PublishMilestoneAsync(
        Guid runId,
        string milestoneName,
        int itemsProcessed,
        string? description = null,
        CancellationToken ct = default);

    #endregion

    #region State Events

    /// <summary>Publish CheckpointSaved event</summary>
    Task<Guid> PublishCheckpointSavedAsync(
        Guid runId,
        string key,
        int sequenceNumber,
        int dataSizeBytes,
        bool wasCompressed,
        CancellationToken ct = default);

    /// <summary>Publish CheckpointRestored event</summary>
    Task<Guid> PublishCheckpointRestoredAsync(
        Guid runId,
        string key,
        int sequenceNumber,
        DateTimeOffset createdAt,
        CancellationToken ct = default);

    #endregion

    #region Custom Events

    /// <summary>Publish a custom log message event</summary>
    Task<Guid> PublishLogAsync(
        Guid runId,
        string message,
        EventSeverity severity = EventSeverity.Info,
        Dictionary<string, object?>? properties = null,
        CancellationToken ct = default);

    /// <summary>Publish a custom metric event</summary>
    Task<Guid> PublishMetricAsync(
        Guid runId,
        string metricName,
        double value,
        string? unit = null,
        Dictionary<string, string>? tags = null,
        CancellationToken ct = default);

    /// <summary>Publish a custom business event</summary>
    Task<Guid> PublishBusinessEventAsync(
        Guid runId,
        string eventName,
        string? entityType = null,
        string? entityId = null,
        Dictionary<string, object?>? data = null,
        CancellationToken ct = default);

    #endregion

    #region Context Management

    /// <summary>
    /// Set correlation ID for subsequent events from this publisher instance.
    /// </summary>
    void SetCorrelationId(string correlationId);

    /// <summary>
    /// Set causation ID for subsequent events from this publisher instance.
    /// </summary>
    void SetCausationId(string causationId);

    /// <summary>
    /// Set actor for subsequent events from this publisher instance.
    /// </summary>
    void SetActor(string actor);

    /// <summary>
    /// Set source for subsequent events from this publisher instance.
    /// </summary>
    void SetSource(string source);

    /// <summary>
    /// Set workflow ID for subsequent events.
    /// </summary>
    void SetWorkflowId(Guid workflowId);

    /// <summary>
    /// Clear all context settings.
    /// </summary>
    void ClearContext();

    #endregion
}

/// <summary>
/// Options when publishing an event.
/// </summary>
public class EventPublishOptions
{
    /// <summary>Event category (default inferred from event type)</summary>
    public string? Category { get; set; }

    /// <summary>Tags for filtering</summary>
    public string[]? Tags { get; set; }

    /// <summary>Override correlation ID</summary>
    public string? CorrelationId { get; set; }

    /// <summary>Override causation ID</summary>
    public string? CausationId { get; set; }

    /// <summary>Override actor</summary>
    public string? Actor { get; set; }

    /// <summary>Override source</summary>
    public string? Source { get; set; }

    /// <summary>Workflow ID</summary>
    public Guid? WorkflowId { get; set; }

    /// <summary>Duration in milliseconds</summary>
    public long? DurationMs { get; set; }

    /// <summary>Whether this is a success event</summary>
    public bool? IsSuccess { get; set; }

    /// <summary>Additional metadata</summary>
    public Dictionary<string, object?>? Metadata { get; set; }

    /// <summary>Schema version</summary>
    public int Version { get; set; } = 1;
}
