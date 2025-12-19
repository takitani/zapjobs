using System.Text.Json;
using ZapJobs.Core.History;

namespace ZapJobs.History;

/// <summary>
/// Provides time-travel debugging capabilities by replaying events and
/// reconstructing job state at any point in history.
/// </summary>
/// <remarks>
/// Unlike Temporal which requires deterministic replay for correctness,
/// ZapJobs events are purely observational. This makes time-travel simpler
/// and more flexible - we can reconstruct state without re-executing code.
/// </remarks>
public class EventReplayer
{
    private readonly IEventStore _eventStore;
    private readonly JsonSerializerOptions _jsonOptions;

    public EventReplayer(IEventStore eventStore)
    {
        _eventStore = eventStore;
        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };
    }

    /// <summary>
    /// Replay events and reconstruct state at each point.
    /// Useful for debugging and understanding job execution flow.
    /// </summary>
    public async Task<ReplayResult> ReplayAsync(
        Guid runId,
        ReplayOptions? options = null,
        CancellationToken ct = default)
    {
        options ??= new ReplayOptions();

        var events = await _eventStore.GetEventsAsync(runId, ct);
        if (events.Count == 0)
        {
            return new ReplayResult
            {
                RunId = runId,
                TotalEvents = 0,
                States = Array.Empty<ReplayStatePoint>(),
                Timeline = Array.Empty<ReplayTimelineEntry>()
            };
        }

        var orderedEvents = events
            .OrderBy(e => e.SequenceNumber)
            .ToList();

        // Filter events if needed
        if (options.FromSequence.HasValue)
        {
            orderedEvents = orderedEvents
                .Where(e => e.SequenceNumber >= options.FromSequence.Value)
                .ToList();
        }

        if (options.ToSequence.HasValue)
        {
            orderedEvents = orderedEvents
                .Where(e => e.SequenceNumber <= options.ToSequence.Value)
                .ToList();
        }

        if (options.FromTime.HasValue)
        {
            orderedEvents = orderedEvents
                .Where(e => e.Timestamp >= options.FromTime.Value)
                .ToList();
        }

        if (options.ToTime.HasValue)
        {
            orderedEvents = orderedEvents
                .Where(e => e.Timestamp <= options.ToTime.Value)
                .ToList();
        }

        if (options.EventTypes != null && options.EventTypes.Length > 0)
        {
            orderedEvents = orderedEvents
                .Where(e => options.EventTypes.Contains(e.EventType))
                .ToList();
        }

        // Build replay result
        var states = new List<ReplayStatePoint>();
        var timeline = new List<ReplayTimelineEntry>();
        var currentState = new ReplayState();
        DateTimeOffset? previousTimestamp = null;

        foreach (var @event in orderedEvents)
        {
            // Create timeline entry
            var timelineEntry = new ReplayTimelineEntry
            {
                EventId = @event.Id,
                SequenceNumber = @event.SequenceNumber,
                Timestamp = @event.Timestamp,
                EventType = @event.EventType,
                Category = @event.Category,
                TimeSincePreviousMs = previousTimestamp.HasValue
                    ? (long)(@event.Timestamp - previousTimestamp.Value).TotalMilliseconds
                    : null,
                DurationMs = @event.DurationMs,
                IsSuccess = @event.IsSuccess,
                Payload = options.IncludePayloads
                    ? @event.PayloadJson
                    : null,
                Actor = @event.Actor,
                Source = @event.Source
            };
            timeline.Add(timelineEntry);

            // Apply event to state
            ApplyEventToState(@event, currentState);

            // Capture state snapshot if requested or at key events
            if (options.CaptureAllStates || IsStateChangeEvent(@event.EventType))
            {
                states.Add(new ReplayStatePoint
                {
                    SequenceNumber = @event.SequenceNumber,
                    Timestamp = @event.Timestamp,
                    EventType = @event.EventType,
                    State = currentState.Clone()
                });
            }

            previousTimestamp = @event.Timestamp;
        }

        // Analyze the replay
        var analysis = AnalyzeReplay(orderedEvents, states);

        return new ReplayResult
        {
            RunId = runId,
            TotalEvents = orderedEvents.Count,
            FirstEventAt = orderedEvents.First().Timestamp,
            LastEventAt = orderedEvents.Last().Timestamp,
            TotalDurationMs = (long)(orderedEvents.Last().Timestamp - orderedEvents.First().Timestamp).TotalMilliseconds,
            FinalState = currentState,
            States = states,
            Timeline = timeline,
            Analysis = analysis
        };
    }

    /// <summary>
    /// Compare two points in time and show what changed.
    /// </summary>
    public async Task<StateDiff> CompareStatesAsync(
        Guid runId,
        DateTimeOffset time1,
        DateTimeOffset time2,
        CancellationToken ct = default)
    {
        var state1 = await _eventStore.GetStateAtAsync(runId, time1, ct);
        var state2 = await _eventStore.GetStateAtAsync(runId, time2, ct);

        return new StateDiff
        {
            RunId = runId,
            Time1 = time1,
            Time2 = time2,
            State1 = state1,
            State2 = state2,
            Changes = ComputeChanges(state1, state2)
        };
    }

    /// <summary>
    /// Find events matching specific criteria.
    /// </summary>
    public async Task<IReadOnlyList<JobEvent>> FindEventsAsync(
        Guid runId,
        Func<JobEvent, bool> predicate,
        CancellationToken ct = default)
    {
        var events = await _eventStore.GetEventsAsync(runId, ct);
        return events.Where(predicate).ToList();
    }

    /// <summary>
    /// Find the first event of a specific type.
    /// </summary>
    public async Task<JobEvent?> FindFirstEventAsync(
        Guid runId,
        string eventType,
        CancellationToken ct = default)
    {
        var events = await _eventStore.GetEventsAsync(runId, ct);
        return events
            .Where(e => e.EventType == eventType)
            .OrderBy(e => e.SequenceNumber)
            .FirstOrDefault();
    }

    /// <summary>
    /// Calculate the time spent in each state.
    /// </summary>
    public async Task<Dictionary<string, TimeSpan>> GetTimeInStatesAsync(
        Guid runId,
        CancellationToken ct = default)
    {
        var events = await _eventStore.GetEventsAsync(runId, ct);
        var timeInStates = new Dictionary<string, TimeSpan>();

        if (events.Count == 0)
            return timeInStates;

        var orderedEvents = events.OrderBy(e => e.SequenceNumber).ToList();
        string currentState = "unknown";
        DateTimeOffset lastTransition = orderedEvents.First().Timestamp;

        foreach (var @event in orderedEvents)
        {
            var newState = GetStateFromEvent(@event);
            if (newState != null && newState != currentState)
            {
                // Record time in previous state
                var duration = @event.Timestamp - lastTransition;
                if (timeInStates.ContainsKey(currentState))
                    timeInStates[currentState] += duration;
                else
                    timeInStates[currentState] = duration;

                currentState = newState;
                lastTransition = @event.Timestamp;
            }
        }

        // Add final state duration (until last event)
        var finalDuration = orderedEvents.Last().Timestamp - lastTransition;
        if (timeInStates.ContainsKey(currentState))
            timeInStates[currentState] += finalDuration;
        else
            timeInStates[currentState] = finalDuration;

        return timeInStates;
    }

    /// <summary>
    /// Get detailed error analysis from events.
    /// </summary>
    public async Task<ErrorAnalysis> AnalyzeErrorsAsync(
        Guid runId,
        CancellationToken ct = default)
    {
        var events = await _eventStore.GetEventsAsync(runId, ct);

        var failedEvents = events
            .Where(e => e.EventType == EventTypes.JobFailed ||
                        e.EventType == EventTypes.ActivityFailed)
            .OrderBy(e => e.SequenceNumber)
            .ToList();

        var retryEvents = events
            .Where(e => e.EventType == EventTypes.JobRetrying)
            .OrderBy(e => e.SequenceNumber)
            .ToList();

        var errors = new List<ErrorEntry>();

        foreach (var @event in failedEvents)
        {
            var payload = TryDeserialize<JobFailedPayload>(@event.PayloadJson);
            if (payload != null)
            {
                errors.Add(new ErrorEntry
                {
                    Timestamp = @event.Timestamp,
                    AttemptNumber = payload.AttemptNumber,
                    ErrorMessage = payload.ErrorMessage,
                    ErrorType = payload.ErrorType,
                    StackTrace = payload.StackTrace,
                    WillRetry = payload.WillRetry,
                    MovedToDeadLetter = payload.MovedToDeadLetter
                });
            }
        }

        return new ErrorAnalysis
        {
            RunId = runId,
            TotalErrors = errors.Count,
            TotalRetries = retryEvents.Count,
            Errors = errors,
            FirstErrorAt = errors.FirstOrDefault()?.Timestamp,
            LastErrorAt = errors.LastOrDefault()?.Timestamp,
            UniqueErrorTypes = errors.Select(e => e.ErrorType).Distinct().ToList()!
        };
    }

    /// <summary>
    /// Get progress history for a job.
    /// </summary>
    public async Task<ProgressHistory> GetProgressHistoryAsync(
        Guid runId,
        CancellationToken ct = default)
    {
        var events = await _eventStore.GetEventsAsync(runId, ct);

        var progressEvents = events
            .Where(e => e.EventType == EventTypes.ProgressUpdated)
            .OrderBy(e => e.SequenceNumber)
            .ToList();

        var itemEvents = events
            .Where(e => e.EventType == EventTypes.ItemProcessed)
            .OrderBy(e => e.SequenceNumber)
            .ToList();

        var milestones = events
            .Where(e => e.EventType == EventTypes.MilestoneReached)
            .OrderBy(e => e.SequenceNumber)
            .ToList();

        var progressPoints = new List<ProgressPoint>();

        foreach (var @event in progressEvents)
        {
            var payload = TryDeserialize<ProgressUpdatedPayload>(@event.PayloadJson);
            if (payload != null)
            {
                progressPoints.Add(new ProgressPoint
                {
                    Timestamp = @event.Timestamp,
                    Current = payload.Current,
                    Total = payload.Total,
                    Percentage = payload.Percentage,
                    Message = payload.Message
                });
            }
        }

        var items = new List<ItemProgress>();
        int successCount = 0;
        int failureCount = 0;

        foreach (var @event in itemEvents)
        {
            var payload = TryDeserialize<ItemProcessedPayload>(@event.PayloadJson);
            if (payload != null)
            {
                items.Add(new ItemProgress
                {
                    Timestamp = @event.Timestamp,
                    ItemId = payload.ItemId,
                    Index = payload.Index,
                    Success = payload.Success,
                    ProcessingTimeMs = payload.ProcessingTimeMs,
                    ErrorMessage = payload.ErrorMessage
                });

                if (payload.Success)
                    successCount++;
                else
                    failureCount++;
            }
        }

        var milestoneList = new List<MilestoneEntry>();
        foreach (var @event in milestones)
        {
            var payload = TryDeserialize<MilestoneReachedPayload>(@event.PayloadJson);
            if (payload != null)
            {
                milestoneList.Add(new MilestoneEntry
                {
                    Timestamp = @event.Timestamp,
                    Name = payload.MilestoneName,
                    ItemsProcessed = payload.ItemsProcessed,
                    Description = payload.Description
                });
            }
        }

        // Calculate throughput
        double? itemsPerSecond = null;
        if (items.Count >= 2)
        {
            var duration = (items.Last().Timestamp - items.First().Timestamp).TotalSeconds;
            if (duration > 0)
            {
                itemsPerSecond = items.Count / duration;
            }
        }

        return new ProgressHistory
        {
            RunId = runId,
            ProgressPoints = progressPoints,
            Items = items,
            Milestones = milestoneList,
            TotalItems = items.Count,
            SuccessfulItems = successCount,
            FailedItems = failureCount,
            AverageItemTimeMs = items.Count > 0
                ? items.Average(i => i.ProcessingTimeMs)
                : null,
            ItemsPerSecond = itemsPerSecond
        };
    }

    #region Private Helpers

    private void ApplyEventToState(JobEvent @event, ReplayState state)
    {
        state.LastEventType = @event.EventType;
        state.LastEventTimestamp = @event.Timestamp;
        state.EventCount++;

        switch (@event.EventType)
        {
            case EventTypes.JobScheduled:
            case EventTypes.JobEnqueued:
                state.Status = "pending";
                break;

            case EventTypes.JobStarted:
                state.Status = "running";
                var startPayload = TryDeserialize<JobStartedPayload>(@event.PayloadJson);
                if (startPayload != null)
                {
                    state.AttemptNumber = startPayload.AttemptNumber;
                    state.CurrentWorker = startPayload.WorkerId;
                    state.IsResuming = startPayload.IsResuming;
                }
                state.LastError = null;
                break;

            case EventTypes.JobCompleted:
                state.Status = "completed";
                var completePayload = TryDeserialize<JobCompletedPayload>(@event.PayloadJson);
                if (completePayload != null)
                {
                    state.ItemsProcessed = completePayload.ItemsProcessed;
                    state.SucceededCount = completePayload.SucceededCount;
                    state.FailedCount = completePayload.FailedCount;
                }
                state.ProgressPercentage = 100;
                break;

            case EventTypes.JobFailed:
                state.Status = "failed";
                var failPayload = TryDeserialize<JobFailedPayload>(@event.PayloadJson);
                if (failPayload != null)
                {
                    state.LastError = failPayload.ErrorMessage;
                    state.AttemptNumber = failPayload.AttemptNumber;
                    state.WillRetry = failPayload.WillRetry;
                }
                break;

            case EventTypes.JobRetrying:
                state.Status = "retrying";
                var retryPayload = TryDeserialize<JobRetryingPayload>(@event.PayloadJson);
                if (retryPayload != null)
                {
                    state.AttemptNumber = retryPayload.NextAttempt;
                    state.LastError = retryPayload.ErrorMessage;
                }
                break;

            case EventTypes.JobCancelled:
                state.Status = "cancelled";
                break;

            case EventTypes.JobTimedOut:
                state.Status = "timed_out";
                break;

            case EventTypes.JobDeadLettered:
                state.Status = "dead_lettered";
                break;

            case EventTypes.ProgressUpdated:
                var progressPayload = TryDeserialize<ProgressUpdatedPayload>(@event.PayloadJson);
                if (progressPayload != null)
                {
                    state.ProgressPercentage = (int)progressPayload.Percentage;
                    state.ProgressMessage = progressPayload.Message;
                }
                break;

            case EventTypes.ItemProcessed:
                var itemPayload = TryDeserialize<ItemProcessedPayload>(@event.PayloadJson);
                if (itemPayload != null)
                {
                    state.ItemsProcessed = (state.ItemsProcessed ?? 0) + 1;
                    if (itemPayload.Success)
                        state.SucceededCount++;
                    else
                        state.FailedCount++;
                }
                break;

            case EventTypes.CheckpointSaved:
                var cpPayload = TryDeserialize<CheckpointSavedPayload>(@event.PayloadJson);
                if (cpPayload != null)
                {
                    state.LastCheckpointKey = cpPayload.Key;
                    state.LastCheckpointSequence = cpPayload.SequenceNumber;
                }
                break;

            case EventTypes.CheckpointRestored:
                var restorePayload = TryDeserialize<CheckpointRestoredPayload>(@event.PayloadJson);
                if (restorePayload != null)
                {
                    state.LastCheckpointKey = restorePayload.Key;
                    state.ItemsProcessed = restorePayload.ItemsProcessedBefore;
                }
                break;
        }
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

    private static string? GetStateFromEvent(JobEvent @event)
    {
        return @event.EventType switch
        {
            EventTypes.JobScheduled or EventTypes.JobEnqueued => "pending",
            EventTypes.JobStarted => "running",
            EventTypes.JobCompleted => "completed",
            EventTypes.JobFailed => "failed",
            EventTypes.JobRetrying => "retrying",
            EventTypes.JobCancelled => "cancelled",
            EventTypes.JobTimedOut => "timed_out",
            EventTypes.JobDeadLettered => "dead_lettered",
            _ => null
        };
    }

    private ReplayAnalysis AnalyzeReplay(List<JobEvent> events, List<ReplayStatePoint> states)
    {
        var analysis = new ReplayAnalysis();

        // Count events by type
        analysis.EventCountsByType = events
            .GroupBy(e => e.EventType)
            .ToDictionary(g => g.Key, g => g.Count());

        // Count events by category
        analysis.EventCountsByCategory = events
            .GroupBy(e => e.Category)
            .ToDictionary(g => g.Key, g => g.Count());

        // Find state transitions
        string? previousState = null;
        foreach (var state in states)
        {
            var currentState = state.State.Status;
            if (currentState != previousState && previousState != null)
            {
                var transition = $"{previousState} -> {currentState}";
                if (analysis.StateTransitions.ContainsKey(transition))
                    analysis.StateTransitions[transition]++;
                else
                    analysis.StateTransitions[transition] = 1;
            }
            previousState = currentState;
        }

        // Calculate time gaps
        if (events.Count >= 2)
        {
            var gaps = new List<long>();
            for (int i = 1; i < events.Count; i++)
            {
                var gap = (long)(events[i].Timestamp - events[i - 1].Timestamp).TotalMilliseconds;
                gaps.Add(gap);
            }

            analysis.AverageEventGapMs = gaps.Average();
            analysis.MaxEventGapMs = gaps.Max();
            analysis.MinEventGapMs = gaps.Min();
        }

        // Check for anomalies
        analysis.HasRetries = events.Any(e => e.EventType == EventTypes.JobRetrying);
        analysis.HasErrors = events.Any(e => e.IsSuccess == false);
        analysis.RetryCount = events.Count(e => e.EventType == EventTypes.JobRetrying);
        analysis.ErrorCount = events.Count(e => e.IsSuccess == false);

        return analysis;
    }

    private List<StateChange> ComputeChanges(JobStateSnapshot state1, JobStateSnapshot state2)
    {
        var changes = new List<StateChange>();

        if (state1.Status != state2.Status)
            changes.Add(new StateChange("Status", state1.Status, state2.Status));

        if (state1.AttemptNumber != state2.AttemptNumber)
            changes.Add(new StateChange("AttemptNumber", state1.AttemptNumber.ToString(), state2.AttemptNumber.ToString()));

        if (state1.ProgressPercentage != state2.ProgressPercentage)
            changes.Add(new StateChange("ProgressPercentage", state1.ProgressPercentage?.ToString() ?? "null", state2.ProgressPercentage?.ToString() ?? "null"));

        if (state1.ItemsProcessed != state2.ItemsProcessed)
            changes.Add(new StateChange("ItemsProcessed", state1.ItemsProcessed?.ToString() ?? "null", state2.ItemsProcessed?.ToString() ?? "null"));

        if (state1.CurrentError != state2.CurrentError)
            changes.Add(new StateChange("CurrentError", state1.CurrentError ?? "null", state2.CurrentError ?? "null"));

        if (state1.CurrentWorker != state2.CurrentWorker)
            changes.Add(new StateChange("CurrentWorker", state1.CurrentWorker ?? "null", state2.CurrentWorker ?? "null"));

        if (state1.LastCheckpointKey != state2.LastCheckpointKey)
            changes.Add(new StateChange("LastCheckpointKey", state1.LastCheckpointKey ?? "null", state2.LastCheckpointKey ?? "null"));

        return changes;
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

#region Result Types

/// <summary>
/// Options for replay operation.
/// </summary>
public class ReplayOptions
{
    /// <summary>Start from this sequence number</summary>
    public long? FromSequence { get; set; }

    /// <summary>End at this sequence number</summary>
    public long? ToSequence { get; set; }

    /// <summary>Start from this time</summary>
    public DateTimeOffset? FromTime { get; set; }

    /// <summary>End at this time</summary>
    public DateTimeOffset? ToTime { get; set; }

    /// <summary>Only include these event types</summary>
    public string[]? EventTypes { get; set; }

    /// <summary>Capture state at every event (vs only state-changing events)</summary>
    public bool CaptureAllStates { get; set; }

    /// <summary>Include raw payloads in timeline</summary>
    public bool IncludePayloads { get; set; }
}

/// <summary>
/// Result of replaying events.
/// </summary>
public class ReplayResult
{
    public Guid RunId { get; set; }
    public int TotalEvents { get; set; }
    public DateTimeOffset? FirstEventAt { get; set; }
    public DateTimeOffset? LastEventAt { get; set; }
    public long? TotalDurationMs { get; set; }
    public ReplayState? FinalState { get; set; }
    public IReadOnlyList<ReplayStatePoint> States { get; set; } = Array.Empty<ReplayStatePoint>();
    public IReadOnlyList<ReplayTimelineEntry> Timeline { get; set; } = Array.Empty<ReplayTimelineEntry>();
    public ReplayAnalysis? Analysis { get; set; }
}

/// <summary>
/// State at a specific point during replay.
/// </summary>
public class ReplayStatePoint
{
    public long SequenceNumber { get; set; }
    public DateTimeOffset Timestamp { get; set; }
    public string EventType { get; set; } = string.Empty;
    public ReplayState State { get; set; } = new();
}

/// <summary>
/// Timeline entry during replay.
/// </summary>
public class ReplayTimelineEntry
{
    public Guid EventId { get; set; }
    public long SequenceNumber { get; set; }
    public DateTimeOffset Timestamp { get; set; }
    public string EventType { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public long? TimeSincePreviousMs { get; set; }
    public long? DurationMs { get; set; }
    public bool? IsSuccess { get; set; }
    public string? Payload { get; set; }
    public string? Actor { get; set; }
    public string? Source { get; set; }
}

/// <summary>
/// Reconstructed state during replay.
/// </summary>
public class ReplayState
{
    public string Status { get; set; } = "unknown";
    public int AttemptNumber { get; set; } = 1;
    public int? ProgressPercentage { get; set; }
    public string? ProgressMessage { get; set; }
    public int? ItemsProcessed { get; set; }
    public int SucceededCount { get; set; }
    public int FailedCount { get; set; }
    public string? LastCheckpointKey { get; set; }
    public int? LastCheckpointSequence { get; set; }
    public string? LastError { get; set; }
    public bool? WillRetry { get; set; }
    public string? CurrentWorker { get; set; }
    public bool IsResuming { get; set; }
    public string? LastEventType { get; set; }
    public DateTimeOffset? LastEventTimestamp { get; set; }
    public int EventCount { get; set; }

    public ReplayState Clone()
    {
        return new ReplayState
        {
            Status = Status,
            AttemptNumber = AttemptNumber,
            ProgressPercentage = ProgressPercentage,
            ProgressMessage = ProgressMessage,
            ItemsProcessed = ItemsProcessed,
            SucceededCount = SucceededCount,
            FailedCount = FailedCount,
            LastCheckpointKey = LastCheckpointKey,
            LastCheckpointSequence = LastCheckpointSequence,
            LastError = LastError,
            WillRetry = WillRetry,
            CurrentWorker = CurrentWorker,
            IsResuming = IsResuming,
            LastEventType = LastEventType,
            LastEventTimestamp = LastEventTimestamp,
            EventCount = EventCount
        };
    }
}

/// <summary>
/// Analysis of replayed events.
/// </summary>
public class ReplayAnalysis
{
    public Dictionary<string, int> EventCountsByType { get; set; } = new();
    public Dictionary<string, int> EventCountsByCategory { get; set; } = new();
    public Dictionary<string, int> StateTransitions { get; set; } = new();
    public double? AverageEventGapMs { get; set; }
    public long? MaxEventGapMs { get; set; }
    public long? MinEventGapMs { get; set; }
    public bool HasRetries { get; set; }
    public bool HasErrors { get; set; }
    public int RetryCount { get; set; }
    public int ErrorCount { get; set; }
}

/// <summary>
/// Comparison between two states.
/// </summary>
public class StateDiff
{
    public Guid RunId { get; set; }
    public DateTimeOffset Time1 { get; set; }
    public DateTimeOffset Time2 { get; set; }
    public JobStateSnapshot? State1 { get; set; }
    public JobStateSnapshot? State2 { get; set; }
    public List<StateChange> Changes { get; set; } = new();
}

/// <summary>
/// A single state change.
/// </summary>
public record StateChange(string Property, string OldValue, string NewValue);

/// <summary>
/// Error analysis results.
/// </summary>
public class ErrorAnalysis
{
    public Guid RunId { get; set; }
    public int TotalErrors { get; set; }
    public int TotalRetries { get; set; }
    public List<ErrorEntry> Errors { get; set; } = new();
    public DateTimeOffset? FirstErrorAt { get; set; }
    public DateTimeOffset? LastErrorAt { get; set; }
    public List<string> UniqueErrorTypes { get; set; } = new();
}

/// <summary>
/// A single error entry.
/// </summary>
public class ErrorEntry
{
    public DateTimeOffset Timestamp { get; set; }
    public int AttemptNumber { get; set; }
    public string ErrorMessage { get; set; } = string.Empty;
    public string? ErrorType { get; set; }
    public string? StackTrace { get; set; }
    public bool WillRetry { get; set; }
    public bool MovedToDeadLetter { get; set; }
}

/// <summary>
/// Progress history for a job.
/// </summary>
public class ProgressHistory
{
    public Guid RunId { get; set; }
    public List<ProgressPoint> ProgressPoints { get; set; } = new();
    public List<ItemProgress> Items { get; set; } = new();
    public List<MilestoneEntry> Milestones { get; set; } = new();
    public int TotalItems { get; set; }
    public int SuccessfulItems { get; set; }
    public int FailedItems { get; set; }
    public double? AverageItemTimeMs { get; set; }
    public double? ItemsPerSecond { get; set; }
}

/// <summary>
/// A progress update point.
/// </summary>
public class ProgressPoint
{
    public DateTimeOffset Timestamp { get; set; }
    public int Current { get; set; }
    public int Total { get; set; }
    public double Percentage { get; set; }
    public string? Message { get; set; }
}

/// <summary>
/// An individual item's progress.
/// </summary>
public class ItemProgress
{
    public DateTimeOffset Timestamp { get; set; }
    public string? ItemId { get; set; }
    public int Index { get; set; }
    public bool Success { get; set; }
    public long ProcessingTimeMs { get; set; }
    public string? ErrorMessage { get; set; }
}

/// <summary>
/// A milestone entry.
/// </summary>
public class MilestoneEntry
{
    public DateTimeOffset Timestamp { get; set; }
    public string Name { get; set; } = string.Empty;
    public int ItemsProcessed { get; set; }
    public string? Description { get; set; }
}

#endregion
