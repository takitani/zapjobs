namespace ZapJobs.Core.History;

/// <summary>
/// Interface for storing and querying job events.
/// Provides features beyond Temporal, Hangfire, and EventStoreDB:
/// - Full-text search on events
/// - Built-in aggregations
/// - Time-travel queries
/// - Correlation tracking
/// - Configurable retention
/// </summary>
public interface IEventStore
{
    #region Write Operations

    /// <summary>
    /// Append a single event to the store.
    /// Events are immutable once appended.
    /// </summary>
    Task AppendAsync(JobEvent @event, CancellationToken ct = default);

    /// <summary>
    /// Append multiple events atomically.
    /// </summary>
    Task AppendBatchAsync(IEnumerable<JobEvent> events, CancellationToken ct = default);

    #endregion

    #region Query Operations

    /// <summary>
    /// Get all events for a specific job run.
    /// </summary>
    Task<IReadOnlyList<JobEvent>> GetEventsAsync(
        Guid runId,
        CancellationToken ct = default);

    /// <summary>
    /// Get events with filtering options.
    /// </summary>
    Task<IReadOnlyList<JobEvent>> GetEventsAsync(
        Guid runId,
        EventQueryOptions options,
        CancellationToken ct = default);

    /// <summary>
    /// Get a single event by ID.
    /// </summary>
    Task<JobEvent?> GetEventAsync(Guid eventId, CancellationToken ct = default);

    /// <summary>
    /// Get the latest event of a specific type for a run.
    /// </summary>
    Task<JobEvent?> GetLatestEventAsync(
        Guid runId,
        string eventType,
        CancellationToken ct = default);

    /// <summary>
    /// Check if a run has any events.
    /// </summary>
    Task<bool> HasEventsAsync(Guid runId, CancellationToken ct = default);

    /// <summary>
    /// Get event count for a run.
    /// </summary>
    Task<int> GetEventCountAsync(Guid runId, CancellationToken ct = default);

    #endregion

    #region Timeline Operations

    /// <summary>
    /// Get the complete timeline for a job run.
    /// </summary>
    Task<IReadOnlyList<TimelineEntry>> GetTimelineAsync(
        Guid runId,
        CancellationToken ct = default);

    /// <summary>
    /// Get events between two points in time.
    /// </summary>
    Task<IReadOnlyList<JobEvent>> GetEventsBetweenAsync(
        Guid runId,
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken ct = default);

    /// <summary>
    /// Get events after a specific sequence number (for streaming).
    /// </summary>
    Task<IReadOnlyList<JobEvent>> GetEventsAfterAsync(
        Guid runId,
        long afterSequence,
        int limit = 100,
        CancellationToken ct = default);

    #endregion

    #region Aggregation Operations

    /// <summary>
    /// Get summary statistics for a job's events.
    /// </summary>
    Task<EventHistorySummary> GetSummaryAsync(
        Guid runId,
        CancellationToken ct = default);

    /// <summary>
    /// Get event counts by type.
    /// </summary>
    Task<Dictionary<string, int>> GetEventCountsByTypeAsync(
        Guid runId,
        CancellationToken ct = default);

    /// <summary>
    /// Get event counts by category.
    /// </summary>
    Task<Dictionary<string, int>> GetEventCountsByCategoryAsync(
        Guid runId,
        CancellationToken ct = default);

    /// <summary>
    /// Get aggregated statistics for multiple runs.
    /// </summary>
    Task<EventHistorySummary> GetAggregatedSummaryAsync(
        IEnumerable<Guid> runIds,
        CancellationToken ct = default);

    #endregion

    #region Correlation Operations

    /// <summary>
    /// Get all events with a specific correlation ID.
    /// Useful for distributed tracing.
    /// </summary>
    Task<IReadOnlyList<JobEvent>> GetByCorrelationIdAsync(
        string correlationId,
        CancellationToken ct = default);

    /// <summary>
    /// Get all events in a workflow/batch.
    /// </summary>
    Task<IReadOnlyList<JobEvent>> GetWorkflowEventsAsync(
        Guid workflowId,
        CancellationToken ct = default);

    /// <summary>
    /// Get the causal chain of events (following causationId).
    /// </summary>
    Task<IReadOnlyList<JobEvent>> GetCausalChainAsync(
        Guid eventId,
        CancellationToken ct = default);

