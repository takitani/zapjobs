using System.Text.Json;
using ZapJobs.Core.Checkpoints;
using ZapJobs.Core.History;

namespace ZapJobs.Core;

/// <summary>
/// Context passed to a job during execution
/// </summary>
public class JobExecutionContext
{
    /// <summary>Unique ID of this run</summary>
    public Guid RunId { get; }

    /// <summary>Job type identifier</summary>
    public string JobTypeId { get; }

    /// <summary>How this job was triggered</summary>
    public JobTriggerType TriggerType { get; }

    /// <summary>Who/what triggered this job</summary>
    public string? TriggeredBy { get; }

    /// <summary>Service provider for dependency injection</summary>
    public IServiceProvider Services { get; }

    /// <summary>Logger for this execution</summary>
    public IJobLogger Logger { get; }

    /// <summary>Raw input as JSON document</summary>
    public JsonDocument? InputDocument { get; }

    /// <summary>Current attempt number (1-based)</summary>
    public int AttemptNumber { get; }

    /// <summary>
    /// Information about job resumption from checkpoint.
    /// Contains IsResuming flag and details about the previous attempt.
    /// </summary>
    public ResumeContext Resume { get; }

    /// <summary>Checkpoint store for saving/restoring state</summary>
    private readonly ICheckpointStore? _checkpointStore;

    /// <summary>Event publisher for event history</summary>
    private readonly IEventPublisher? _eventPublisher;

    private int _itemsProcessed;
    private int _itemsSucceeded;
    private int _itemsFailed;
    private object? _output;

    public JobExecutionContext(
        Guid runId,
        string jobTypeId,
        JobTriggerType triggerType,
        string? triggeredBy,
        IServiceProvider services,
        IJobLogger logger,
        JsonDocument? inputDocument = null,
        int attemptNumber = 1,
        ResumeContext? resumeContext = null,
        ICheckpointStore? checkpointStore = null,
        IEventPublisher? eventPublisher = null)
    {
        RunId = runId;
        JobTypeId = jobTypeId;
        TriggerType = triggerType;
        TriggeredBy = triggeredBy;
        Services = services;
        Logger = logger;
        InputDocument = inputDocument;
        AttemptNumber = attemptNumber;
        Resume = resumeContext ?? new ResumeContext { IsResuming = false, Reason = ResumeReason.None };
        _checkpointStore = checkpointStore;
        _eventPublisher = eventPublisher;
    }

    /// <summary>Get typed input from JSON</summary>
    public T? GetInput<T>() where T : class
    {
        if (InputDocument == null) return null;
        return InputDocument.Deserialize<T>();
    }

    /// <summary>Convert to typed context</summary>
    public JobExecutionContext<T> As<T>() where T : class
    {
        return new JobExecutionContext<T>(this);
    }

    /// <summary>Set output data</summary>
    public void SetOutput(object? output)
    {
        _output = output;
    }

    /// <summary>Get output data</summary>
    public object? GetOutput() => _output;

    /// <summary>Increment items processed counter</summary>
    public void IncrementProcessed(int count = 1)
    {
        Interlocked.Add(ref _itemsProcessed, count);
    }

    /// <summary>Increment items succeeded counter</summary>
    public void IncrementSucceeded(int count = 1)
    {
        Interlocked.Add(ref _itemsSucceeded, count);
    }

    /// <summary>Increment items failed counter</summary>
    public void IncrementFailed(int count = 1)
    {
        Interlocked.Add(ref _itemsFailed, count);
    }

    /// <summary>Get current metrics</summary>
    public JobMetrics GetMetrics() => new(_itemsProcessed, _itemsSucceeded, _itemsFailed);

    /// <summary>Update all metrics at once</summary>
    public void UpdateMetrics(int processed, int succeeded, int failed)
    {
        Interlocked.Exchange(ref _itemsProcessed, processed);
        Interlocked.Exchange(ref _itemsSucceeded, succeeded);
        Interlocked.Exchange(ref _itemsFailed, failed);
    }

    #region Checkpoint Methods

