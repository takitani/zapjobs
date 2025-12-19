namespace ZapJobs.Core.History;

/// <summary>
/// Represents an immutable event in a job's execution history.
/// Events are append-only and provide a complete audit trail.
/// </summary>
/// <remarks>
/// Unlike Temporal which requires deterministic replay, ZapJobs events
/// are purely observational - they don't affect job execution.
/// Unlike Hangfire which only has text logs, events are structured and queryable.
/// </remarks>
public class JobEvent
{
    /// <summary>Unique event identifier</summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>The job run this event belongs to</summary>
    public Guid RunId { get; set; }

    /// <summary>Optional workflow/batch ID for correlated events</summary>
    public Guid? WorkflowId { get; set; }

    /// <summary>Event type from EventTypes constants</summary>
    public string EventType { get; set; } = string.Empty;

    /// <summary>Event category for filtering (job, activity, checkpoint, custom)</summary>
    public string Category { get; set; } = EventCategories.Job;

    /// <summary>Global sequence number for ordering across all events</summary>
    public long SequenceNumber { get; set; }

    /// <summary>Sequence within the run (1, 2, 3...)</summary>
    public int RunSequence { get; set; }

    /// <summary>When the event occurred (UTC)</summary>
    public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>Structured event payload as JSON</summary>
    public string PayloadJson { get; set; } = "{}";

    /// <summary>
    /// Correlation ID to track related events across jobs.
    /// Enables distributed tracing.
    /// </summary>
    public string? CorrelationId { get; set; }

    /// <summary>
    /// Causation ID - the event that caused this event.
    /// Creates a causal chain for debugging.
    /// </summary>
    public string? CausationId { get; set; }

    /// <summary>Actor who triggered the event (user, system, worker)</summary>
    public string? Actor { get; set; }

    /// <summary>Source system/worker that generated the event</summary>
    public string? Source { get; set; }

    /// <summary>Optional tags for filtering and search</summary>
    public string[]? Tags { get; set; }

    /// <summary>Event schema version for migrations</summary>
    public int Version { get; set; } = 1;

    /// <summary>Duration in milliseconds (for timed events)</summary>
    public long? DurationMs { get; set; }

    /// <summary>Whether this event indicates success</summary>
    public bool? IsSuccess { get; set; }

    /// <summary>Additional metadata as JSON</summary>
    public string? MetadataJson { get; set; }
}

/// <summary>
/// Event categories for filtering and organization.
/// </summary>
public static class EventCategories
{
    /// <summary>Core job lifecycle events</summary>
    public const string Job = "job";

    /// <summary>Activity/step events within a job</summary>
    public const string Activity = "activity";

    /// <summary>Workflow/batch coordination events</summary>
    public const string Workflow = "workflow";

    /// <summary>State persistence events</summary>
    public const string State = "state";

    /// <summary>Progress and metrics events</summary>
    public const string Progress = "progress";

    /// <summary>System/infrastructure events</summary>
    public const string System = "system";

    /// <summary>User-defined custom events</summary>
    public const string Custom = "custom";
}

/// <summary>
/// Comprehensive event types covering all job lifecycle scenarios.
/// More complete than Temporal or Hangfire.
/// </summary>
public static class EventTypes
{
    #region Job Lifecycle

    /// <summary>Job was scheduled for execution</summary>
    public const string JobScheduled = "job.scheduled";

    /// <summary>Job was enqueued for immediate execution</summary>
    public const string JobEnqueued = "job.enqueued";

    /// <summary>Job execution started</summary>
    public const string JobStarted = "job.started";

    /// <summary>Job completed successfully</summary>
    public const string JobCompleted = "job.completed";

    /// <summary>Job failed with an error</summary>
    public const string JobFailed = "job.failed";

    /// <summary>Job will be retried</summary>
    public const string JobRetrying = "job.retrying";

    /// <summary>Job was cancelled</summary>
    public const string JobCancelled = "job.cancelled";

    /// <summary>Job timed out</summary>
    public const string JobTimedOut = "job.timedout";

    /// <summary>Job was skipped (e.g., rate limited with Skip behavior)</summary>
    public const string JobSkipped = "job.skipped";

    /// <summary>Job was delayed (e.g., rate limited with Delay behavior)</summary>
    public const string JobDelayed = "job.delayed";

    /// <summary>Job moved to dead letter queue</summary>
    public const string JobDeadLettered = "job.deadlettered";

    /// <summary>Job was requeued from dead letter</summary>
    public const string JobRequeued = "job.requeued";

    #endregion