    #endregion

    #region Search Operations

    /// <summary>
    /// Search events by text (searches in payload, tags, actor).
    /// Feature not available in Temporal or Hangfire.
    /// </summary>
    Task<IReadOnlyList<JobEvent>> SearchAsync(
        string query,
        EventSearchOptions? options = null,
        CancellationToken ct = default);

    /// <summary>
    /// Find events matching specific criteria.
    /// </summary>
    Task<IReadOnlyList<JobEvent>> FindEventsAsync(
        EventFilterCriteria criteria,
        int limit = 100,
        CancellationToken ct = default);

    #endregion

    #region Time-Travel Operations

    /// <summary>
    /// Reconstruct job state at a specific point in time.
    /// Like Temporal but simpler - no determinism required.
    /// </summary>
    Task<JobStateSnapshot> GetStateAtAsync(
        Guid runId,
        DateTimeOffset pointInTime,
        CancellationToken ct = default);

    /// <summary>
    /// Reconstruct job state at a specific event sequence.
    /// </summary>
    Task<JobStateSnapshot> GetStateAtSequenceAsync(
        Guid runId,
        long sequenceNumber,
        CancellationToken ct = default);

    /// <summary>
    /// Get state snapshots at regular intervals (for visualization).
    /// </summary>
    Task<IReadOnlyList<JobStateSnapshot>> GetStateHistoryAsync(
        Guid runId,
        int maxSnapshots = 10,
        CancellationToken ct = default);

    #endregion

    #region Cleanup Operations

    /// <summary>
    /// Delete events for a specific run.
    /// </summary>
    Task<int> DeleteEventsAsync(Guid runId, CancellationToken ct = default);

    /// <summary>
    /// Delete events older than a specified age.
    /// </summary>
    Task<int> DeleteOldEventsAsync(TimeSpan maxAge, CancellationToken ct = default);

    /// <summary>
    /// Delete events matching retention policy.
    /// </summary>
    Task<int> ApplyRetentionPolicyAsync(
        EventRetentionPolicy policy,
        CancellationToken ct = default);

    #endregion

    #region Export Operations

    /// <summary>
    /// Export events for a run in a specified format.
    /// </summary>
    Task<string> ExportAsync(
        Guid runId,
        EventExportFormat format,
        CancellationToken ct = default);

    /// <summary>
    /// Export events as a stream (for large event histories).
    /// </summary>
    IAsyncEnumerable<JobEvent> StreamEventsAsync(
        Guid runId,
        CancellationToken ct = default);

    #endregion
}

/// <summary>
/// Options for querying events.
/// </summary>
public class EventQueryOptions
{
    /// <summary>Filter by event type</summary>
    public string? EventType { get; set; }

    /// <summary>Filter by event category</summary>
    public string? Category { get; set; }

    /// <summary>Filter by tags</summary>
    public string[]? Tags { get; set; }

    /// <summary>Start after this sequence number</summary>
    public long? AfterSequence { get; set; }

    /// <summary>Events after this time</summary>
    public DateTimeOffset? AfterTimestamp { get; set; }

    /// <summary>Events before this time</summary>
    public DateTimeOffset? BeforeTimestamp { get; set; }

    /// <summary>Only success events</summary>
    public bool? SuccessOnly { get; set; }

    /// <summary>Only failure events</summary>
    public bool? FailureOnly { get; set; }

    /// <summary>Maximum events to return</summary>
    public int Limit { get; set; } = 1000;

    /// <summary>Skip first N events</summary>
    public int Offset { get; set; } = 0;

    /// <summary>Sort order</summary>
    public EventSortOrder SortOrder { get; set; } = EventSortOrder.Ascending;
}

/// <summary>
/// Sort order for events.
/// </summary>
public enum EventSortOrder
{
    /// <summary>Oldest first</summary>
    Ascending,

    /// <summary>Newest first</summary>
    Descending
}

/// <summary>
/// Options for searching events.
/// </summary>
public class EventSearchOptions
{
    /// <summary>Limit search to specific run</summary>
    public Guid? RunId { get; set; }

    /// <summary>Limit search to specific workflow</summary>
    public Guid? WorkflowId { get; set; }

    /// <summary>Filter by event types</summary>
    public string[]? EventTypes { get; set; }

    /// <summary>Filter by categories</summary>
    public string[]? Categories { get; set; }