    /// <summary>
    /// Saves a checkpoint with the specified key.
    /// Use different keys for different checkpoint types (e.g., "progress", "cursor", "batch-state").
    /// </summary>
    /// <param name="key">Checkpoint key identifier</param>
    /// <param name="data">Data to checkpoint (will be serialized to JSON)</param>
    /// <param name="options">Optional save options (TTL, compression, etc.)</param>
    /// <param name="ct">Cancellation token</param>
    /// <typeparam name="T">Type of the checkpoint data</typeparam>
    /// <returns>Result of the save operation</returns>
    /// <exception cref="InvalidOperationException">Thrown if checkpoint store is not configured</exception>
    public async Task<CheckpointResult> CheckpointAsync<T>(
        string key,
        T data,
        CheckpointSaveOptions? options = null,
        CancellationToken ct = default) where T : class
    {
        if (_checkpointStore == null)
        {
            return CheckpointResult.Fail("Checkpoint store not configured. Enable checkpoints in ZapJobsOptions.");
        }

        // Auto-populate metadata if enabled and not provided
        if (options?.Metadata == null)
        {
            options ??= new CheckpointSaveOptions();
            options.Metadata = new CheckpointMetadata
            {
                JobTypeId = JobTypeId,
                AttemptNumber = AttemptNumber,
                ItemsProcessed = _itemsProcessed,
                Progress = null // Will be set by job if desired
            };
        }

        return await _checkpointStore.SaveAsync(RunId, key, data, options, ct);
    }

    /// <summary>
    /// Saves a checkpoint using the default key "state".
    /// </summary>
    public Task<CheckpointResult> CheckpointAsync<T>(
        T data,
        CheckpointSaveOptions? options = null,
        CancellationToken ct = default) where T : class
    {
        return CheckpointAsync("state", data, options, ct);
    }

    /// <summary>
    /// Gets the latest checkpoint data for the specified key.
    /// Returns null if no checkpoint exists or checkpoints are not enabled.
    /// </summary>
    /// <param name="key">Checkpoint key identifier</param>
    /// <param name="ct">Cancellation token</param>
    /// <typeparam name="T">Type to deserialize the checkpoint data to</typeparam>
    /// <returns>The checkpoint data, or null if not found</returns>
    public async Task<T?> GetCheckpointAsync<T>(string key, CancellationToken ct = default) where T : class
    {
        if (_checkpointStore == null)
        {
            return null;
        }

        return await _checkpointStore.GetAsync<T>(RunId, key, ct);
    }

    /// <summary>
    /// Gets the latest checkpoint data using the default key "state".
    /// </summary>
    public Task<T?> GetCheckpointAsync<T>(CancellationToken ct = default) where T : class
    {
        return GetCheckpointAsync<T>("state", ct);
    }

    /// <summary>
    /// Checks if a checkpoint exists for the specified key.
    /// </summary>
    public async Task<bool> HasCheckpointAsync(string key, CancellationToken ct = default)
    {
        if (_checkpointStore == null)
        {
            return false;
        }

        return await _checkpointStore.ExistsAsync(RunId, key, ct);
    }

    /// <summary>
    /// Checks if a checkpoint exists using the default key "state".
    /// </summary>
    public Task<bool> HasCheckpointAsync(CancellationToken ct = default)
    {
        return HasCheckpointAsync("state", ct);
    }

    /// <summary>
    /// Gets all checkpoints for this job run.
    /// </summary>
    public async Task<IReadOnlyList<Checkpoint>> GetAllCheckpointsAsync(CancellationToken ct = default)
    {
        if (_checkpointStore == null)
        {
            return Array.Empty<Checkpoint>();
        }

        return await _checkpointStore.GetAllAsync(RunId, ct);
    }

    /// <summary>
    /// Deletes all checkpoints for this job run.
    /// Useful for cleanup after successful completion.
    /// </summary>
    /// <returns>Number of checkpoints deleted</returns>
    public async Task<int> ClearCheckpointsAsync(CancellationToken ct = default)
    {
        if (_checkpointStore == null)
        {
            return 0;
        }

        return await _checkpointStore.DeleteAllAsync(RunId, ct);
    }

    #endregion

    #region Event Methods