    #region Activity Lifecycle (for future Durable Execution)

    /// <summary>Activity/step started</summary>
    public const string ActivityStarted = "activity.started";

    /// <summary>Activity/step completed</summary>
    public const string ActivityCompleted = "activity.completed";

    /// <summary>Activity/step failed</summary>
    public const string ActivityFailed = "activity.failed";

    /// <summary>Activity/step will be retried</summary>
    public const string ActivityRetrying = "activity.retrying";

    /// <summary>Activity was skipped</summary>
    public const string ActivitySkipped = "activity.skipped";

    #endregion

    #region Workflow Lifecycle

    /// <summary>Workflow/batch started</summary>
    public const string WorkflowStarted = "workflow.started";

    /// <summary>Workflow/batch completed</summary>
    public const string WorkflowCompleted = "workflow.completed";

    /// <summary>Workflow/batch failed</summary>
    public const string WorkflowFailed = "workflow.failed";

    /// <summary>Workflow/batch was cancelled</summary>
    public const string WorkflowCancelled = "workflow.cancelled";

    /// <summary>Step in workflow started</summary>
    public const string StepStarted = "workflow.step.started";

    /// <summary>Step in workflow completed</summary>
    public const string StepCompleted = "workflow.step.completed";

    /// <summary>Continuation job triggered</summary>
    public const string ContinuationTriggered = "workflow.continuation.triggered";

    #endregion

    #region Saga Events (for future Saga Pattern)

    /// <summary>Saga started</summary>
    public const string SagaStarted = "saga.started";

    /// <summary>Saga completed successfully</summary>
    public const string SagaCompleted = "saga.completed";

    /// <summary>Saga is compensating (rolling back)</summary>
    public const string SagaCompensating = "saga.compensating";

    /// <summary>Saga compensation completed</summary>
    public const string SagaCompensated = "saga.compensated";

    /// <summary>Individual compensation started</summary>
    public const string CompensationStarted = "saga.compensation.started";

    /// <summary>Individual compensation completed</summary>
    public const string CompensationCompleted = "saga.compensation.completed";

    /// <summary>Compensation failed</summary>
    public const string CompensationFailed = "saga.compensation.failed";

    #endregion

    #region State Events

    /// <summary>Checkpoint saved</summary>
    public const string CheckpointSaved = "state.checkpoint.saved";

    /// <summary>Checkpoint restored</summary>
    public const string CheckpointRestored = "state.checkpoint.restored";

    /// <summary>Checkpoints cleared</summary>
    public const string CheckpointsCleared = "state.checkpoints.cleared";

    /// <summary>Job output set</summary>
    public const string OutputSet = "state.output.set";

    #endregion

    #region Progress Events

    /// <summary>Progress updated</summary>
    public const string ProgressUpdated = "progress.updated";

    /// <summary>Item processed (for batch jobs)</summary>
    public const string ItemProcessed = "progress.item.processed";

    /// <summary>Milestone reached</summary>
    public const string MilestoneReached = "progress.milestone";

    /// <summary>Heartbeat received</summary>
    public const string Heartbeat = "progress.heartbeat";

    #endregion

    #region System Events

    /// <summary>Worker assigned to job</summary>
    public const string WorkerAssigned = "system.worker.assigned";

    /// <summary>Job picked up from queue</summary>
    public const string JobDequeued = "system.dequeued";

    /// <summary>Rate limit hit</summary>
    public const string RateLimitHit = "system.ratelimit.hit";

    /// <summary>Timer scheduled</summary>
    public const string TimerScheduled = "system.timer.scheduled";

    /// <summary>Timer fired</summary>
    public const string TimerFired = "system.timer.fired";

    /// <summary>Signal received</summary>
    public const string SignalReceived = "system.signal.received";

    #endregion

    #region Custom Events

    /// <summary>User-defined event</summary>
    public const string Custom = "custom";

    /// <summary>Log message (structured)</summary>
    public const string LogMessage = "custom.log";

    /// <summary>Metric recorded</summary>
    public const string MetricRecorded = "custom.metric";

    /// <summary>Business event</summary>
    public const string BusinessEvent = "custom.business";

    #endregion
}

/// <summary>
/// Severity levels for events.
/// </summary>
public enum EventSeverity
{
    /// <summary>Debug-level detail</summary>
    Debug = 0,

    /// <summary>Informational event</summary>
    Info = 1,

    /// <summary>Warning condition</summary>
    Warning = 2,

    /// <summary>Error condition</summary>
    Error = 3,

    /// <summary>Critical failure</summary>
    Critical = 4
}