    /// <summary>Search in time range</summary>
    public DateTimeOffset? From { get; set; }

    /// <summary>Search in time range</summary>
    public DateTimeOffset? To { get; set; }

    /// <summary>Maximum results</summary>
    public int Limit { get; set; } = 100;

    /// <summary>Skip first N results</summary>
    public int Offset { get; set; } = 0;
}

/// <summary>
/// Criteria for filtering events.
/// </summary>
public class EventFilterCriteria
{
    /// <summary>Filter by run IDs</summary>
    public Guid[]? RunIds { get; set; }

    /// <summary>Filter by workflow IDs</summary>
    public Guid[]? WorkflowIds { get; set; }

    /// <summary>Filter by job type IDs</summary>
    public string[]? JobTypeIds { get; set; }

    /// <summary>Filter by event types</summary>
    public string[]? EventTypes { get; set; }

    /// <summary>Filter by categories</summary>
    public string[]? Categories { get; set; }

    /// <summary>Filter by correlation IDs</summary>
    public string[]? CorrelationIds { get; set; }

    /// <summary>Filter by actors</summary>
    public string[]? Actors { get; set; }

    /// <summary>Filter by sources</summary>
    public string[]? Sources { get; set; }

    /// <summary>Filter by tags (any match)</summary>
    public string[]? Tags { get; set; }

    /// <summary>Events after this time</summary>
    public DateTimeOffset? From { get; set; }

    /// <summary>Events before this time</summary>
    public DateTimeOffset? To { get; set; }

    /// <summary>Only success events</summary>
    public bool? IsSuccess { get; set; }

    /// <summary>Minimum duration in ms</summary>
    public long? MinDurationMs { get; set; }

    /// <summary>Maximum duration in ms</summary>
    public long? MaxDurationMs { get; set; }
}

/// <summary>
/// Retention policy for events.
/// </summary>
public class EventRetentionPolicy
{
    /// <summary>Default retention period</summary>
    public TimeSpan DefaultRetention { get; set; } = TimeSpan.FromDays(30);

    /// <summary>Retention per category (overrides default)</summary>
    public Dictionary<string, TimeSpan> CategoryRetention { get; set; } = new();

    /// <summary>Retention per event type (overrides category)</summary>
    public Dictionary<string, TimeSpan> EventTypeRetention { get; set; } = new();

    /// <summary>Keep events for completed jobs for this duration</summary>
    public TimeSpan? CompletedJobRetention { get; set; }

    /// <summary>Keep events for failed jobs for this duration</summary>
    public TimeSpan? FailedJobRetention { get; set; }

    /// <summary>Maximum events per run (oldest deleted first)</summary>
    public int? MaxEventsPerRun { get; set; }

    /// <summary>Maximum total events in store</summary>
    public long? MaxTotalEvents { get; set; }
}

/// <summary>
/// Export format for events.
/// </summary>
public enum EventExportFormat
{
    /// <summary>JSON array format</summary>
    Json,

    /// <summary>Newline-delimited JSON (for streaming)</summary>
    JsonLines,

    /// <summary>CSV format</summary>
    Csv,

    /// <summary>Human-readable text format</summary>
    Text
}

/// <summary>
/// Options for the event store.
/// </summary>
public class EventStoreOptions
{
    /// <summary>Enable event recording (default: true)</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Enable full-text search indexing</summary>
    public bool EnableFullTextSearch { get; set; } = true;

    /// <summary>Default retention policy</summary>
    public EventRetentionPolicy RetentionPolicy { get; set; } = new();

    /// <summary>Maximum batch size for writes</summary>
    public int MaxBatchSize { get; set; } = 100;

    /// <summary>Enable compression for old events</summary>
    public bool EnableCompression { get; set; } = true;

    /// <summary>Compress events older than this age</summary>
    public TimeSpan CompressAfter { get; set; } = TimeSpan.FromDays(7);

    /// <summary>Cleanup interval for old events</summary>
    public TimeSpan CleanupInterval { get; set; } = TimeSpan.FromHours(6);

    /// <summary>Include stack traces in error events</summary>
    public bool IncludeStackTraces { get; set; } = true;

    /// <summary>Maximum payload size in bytes</summary>
    public int MaxPayloadSizeBytes { get; set; } = 64 * 1024; // 64 KB
}