    /// <summary>
    /// Publishes a custom event to the event history.
    /// Use for domain-specific events during job execution.
    /// </summary>
    /// <param name="eventType">Event type identifier (e.g., "order.validated", "payment.processed")</param>
    /// <param name="payload">Event payload data</param>
    /// <param name="options">Optional publish options</param>
    /// <param name="ct">Cancellation token</param>
    /// <typeparam name="T">Type of the payload</typeparam>
    /// <returns>Event ID if published, null if event publishing is not enabled</returns>
    public async Task<Guid?> PublishEventAsync<T>(
        string eventType,
        T payload,
        EventPublishOptions? options = null,
        CancellationToken ct = default) where T : class
    {
        if (_eventPublisher == null) return null;
        return await _eventPublisher.PublishAsync(RunId, eventType, payload, options, ct);
    }

    /// <summary>
    /// Publishes a progress update event.
    /// Better than SetProgress for tracking progress history over time.
    /// </summary>
    /// <param name="current">Current items processed</param>
    /// <param name="total">Total items to process</param>
    /// <param name="message">Optional progress message</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>Event ID if published, null if event publishing is not enabled</returns>
    public async Task<Guid?> PublishProgressAsync(
        int current,
        int total,
        string? message = null,
        CancellationToken ct = default)
    {
        if (_eventPublisher == null) return null;
        return await _eventPublisher.PublishProgressAsync(RunId, current, total, message, ct);
    }

    /// <summary>
    /// Publishes a milestone event for significant progress points.
    /// </summary>
    /// <param name="milestoneName">Name of the milestone (e.g., "validation-complete", "50-percent")</param>
    /// <param name="description">Optional description</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>Event ID if published, null if event publishing is not enabled</returns>
    public async Task<Guid?> PublishMilestoneAsync(
        string milestoneName,
        string? description = null,
        CancellationToken ct = default)
    {
        if (_eventPublisher == null) return null;
        return await _eventPublisher.PublishMilestoneAsync(
            RunId, milestoneName, _itemsProcessed, description, ct);
    }

    /// <summary>
    /// Publishes a log message to the event history.
    /// These are queryable unlike standard logs.
    /// </summary>
    /// <param name="message">Log message</param>
    /// <param name="severity">Log severity</param>
    /// <param name="properties">Additional properties</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>Event ID if published, null if event publishing is not enabled</returns>
    public async Task<Guid?> PublishLogAsync(
        string message,
        EventSeverity severity = EventSeverity.Info,
        Dictionary<string, object?>? properties = null,
        CancellationToken ct = default)
    {
        if (_eventPublisher == null) return null;
        return await _eventPublisher.PublishLogAsync(RunId, message, severity, properties, ct);
    }

    /// <summary>
    /// Publishes a metric event for performance tracking.
    /// </summary>
    /// <param name="metricName">Metric name (e.g., "items_per_second", "batch_size")</param>
    /// <param name="value">Metric value</param>
    /// <param name="unit">Optional unit (e.g., "ms", "bytes", "count")</param>
    /// <param name="tags">Optional tags for filtering</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>Event ID if published, null if event publishing is not enabled</returns>
    public async Task<Guid?> PublishMetricAsync(
        string metricName,
        double value,
        string? unit = null,
        Dictionary<string, string>? tags = null,
        CancellationToken ct = default)
    {
        if (_eventPublisher == null) return null;
        return await _eventPublisher.PublishMetricAsync(RunId, metricName, value, unit, tags, ct);
    }

    /// <summary>
    /// Publishes a business event for domain-specific tracking.
    /// </summary>
    /// <param name="eventName">Business event name (e.g., "order.created", "payment.received")</param>
    /// <param name="entityType">Optional entity type (e.g., "Order", "Customer")</param>
    /// <param name="entityId">Optional entity ID</param>
    /// <param name="data">Additional event data</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>Event ID if published, null if event publishing is not enabled</returns>
    public async Task<Guid?> PublishBusinessEventAsync(
        string eventName,
        string? entityType = null,
        string? entityId = null,
        Dictionary<string, object?>? data = null,
        CancellationToken ct = default)
    {
        if (_eventPublisher == null) return null;
        return await _eventPublisher.PublishBusinessEventAsync(
            RunId, eventName, entityType, entityId, data, ct);
    }

