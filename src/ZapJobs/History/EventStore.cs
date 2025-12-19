using System.Text.Json;
using ZapJobs.Core;
using ZapJobs.Core.History;

namespace ZapJobs.History;

/// <summary>
/// Default implementation of IEventStore that provides rich event history
/// capabilities including aggregations, time-travel, and search.
/// </summary>
public class EventStore : IEventStore
{
    private readonly IJobStorage _storage;
    private readonly EventStoreOptions _options;
    private readonly JsonSerializerOptions _jsonOptions;

    public EventStore(IJobStorage storage, EventStoreOptions? options = null)
    {
        _storage = storage;
        _options = options ?? new EventStoreOptions();
        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false
        };
    }

    #region Write Operations

    public async Task AppendAsync(JobEvent @event, CancellationToken ct = default)
    {
        if (!_options.Enabled) return;

        ValidateEvent(@event);

        // Assign sequence number if not set
        if (@event.SequenceNumber == 0)
        {
            @event.SequenceNumber = await _storage.GetNextEventSequenceAsync(ct);
        }

        // Get run sequence
        var existingCount = await _storage.GetEventCountAsync(@event.RunId, ct);
        @event.RunSequence = existingCount + 1;

        await _storage.AppendEventAsync(@event, ct);
    }

    public async Task AppendBatchAsync(IEnumerable<JobEvent> events, CancellationToken ct = default)
    {
        if (!_options.Enabled) return;

        var eventList = events.ToList();
        if (eventList.Count == 0) return;

        if (eventList.Count > _options.MaxBatchSize)
        {
            throw new InvalidOperationException(
                $"Batch size {eventList.Count} exceeds maximum of {_options.MaxBatchSize}");
        }

        // Assign sequence numbers
        var baseSequence = await _storage.GetNextEventSequenceAsync(ct);
        for (int i = 0; i < eventList.Count; i++)
        {
            ValidateEvent(eventList[i]);
            if (eventList[i].SequenceNumber == 0)
            {
                eventList[i].SequenceNumber = baseSequence + i;
            }
        }

        await _storage.AppendEventsAsync(eventList, ct);
    }

    private void ValidateEvent(JobEvent @event)
    {
        if (@event.RunId == Guid.Empty)
            throw new ArgumentException("RunId is required", nameof(@event));

        if (string.IsNullOrEmpty(@event.EventType))
            throw new ArgumentException("EventType is required", nameof(@event));

        // Check payload size
        if (@event.PayloadJson?.Length > _options.MaxPayloadSizeBytes)
        {
            throw new InvalidOperationException(
                $"Payload size {_options.MaxPayloadSizeBytes} bytes exceeds maximum of {_options.MaxPayloadSizeBytes}");
        }
    }

    #endregion

    #region Query Operations

    public Task<IReadOnlyList<JobEvent>> GetEventsAsync(Guid runId, CancellationToken ct = default)
    {
        return _storage.GetEventsAsync(runId, ct);
    }

    public Task<IReadOnlyList<JobEvent>> GetEventsAsync(
        Guid runId,
        EventQueryOptions options,
        CancellationToken ct = default)
    {
        return _storage.GetEventsAsync(
            runId,
            options.EventType,
            options.Category,
            options.Limit,
            options.Offset,
            ct);
    }

    public Task<JobEvent?> GetEventAsync(Guid eventId, CancellationToken ct = default)
    {
        return _storage.GetEventAsync(eventId, ct);
    }

    public Task<JobEvent?> GetLatestEventAsync(Guid runId, string eventType, CancellationToken ct = default)
    {
        return _storage.GetLatestEventAsync(runId, eventType, ct);
    }

    public async Task<bool> HasEventsAsync(Guid runId, CancellationToken ct = default)
    {
        var count = await _storage.GetEventCountAsync(runId, ct);
        return count > 0;
    }

    public Task<int> GetEventCountAsync(Guid runId, CancellationToken ct = default)
    {
        return _storage.GetEventCountAsync(runId, ct);
    }

    #endregion

    #region Timeline Operations

    public async Task<IReadOnlyList<TimelineEntry>> GetTimelineAsync(Guid runId, CancellationToken ct = default)
    {
        var events = await _storage.GetEventsAsync(runId, ct);
        var timeline = new List<TimelineEntry>();

        DateTimeOffset? previousTimestamp = null;

        foreach (var @event in events.OrderBy(e => e.SequenceNumber))
        {
            var entry = new TimelineEntry
            {
                Id = @event.Id,
                EventType = @event.EventType,
                Category = @event.Category,
                Timestamp = @event.Timestamp,
                MsSincePrevious = previousTimestamp.HasValue
                    ? (long)(@event.Timestamp - previousTimestamp.Value).TotalMilliseconds
                    : null,
                Title = GetEventTitle(@event),
                Description = GetEventDescription(@event),
                IsSuccess = @event.IsSuccess,
                DurationMs = @event.DurationMs,
                Severity = GetEventSeverity(@event),
                Payload = DeserializePayload(@event)
            };

            timeline.Add(entry);
            previousTimestamp = @event.Timestamp;
        }

        return timeline;
    }

    public async Task<IReadOnlyList<JobEvent>> GetEventsBetweenAsync(
        Guid runId,
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken ct = default)
    {
        var allEvents = await _storage.GetEventsAsync(runId, ct);
        return allEvents
            .Where(e => e.Timestamp >= from && e.Timestamp <= to)
            .OrderBy(e => e.SequenceNumber)
            .ToList();
    }

    public Task<IReadOnlyList<JobEvent>> GetEventsAfterAsync(
        Guid runId,
        long afterSequence,
        int limit = 100,
        CancellationToken ct = default)
    {
        return _storage.GetEventsAfterSequenceAsync(runId, afterSequence, limit, ct);
    }

    #endregion

    #region Aggregation Operations

    public async Task<EventHistorySummary> GetSummaryAsync(Guid runId, CancellationToken ct = default)
    {
        var events = await _storage.GetEventsAsync(runId, ct);
        return BuildSummary(events);
    }

    public async Task<Dictionary<string, int>> GetEventCountsByTypeAsync(Guid runId, CancellationToken ct = default)
    {
        var events = await _storage.GetEventsAsync(runId, ct);
        return events
            .GroupBy(e => e.EventType)
            .ToDictionary(g => g.Key, g => g.Count());
    }

    public async Task<Dictionary<string, int>> GetEventCountsByCategoryAsync(Guid runId, CancellationToken ct = default)
    {
        var events = await _storage.GetEventsAsync(runId, ct);
        return events
            .GroupBy(e => e.Category)
            .ToDictionary(g => g.Key, g => g.Count());
    }

    public async Task<EventHistorySummary> GetAggregatedSummaryAsync(
        IEnumerable<Guid> runIds,
        CancellationToken ct = default)
    {
        var allEvents = new List<JobEvent>();

        foreach (var runId in runIds)
        {
            var events = await _storage.GetEventsAsync(runId, ct);
            allEvents.AddRange(events);
        }

        return BuildSummary(allEvents);
    }

    private EventHistorySummary BuildSummary(IReadOnlyList<JobEvent> events)
    {
        if (events.Count == 0)
        {
            return new EventHistorySummary { TotalEvents = 0 };
        }

        var firstEvent = events.MinBy(e => e.Timestamp);
        var lastEvent = events.MaxBy(e => e.Timestamp);
        var totalDuration = lastEvent != null && firstEvent != null
            ? (long)(lastEvent.Timestamp - firstEvent.Timestamp).TotalMilliseconds
            : (long?)null;

        return new EventHistorySummary
        {
            TotalEvents = events.Count,
            EventsByCategory = events
                .GroupBy(e => e.Category)
                .ToDictionary(g => g.Key, g => g.Count()),
            EventsByType = events
                .GroupBy(e => e.EventType)
                .ToDictionary(g => g.Key, g => g.Count()),
            FirstEventAt = firstEvent?.Timestamp,
            LastEventAt = lastEvent?.Timestamp,
            TotalDurationMs = totalDuration,
            ErrorCount = events.Count(e => e.IsSuccess == false ||
                e.EventType == EventTypes.JobFailed ||
                e.EventType == EventTypes.ActivityFailed),
            RetryCount = events.Count(e => e.EventType == EventTypes.JobRetrying),
            EventsPerSecond = totalDuration.HasValue && totalDuration.Value > 0
                ? events.Count / (totalDuration.Value / 1000.0)
                : null
        };
    }

    #endregion

    #region Correlation Operations

    public Task<IReadOnlyList<JobEvent>> GetByCorrelationIdAsync(
        string correlationId,
        CancellationToken ct = default)
    {
        return _storage.GetEventsByCorrelationIdAsync(correlationId, ct);
    }

    public Task<IReadOnlyList<JobEvent>> GetWorkflowEventsAsync(
        Guid workflowId,
        CancellationToken ct = default)
    {
        return _storage.GetEventsByWorkflowIdAsync(workflowId, ct);
    }

    public async Task<IReadOnlyList<JobEvent>> GetCausalChainAsync(
        Guid eventId,
        CancellationToken ct = default)
    {
        var chain = new List<JobEvent>();
        var currentEvent = await _storage.GetEventAsync(eventId, ct);

        while (currentEvent != null)
        {
            chain.Add(currentEvent);

            if (string.IsNullOrEmpty(currentEvent.CausationId))
                break;

            // Try to find parent event by causation ID (which should be the parent's event ID)
            if (Guid.TryParse(currentEvent.CausationId, out var parentEventId))
            {
                currentEvent = await _storage.GetEventAsync(parentEventId, ct);
            }
            else
            {
                // CausationId might be a correlation ID, search for it
                var parentEvents = await _storage.GetEventsByCorrelationIdAsync(
                    currentEvent.CausationId, ct);
                currentEvent = parentEvents
                    .Where(e => e.Timestamp < currentEvent.Timestamp)
                    .OrderByDescending(e => e.Timestamp)
                    .FirstOrDefault();
            }
        }

        // Return in chronological order
        chain.Reverse();
        return chain;
    }

    #endregion

    #region Search Operations

    public Task<IReadOnlyList<JobEvent>> SearchAsync(
        string query,
        EventSearchOptions? options = null,
        CancellationToken ct = default)
    {
        options ??= new EventSearchOptions();

        return _storage.SearchEventsAsync(
            query,
            options.RunId,
            options.WorkflowId,
            options.EventTypes,
            options.From,
            options.To,
            options.Limit,
            ct);
    }

    public async Task<IReadOnlyList<JobEvent>> FindEventsAsync(
        EventFilterCriteria criteria,
        int limit = 100,
        CancellationToken ct = default)
    {
        // Build search based on criteria
        var allEvents = new List<JobEvent>();

        // Get events by run IDs
        if (criteria.RunIds != null && criteria.RunIds.Length > 0)
        {
            foreach (var runId in criteria.RunIds)
            {
                var events = await _storage.GetEventsAsync(runId, ct);
                allEvents.AddRange(events);
            }
        }
        // Get events by workflow IDs
        else if (criteria.WorkflowIds != null && criteria.WorkflowIds.Length > 0)
        {
            foreach (var workflowId in criteria.WorkflowIds)
            {
                var events = await _storage.GetEventsByWorkflowIdAsync(workflowId, ct);
                allEvents.AddRange(events);
            }
        }
        // Get events by correlation IDs
        else if (criteria.CorrelationIds != null && criteria.CorrelationIds.Length > 0)
        {
            foreach (var correlationId in criteria.CorrelationIds)
            {
                var events = await _storage.GetEventsByCorrelationIdAsync(correlationId, ct);
                allEvents.AddRange(events);
            }
        }

        // Apply filters
        var filtered = allEvents.AsEnumerable();

        if (criteria.EventTypes != null && criteria.EventTypes.Length > 0)
        {
            filtered = filtered.Where(e => criteria.EventTypes.Contains(e.EventType));
        }

        if (criteria.Categories != null && criteria.Categories.Length > 0)
        {
            filtered = filtered.Where(e => criteria.Categories.Contains(e.Category));
        }

        if (criteria.Actors != null && criteria.Actors.Length > 0)
        {
            filtered = filtered.Where(e => e.Actor != null && criteria.Actors.Contains(e.Actor));
        }

        if (criteria.Sources != null && criteria.Sources.Length > 0)
        {
            filtered = filtered.Where(e => e.Source != null && criteria.Sources.Contains(e.Source));
        }

        if (criteria.Tags != null && criteria.Tags.Length > 0)
        {
            filtered = filtered.Where(e => e.Tags != null && e.Tags.Intersect(criteria.Tags).Any());
        }

        if (criteria.From.HasValue)
        {
            filtered = filtered.Where(e => e.Timestamp >= criteria.From.Value);
        }

        if (criteria.To.HasValue)
        {
            filtered = filtered.Where(e => e.Timestamp <= criteria.To.Value);
        }

        if (criteria.IsSuccess.HasValue)
        {
            filtered = filtered.Where(e => e.IsSuccess == criteria.IsSuccess.Value);
        }

        if (criteria.MinDurationMs.HasValue)
        {
            filtered = filtered.Where(e => e.DurationMs >= criteria.MinDurationMs.Value);
        }

        if (criteria.MaxDurationMs.HasValue)
        {
            filtered = filtered.Where(e => e.DurationMs <= criteria.MaxDurationMs.Value);
        }

        return filtered
            .OrderBy(e => e.Timestamp)
            .Take(limit)
            .ToList();
    }

    #endregion

    #region Time-Travel Operations

    public async Task<JobStateSnapshot> GetStateAtAsync(
        Guid runId,
        DateTimeOffset pointInTime,
        CancellationToken ct = default)
    {
        var events = await _storage.GetEventsAsync(runId, ct);
        var relevantEvents = events
            .Where(e => e.Timestamp <= pointInTime)
            .OrderBy(e => e.SequenceNumber)
            .ToList();

        return ReconstructState(runId, relevantEvents, pointInTime);
    }

    public async Task<JobStateSnapshot> GetStateAtSequenceAsync(
        Guid runId,
        long sequenceNumber,
        CancellationToken ct = default)
    {
        var events = await _storage.GetEventsAsync(runId, ct);
        var relevantEvents = events
            .Where(e => e.SequenceNumber <= sequenceNumber)
            .OrderBy(e => e.SequenceNumber)
            .ToList();

        var lastEvent = relevantEvents.LastOrDefault();
        return ReconstructState(runId, relevantEvents, lastEvent?.Timestamp ?? DateTimeOffset.UtcNow);
    }

    public async Task<IReadOnlyList<JobStateSnapshot>> GetStateHistoryAsync(
        Guid runId,
        int maxSnapshots = 10,
        CancellationToken ct = default)
    {
        var events = await _storage.GetEventsAsync(runId, ct);
        if (events.Count == 0) return Array.Empty<JobStateSnapshot>();

        var orderedEvents = events.OrderBy(e => e.SequenceNumber).ToList();
        var snapshots = new List<JobStateSnapshot>();

        // Create snapshots at key events (state changes)
        var stateChangeEvents = orderedEvents
            .Where(e => IsStateChangeEvent(e.EventType))
            .ToList();

        // If we have more state changes than max, sample evenly
        var eventsToSnapshot = stateChangeEvents.Count <= maxSnapshots
            ? stateChangeEvents
            : SampleEvenly(stateChangeEvents, maxSnapshots);

        var processedEvents = new List<JobEvent>();
        foreach (var evt in orderedEvents)
        {
            processedEvents.Add(evt);

            if (eventsToSnapshot.Contains(evt))
            {
                var snapshot = ReconstructState(runId, processedEvents, evt.Timestamp);
                snapshots.Add(snapshot);
            }
        }

        return snapshots;
    }

    private static bool IsStateChangeEvent(string eventType)
    {
        return eventType is EventTypes.JobScheduled
            or EventTypes.JobEnqueued
            or EventTypes.JobStarted
            or EventTypes.JobCompleted
            or EventTypes.JobFailed
            or EventTypes.JobRetrying
            or EventTypes.JobCancelled
            or EventTypes.JobTimedOut
            or EventTypes.JobDeadLettered
            or EventTypes.CheckpointSaved
            or EventTypes.CheckpointRestored;
    }

    private static List<T> SampleEvenly<T>(List<T> source, int count)
    {
        if (source.Count <= count) return source;

        var result = new List<T>(count);
        var step = (double)source.Count / count;

        for (int i = 0; i < count; i++)
        {
            var index = (int)(i * step);
            result.Add(source[index]);
        }

        return result;
    }

    private JobStateSnapshot ReconstructState(
        Guid runId,
        List<JobEvent> events,
        DateTimeOffset snapshotAt)
    {
        var state = new JobStateSnapshot
        {
            RunId = runId,
            SnapshotAt = snapshotAt,
            EventsApplied = events.Count
        };

        if (events.Count == 0)
        {
            return state with { Status = "unknown" };
        }

        var firstEvent = events.First();
        var status = "pending";
        int attemptNumber = 1;
        int? progressPercentage = null;
        int? itemsProcessed = null;
        string? lastCheckpointKey = null;
        string? currentError = null;
        string? currentWorker = null;

        foreach (var @event in events)
        {
            switch (@event.EventType)
            {
                case EventTypes.JobScheduled:
                case EventTypes.JobEnqueued:
                    status = "pending";
                    break;

                case EventTypes.JobStarted:
                    status = "running";
                    var startPayload = TryDeserialize<JobStartedPayload>(@event.PayloadJson);
                    if (startPayload != null)
                    {
                        attemptNumber = startPayload.AttemptNumber;
                        currentWorker = startPayload.WorkerId;
                    }
                    currentError = null;
                    break;

                case EventTypes.JobCompleted:
                    status = "completed";
                    var completePayload = TryDeserialize<JobCompletedPayload>(@event.PayloadJson);
                    if (completePayload != null)
                    {
                        itemsProcessed = completePayload.ItemsProcessed;
                    }
                    progressPercentage = 100;
                    break;

                case EventTypes.JobFailed:
                    status = "failed";
                    var failPayload = TryDeserialize<JobFailedPayload>(@event.PayloadJson);
                    if (failPayload != null)
                    {
                        currentError = failPayload.ErrorMessage;
                        attemptNumber = failPayload.AttemptNumber;
                    }
                    break;

                case EventTypes.JobRetrying:
                    status = "retrying";
                    var retryPayload = TryDeserialize<JobRetryingPayload>(@event.PayloadJson);
                    if (retryPayload != null)
                    {
                        attemptNumber = retryPayload.NextAttempt;
                        currentError = retryPayload.ErrorMessage;
                    }
                    break;

                case EventTypes.JobCancelled:
                    status = "cancelled";
                    break;

                case EventTypes.JobTimedOut:
                    status = "timed_out";
                    break;

                case EventTypes.JobDeadLettered:
                    status = "dead_lettered";
                    break;

                case EventTypes.ProgressUpdated:
                    var progressPayload = TryDeserialize<ProgressUpdatedPayload>(@event.PayloadJson);
                    if (progressPayload != null)
                    {
                        progressPercentage = (int)progressPayload.Percentage;
                    }
                    break;

                case EventTypes.ItemProcessed:
                    var itemPayload = TryDeserialize<ItemProcessedPayload>(@event.PayloadJson);
                    if (itemPayload != null)
                    {
                        itemsProcessed = itemPayload.Index + 1;
                    }
                    break;

                case EventTypes.CheckpointSaved:
                    var cpPayload = TryDeserialize<CheckpointSavedPayload>(@event.PayloadJson);
                    if (cpPayload != null)
                    {
                        lastCheckpointKey = cpPayload.Key;
                    }
                    break;

                case EventTypes.CheckpointRestored:
                    var restorePayload = TryDeserialize<CheckpointRestoredPayload>(@event.PayloadJson);
                    if (restorePayload != null)
                    {
                        lastCheckpointKey = restorePayload.Key;
                        itemsProcessed = restorePayload.ItemsProcessedBefore;
                    }
                    break;
            }
        }

        var elapsedMs = (long)(snapshotAt - firstEvent.Timestamp).TotalMilliseconds;

        return state with
        {
            Status = status,
            AttemptNumber = attemptNumber,
            ProgressPercentage = progressPercentage,
            ItemsProcessed = itemsProcessed,
            LastCheckpointKey = lastCheckpointKey,
            CurrentError = currentError,
            CurrentWorker = currentWorker,
            ElapsedMs = elapsedMs
        };
    }

    #endregion

    #region Cleanup Operations

    public Task<int> DeleteEventsAsync(Guid runId, CancellationToken ct = default)
    {
        return _storage.DeleteEventsForRunAsync(runId, ct);
    }

    public Task<int> DeleteOldEventsAsync(TimeSpan maxAge, CancellationToken ct = default)
    {
        return _storage.DeleteOldEventsAsync(maxAge, ct);
    }

    public async Task<int> ApplyRetentionPolicyAsync(
        EventRetentionPolicy policy,
        CancellationToken ct = default)
    {
        int totalDeleted = 0;

        // Apply default retention
        totalDeleted += await _storage.DeleteOldEventsAsync(policy.DefaultRetention, ct);

        // Category-specific retention would require more sophisticated querying
        // For now, the default retention is applied

        return totalDeleted;
    }

    #endregion

    #region Export Operations

    public async Task<string> ExportAsync(
        Guid runId,
        EventExportFormat format,
        CancellationToken ct = default)
    {
        var events = await _storage.GetEventsAsync(runId, ct);

        return format switch
        {
            EventExportFormat.Json => JsonSerializer.Serialize(events, _jsonOptions),
            EventExportFormat.JsonLines => ExportAsJsonLines(events),
            EventExportFormat.Csv => ExportAsCsv(events),
            EventExportFormat.Text => ExportAsText(events),
            _ => throw new ArgumentOutOfRangeException(nameof(format))
        };
    }

    public async IAsyncEnumerable<JobEvent> StreamEventsAsync(
        Guid runId,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        long lastSequence = -1;
        const int batchSize = 100;

        while (!ct.IsCancellationRequested)
        {
            var batch = await _storage.GetEventsAfterSequenceAsync(runId, lastSequence, batchSize, ct);

            if (batch.Count == 0)
                yield break;

            foreach (var @event in batch)
            {
                yield return @event;
                lastSequence = @event.SequenceNumber;
            }

            if (batch.Count < batchSize)
                yield break;
        }
    }

    private string ExportAsJsonLines(IReadOnlyList<JobEvent> events)
    {
        return string.Join("\n", events.Select(e => JsonSerializer.Serialize(e, _jsonOptions)));
    }

    private string ExportAsCsv(IReadOnlyList<JobEvent> events)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("Id,RunId,EventType,Category,SequenceNumber,Timestamp,DurationMs,IsSuccess,Actor,Source");

        foreach (var e in events)
        {
            sb.AppendLine($"{e.Id},{e.RunId},{e.EventType},{e.Category},{e.SequenceNumber},{e.Timestamp:O},{e.DurationMs},{e.IsSuccess},{e.Actor},{e.Source}");
        }

        return sb.ToString();
    }

    private string ExportAsText(IReadOnlyList<JobEvent> events)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"Event History for Run (Count: {events.Count})");
        sb.AppendLine(new string('-', 80));

        foreach (var e in events.OrderBy(x => x.SequenceNumber))
        {
            var status = e.IsSuccess switch
            {
                true => "[OK]",
                false => "[FAIL]",
                _ => ""
            };

            var duration = e.DurationMs.HasValue ? $" ({e.DurationMs}ms)" : "";

            sb.AppendLine($"{e.Timestamp:HH:mm:ss.fff} {status} {e.EventType}{duration}");

            if (!string.IsNullOrEmpty(e.PayloadJson) && e.PayloadJson != "{}")
            {
                sb.AppendLine($"    Payload: {e.PayloadJson}");
            }
        }

        return sb.ToString();
    }

    #endregion

    #region Helper Methods

    private string GetEventTitle(JobEvent @event)
    {
        return @event.EventType switch
        {
            EventTypes.JobScheduled => "Job Scheduled",
            EventTypes.JobEnqueued => "Job Enqueued",
            EventTypes.JobStarted => "Job Started",
            EventTypes.JobCompleted => "Job Completed",
            EventTypes.JobFailed => "Job Failed",
            EventTypes.JobRetrying => "Retrying",
            EventTypes.JobCancelled => "Job Cancelled",
            EventTypes.JobTimedOut => "Job Timed Out",
            EventTypes.JobDeadLettered => "Moved to Dead Letter",
            EventTypes.CheckpointSaved => "Checkpoint Saved",
            EventTypes.CheckpointRestored => "Checkpoint Restored",
            EventTypes.ProgressUpdated => "Progress Updated",
            EventTypes.ItemProcessed => "Item Processed",
            EventTypes.MilestoneReached => "Milestone Reached",
            _ => @event.EventType
        };
    }

    private string? GetEventDescription(JobEvent @event)
    {
        try
        {
            return @event.EventType switch
            {
                EventTypes.JobStarted => GetDescription<JobStartedPayload>(@event,
                    p => $"Worker: {p.WorkerId}, Attempt: {p.AttemptNumber}"),
                EventTypes.JobCompleted => GetDescription<JobCompletedPayload>(@event,
                    p => $"Duration: {p.DurationMs}ms, Items: {p.ItemsProcessed ?? 0}"),
                EventTypes.JobFailed => GetDescription<JobFailedPayload>(@event,
                    p => p.ErrorMessage),
                EventTypes.JobRetrying => GetDescription<JobRetryingPayload>(@event,
                    p => $"Attempt {p.NextAttempt}/{p.MaxRetries}, Delay: {p.DelayMs}ms"),
                EventTypes.ProgressUpdated => GetDescription<ProgressUpdatedPayload>(@event,
                    p => p.Message ?? $"{p.Current}/{p.Total} ({p.Percentage:F1}%)"),
                EventTypes.CheckpointSaved => GetDescription<CheckpointSavedPayload>(@event,
                    p => $"Key: {p.Key}, Size: {p.DataSizeBytes} bytes"),
                _ => null
            };
        }
        catch
        {
            return null;
        }
    }

    private string? GetDescription<T>(JobEvent @event, Func<T, string> formatter) where T : class
    {
        var payload = TryDeserialize<T>(@event.PayloadJson);
        return payload != null ? formatter(payload) : null;
    }

    private EventSeverity GetEventSeverity(JobEvent @event)
    {
        return @event.EventType switch
        {
            EventTypes.JobFailed or EventTypes.ActivityFailed => EventSeverity.Error,
            EventTypes.JobRetrying => EventSeverity.Warning,
            EventTypes.JobDeadLettered => EventSeverity.Critical,
            EventTypes.JobTimedOut => EventSeverity.Error,
            EventTypes.JobCancelled => EventSeverity.Warning,
            _ => EventSeverity.Info
        };
    }

    private object? DeserializePayload(JobEvent @event)
    {
        if (string.IsNullOrEmpty(@event.PayloadJson) || @event.PayloadJson == "{}")
            return null;

        try
        {
            return JsonSerializer.Deserialize<object>(@event.PayloadJson, _jsonOptions);
        }
        catch
        {
            return null;
        }
    }

    private T? TryDeserialize<T>(string? json) where T : class
    {
        if (string.IsNullOrEmpty(json) || json == "{}")
            return null;

        try
        {
            return JsonSerializer.Deserialize<T>(json, _jsonOptions);
        }
        catch
        {
            return null;
        }
    }

    #endregion
}