    /// <summary>
    /// Records an item processed event for detailed item tracking.
    /// </summary>
    /// <param name="itemId">Optional item identifier</param>
    /// <param name="index">Item index in the batch</param>
    /// <param name="success">Whether processing succeeded</param>
    /// <param name="processingTimeMs">Processing time in milliseconds</param>
    /// <param name="errorMessage">Error message if failed</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>Event ID if published, null if event publishing is not enabled</returns>
    public async Task<Guid?> PublishItemProcessedAsync(
        string? itemId,
        int index,
        bool success,
        long processingTimeMs,
        string? errorMessage = null,
        CancellationToken ct = default)
    {
        if (_eventPublisher == null) return null;
        return await _eventPublisher.PublishItemProcessedAsync(
            RunId, itemId, index, success, processingTimeMs, errorMessage, ct);
    }

    /// <summary>
    /// Gets whether event publishing is enabled for this context.
    /// </summary>
    public bool EventsEnabled => _eventPublisher != null;

    /// <summary>
    /// Gets the raw event publisher for advanced scenarios.
    /// Returns null if event publishing is not enabled.
    /// </summary>
    public IEventPublisher? Events => _eventPublisher;

    #endregion
}

/// <summary>
/// Typed context with strongly-typed input
/// </summary>
public class JobExecutionContext<TInput> where TInput : class
{
    private readonly JobExecutionContext _inner;

    public JobExecutionContext(JobExecutionContext inner)
    {
        _inner = inner;
        Input = inner.GetInput<TInput>();
    }

    /// <summary>Typed input data</summary>
    public TInput? Input { get; }

    /// <summary>Unique ID of this run</summary>
    public Guid RunId => _inner.RunId;

    /// <summary>Job type identifier</summary>
    public string JobTypeId => _inner.JobTypeId;

    /// <summary>How this job was triggered</summary>
    public JobTriggerType TriggerType => _inner.TriggerType;

    /// <summary>Who/what triggered this job</summary>
    public string? TriggeredBy => _inner.TriggeredBy;

    /// <summary>Service provider for dependency injection</summary>
    public IServiceProvider Services => _inner.Services;

    /// <summary>Logger for this execution</summary>
    public IJobLogger Logger => _inner.Logger;

    /// <summary>Current attempt number (1-based)</summary>
    public int AttemptNumber => _inner.AttemptNumber;

    /// <summary>
    /// Information about job resumption from checkpoint.
    /// Contains IsResuming flag and details about the previous attempt.
    /// </summary>
    public ResumeContext Resume => _inner.Resume;

    /// <summary>Set output data</summary>
    public void SetOutput(object? output) => _inner.SetOutput(output);

    /// <summary>Increment items processed counter</summary>
    public void IncrementProcessed(int count = 1) => _inner.IncrementProcessed(count);

    /// <summary>Increment items succeeded counter</summary>
    public void IncrementSucceeded(int count = 1) => _inner.IncrementSucceeded(count);

    /// <summary>Increment items failed counter</summary>
    public void IncrementFailed(int count = 1) => _inner.IncrementFailed(count);

    /// <summary>Get current metrics</summary>
    public JobMetrics GetMetrics() => _inner.GetMetrics();

    /// <summary>Update all metrics at once</summary>
    public void UpdateMetrics(int processed, int succeeded, int failed) => _inner.UpdateMetrics(processed, succeeded, failed);

    #region Checkpoint Methods

    /// <summary>
    /// Saves a checkpoint with the specified key.
    /// Use different keys for different checkpoint types (e.g., "progress", "cursor", "batch-state").
    /// </summary>
    public Task<CheckpointResult> CheckpointAsync<T>(
        string key,
        T data,
        CheckpointSaveOptions? options = null,
        CancellationToken ct = default) where T : class
        => _inner.CheckpointAsync(key, data, options, ct);

    /// <summary>
    /// Saves a checkpoint using the default key "state".
    /// </summary>
    public Task<CheckpointResult> CheckpointAsync<T>(
        T data,
        CheckpointSaveOptions? options = null,
        CancellationToken ct = default) where T : class
        => _inner.CheckpointAsync(data, options, ct);

    /// <summary>
    /// Gets the latest checkpoint data for the specified key.
    /// Returns null if no checkpoint exists or checkpoints are not enabled.
    /// </summary>
    public Task<T?> GetCheckpointAsync<T>(string key, CancellationToken ct = default) where T : class
        => _inner.GetCheckpointAsync<T>(key, ct);

    /// <summary>
    /// Gets the latest checkpoint data using the default key "state".
    /// </summary>
    public Task<T?> GetCheckpointAsync<T>(CancellationToken ct = default) where T : class
        => _inner.GetCheckpointAsync<T>(ct);

    /// <summary>
    /// Checks if a checkpoint exists for the specified key.
    /// </summary>
    public Task<bool> HasCheckpointAsync(string key, CancellationToken ct = default)
        => _inner.HasCheckpointAsync(key, ct);

    /// <summary>
    /// Checks if a checkpoint exists using the default key "state".
    /// </summary>
    public Task<bool> HasCheckpointAsync(CancellationToken ct = default)
        => _inner.HasCheckpointAsync(ct);

    /// <summary>
    /// Gets all checkpoints for this job run.
    /// </summary>
    public Task<IReadOnlyList<Checkpoint>> GetAllCheckpointsAsync(CancellationToken ct = default)
        => _inner.GetAllCheckpointsAsync(ct);

    /// <summary>
    /// Deletes all checkpoints for this job run.
    /// Useful for cleanup after successful completion.
    /// </summary>
    public Task<int> ClearCheckpointsAsync(CancellationToken ct = default)
        => _inner.ClearCheckpointsAsync(ct);

    #endregion

    #region Event Methods

    /// <summary>
    /// Publishes a custom event to the event history.
    /// </summary>
    public Task<Guid?> PublishEventAsync<T>(
        string eventType,
        T payload,
        EventPublishOptions? options = null,
        CancellationToken ct = default) where T : class
        => _inner.PublishEventAsync(eventType, payload, options, ct);

    /// <summary>
    /// Publishes a progress update event.
    /// </summary>
    public Task<Guid?> PublishProgressAsync(
        int current,
        int total,
        string? message = null,
        CancellationToken ct = default)
        => _inner.PublishProgressAsync(current, total, message, ct);

    /// <summary>
    /// Publishes a milestone event for significant progress points.
    /// </summary>
    public Task<Guid?> PublishMilestoneAsync(
        string milestoneName,
        string? description = null,
        CancellationToken ct = default)
        => _inner.PublishMilestoneAsync(milestoneName, description, ct);

    /// <summary>
    /// Publishes a log message to the event history.
    /// </summary>
    public Task<Guid?> PublishLogAsync(
        string message,
        EventSeverity severity = EventSeverity.Info,
        Dictionary<string, object?>? properties = null,
        CancellationToken ct = default)
        => _inner.PublishLogAsync(message, severity, properties, ct);

    /// <summary>
    /// Publishes a metric event for performance tracking.
    /// </summary>
    public Task<Guid?> PublishMetricAsync(
        string metricName,
        double value,
        string? unit = null,
        Dictionary<string, string>? tags = null,
        CancellationToken ct = default)
        => _inner.PublishMetricAsync(metricName, value, unit, tags, ct);

    /// <summary>
    /// Publishes a business event for domain-specific tracking.
    /// </summary>
    public Task<Guid?> PublishBusinessEventAsync(
        string eventName,
        string? entityType = null,
        string? entityId = null,
        Dictionary<string, object?>? data = null,
        CancellationToken ct = default)
        => _inner.PublishBusinessEventAsync(eventName, entityType, entityId, data, ct);

    /// <summary>
    /// Records an item processed event for detailed item tracking.
    /// </summary>
    public Task<Guid?> PublishItemProcessedAsync(
        string? itemId,
        int index,
        bool success,
        long processingTimeMs,
        string? errorMessage = null,
        CancellationToken ct = default)
        => _inner.PublishItemProcessedAsync(itemId, index, success, processingTimeMs, errorMessage, ct);

    /// <summary>
    /// Gets whether event publishing is enabled for this context.
    /// </summary>
    public bool EventsEnabled => _inner.EventsEnabled;

    /// <summary>
    /// Gets the raw event publisher for advanced scenarios.
    /// </summary>
    public IEventPublisher? Events => _inner.Events;

    #endregion
}
