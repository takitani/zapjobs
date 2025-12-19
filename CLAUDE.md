# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Overview

**ZapJobs** is a database-driven background job library for .NET. It provides scheduled and on-demand job execution with persistence, retry logic, and monitoring - similar to Hangfire/Quartz but with a simpler, more modern API.

## Build & Run Commands

```bash
# Build
dotnet build ZapJobs.slnx

# Run tests (when added)
dotnet test

# Pack NuGet packages
dotnet pack -c Release
```

## Environment Setup

- .NET 10 (managed via `mise` in parent projects)
- No external dependencies for core library
- PostgreSQL required for production storage

## Architecture

```
ZapJobs.Core (Abstractions + Entities)
    ├── Abstractions/    → IJob, IJobStorage, IJobScheduler, IJobTracker, IJobLogger, IDeadLetterManager
    ├── Batches/         → IBatchBuilder, IBatchService
    ├── Checkpoints/     → ICheckpointStore, Checkpoint, CheckpointOptions, ResumeContext
    ├── Configuration/   → ZapJobsOptions, RetryPolicy
    ├── Context/         → JobExecutionContext<T>
    ├── Events/          → IJobEvent, IJobEventHandler, IJobEventDispatcher, Job*Event classes
    ├── History/         → JobEvent, EventTypes, EventCategories, EventPayloads, IEventPublisher
    ├── RateLimiting/    → IRateLimiter, RateLimitPolicy
    └── Entities/        → JobDefinition, JobRun, JobLog, JobHeartbeat, JobContinuation, DeadLetterEntry, JobBatch

ZapJobs (Runtime)
    ├── Batches/         → BatchBuilder, BatchService
    ├── Checkpoints/     → CheckpointService, CheckpointCleanupService
    ├── DeadLetter/      → DeadLetterManager
    ├── Events/          → JobEventDispatcher, JobEventBackgroundService
    ├── Execution/       → JobExecutor, RetryHandler
    ├── History/         → EventStore, EventPublisher, EventReplayer, EventHistoryHandler
    ├── RateLimiting/    → SlidingWindowRateLimiter, RateLimitCleanupService
    ├── Scheduling/      → JobSchedulerService, CronScheduler
    ├── Tracking/        → JobTrackerService, JobLoggerService, HeartbeatService
    └── HostedServices/  → JobProcessorHostedService, JobRegistrationHostedService

ZapJobs.Storage.InMemory (Dev/Test)
    └── InMemoryJobStorage.cs

ZapJobs.Storage.PostgreSQL (Production)
    ├── PostgreSqlJobStorage.cs
    └── Migrations/InitialCreate.sql
```

## Key Concepts

### Job Interface

Jobs implement `IJob<TInput>` with typed input:

```csharp
public record SendEmailInput(string To, string Subject, string Body);

public class SendEmailJob : IJob<SendEmailInput>
{
    public string JobTypeId => "send-email";

    public async Task ExecuteAsync(JobExecutionContext<SendEmailInput> context, CancellationToken ct)
    {
        var input = context.Input;
        await context.Logger.InfoAsync($"Sending email to {input.To}");
        // ... send email
        context.IncrementSucceeded();
    }
}
```

### Registration

```csharp
services
    .AddZapJobs(options =>
    {
        options.WorkerCount = 4;
        options.PollingInterval = TimeSpan.FromSeconds(5);
        options.DefaultQueue = "default";
        options.Queues = ["critical", "default", "low"];
    })
    .AddJob<SendEmailJob>()
    .AddJob<ProcessOrderJob>();

// Storage backend
services.UseInMemoryStorage();  // Dev
// OR
services.UsePostgreSqlStorage(connectionString);  // Production
```

### Scheduling Jobs

```csharp
// Inject IJobScheduler
public class MyService
{
    private readonly IJobScheduler _scheduler;

    // Schedule for later
    public async Task ScheduleEmail(SendEmailInput input)
    {
        await _scheduler.ScheduleAsync(
            "send-email",
            DateTimeOffset.UtcNow.AddMinutes(30),
            input);
    }

    // Enqueue immediately
    public async Task SendNow(SendEmailInput input)
    {
        await _scheduler.EnqueueAsync("send-email", input);
    }

    // Recurring (CRON)
    public async Task SetupDailyReport()
    {
        await _scheduler.ScheduleRecurringAsync(
            "daily-report",
            "0 8 * * *",  // Every day at 8 AM
            new ReportInput { Type = "daily" });
    }
}
```

### Job Continuations

Chain jobs to run after a parent completes:

```csharp
// Schedule a job with a continuation
var runId = await _scheduler.EnqueueAsync("process-data", data);

// Run on success (default)
await _scheduler.ContinueWithAsync(runId, "notify-job");

// Run on failure
await _scheduler.ContinueWithAsync(runId, "error-handler",
    condition: ContinuationCondition.OnFailure);

// Run always (cleanup)
await _scheduler.ContinueWithAsync(runId, "cleanup-job",
    condition: ContinuationCondition.Always);

// Pass parent output as continuation input
await _scheduler.ContinueWithAsync(runId, "follow-up-job",
    passParentOutput: true);
```

**Continuation Conditions:**
- `OnSuccess` - Run only if parent succeeds (default)
- `OnFailure` - Run only if parent fails permanently
- `Always` - Run regardless of outcome

### Dead Letter Queue

Jobs that fail permanently (after exhausting all retries) are automatically moved to the dead letter queue for review and potential reprocessing:

```csharp
// Inject IDeadLetterManager
public class AdminService
{
    private readonly IDeadLetterManager _dlq;

    // Get pending entries
    public async Task<IReadOnlyList<DeadLetterEntry>> GetFailedJobs()
    {
        return await _dlq.GetEntriesAsync(status: DeadLetterStatus.Pending);
    }

    // Requeue a failed job
    public async Task<Guid> RetryJob(Guid deadLetterId)
    {
        return await _dlq.RequeueAsync(deadLetterId);
    }

    // Requeue with modified input
    public async Task<Guid> RetryJobWithNewInput(Guid deadLetterId, string newInputJson)
    {
        return await _dlq.RequeueAsync(deadLetterId, newInput: newInputJson);
    }

    // Discard a failed job (won't be processed)
    public async Task DiscardJob(Guid deadLetterId)
    {
        await _dlq.DiscardAsync(deadLetterId, notes: "Known issue, not retrying");
    }

    // Archive for records
    public async Task ArchiveJob(Guid deadLetterId)
    {
        await _dlq.ArchiveAsync(deadLetterId, notes: "Investigated and resolved");
    }

    // Bulk operations
    public async Task<int> RequeueAllFailedEmailJobs()
    {
        return await _dlq.RequeueAllAsync("send-email");
    }
}
```

**Dead Letter Statuses:**
- `Pending` - Waiting for review
- `Requeued` - Sent back to processing
- `Discarded` - Manually discarded (won't be processed)
- `Archived` - Kept for records

### Batch Jobs

Create and manage groups of jobs as a single unit:

```csharp
// Inject IBatchService
public class MyService
{
    private readonly IBatchService _batchService;

    public async Task ProcessCustomers(IEnumerable<Customer> customers)
    {
        // Create a batch of jobs
        var batchId = await _batchService.CreateBatchAsync(
            "import-customers",
            batch =>
            {
                foreach (var customer in customers)
                {
                    batch.Enqueue("process-customer", new { CustomerId = customer.Id });
                }

                // Optional: run when batch completes
                batch.OnSuccess("send-summary-email", new { BatchId = "..." });
                batch.OnFailure("alert-admin", new { Reason = "Import failed" });
                batch.OnComplete("cleanup-temp-files");
            },
            createdBy: "import-service");

        // Check batch progress
        var batch = await _batchService.GetBatchAsync(batchId);
        Console.WriteLine($"Progress: {batch.CompletedJobs}/{batch.TotalJobs}");
    }
}
```

**Nested Batches:**

```csharp
await _batchService.CreateBatchAsync("parent-batch", parent =>
{
    parent.Enqueue("job-1");

    // Add a nested batch
    parent.AddBatch("child-batch", child =>
    {
        child.Enqueue("child-job-1");
        child.Enqueue("child-job-2");
        child.OnSuccess("child-complete-handler");
    });

    parent.OnSuccess("parent-complete-handler");
});
```

**Batch Status:**
- `Created` - Batch created, no jobs started
- `Started` - At least one job has started
- `Completed` - All jobs completed successfully
- `Failed` - At least one job failed
- `Cancelled` - Batch was cancelled

### Execution Context

The `JobExecutionContext<T>` provides:

```csharp
context.Input          // Typed input data
context.RunId          // Unique run identifier
context.AttemptNumber  // Current retry attempt (1-based)
context.Services       // IServiceProvider for DI
context.Logger         // Structured logging
context.SetProgress(50, 100, "Processing...")
context.IncrementSucceeded()
context.IncrementFailed()
context.SetOutput(result)  // For IJob<TInput, TOutput>
```

### Rate Limiting

Control job execution rates to prevent overwhelming external services or resources:

```csharp
// Per job type rate limiting via fluent API
services
    .AddZapJobs(options =>
    {
        options.WorkerCount = 4;

        // Global rate limit (all jobs)
        options.GlobalRateLimit = RateLimitPolicy.Create(100, TimeSpan.FromMinutes(1));

        // Per-queue rate limits
        options.QueueRateLimits["api-calls"] = RateLimitPolicy.Create(50, TimeSpan.FromMinutes(1));
    })
    .AddJob<SendEmailJob>(job => job
        .WithRateLimit(10, TimeSpan.FromMinutes(1)))  // 10 per minute
    .AddJob<ApiCallJob>(job => job
        .WithRateLimit(100, TimeSpan.FromHours(1), RateLimitBehavior.Reject));
```

**Rate Limit Behaviors:**
- `Delay` (default) - Reschedule job execution until rate limit allows
- `Reject` - Fail the job immediately
- `Skip` - Cancel silently (useful for recurring jobs)

**Rate Limit Scopes:**
- **Job Type** - Configure on `JobDefinitionBuilder.WithRateLimit()`
- **Queue** - Configure on `ZapJobsOptions.QueueRateLimits`
- **Global** - Configure on `ZapJobsOptions.GlobalRateLimit`

Rate limits are checked in order: Global → Queue → Job Type. All applicable limits must allow execution.

```csharp
// Advanced configuration with max delay for Delay behavior
.AddJob<SlackNotificationJob>(job => job
    .WithRateLimit(
        limit: 5,
        window: TimeSpan.FromSeconds(30),
        behavior: RateLimitBehavior.Delay,
        maxDelay: TimeSpan.FromMinutes(5)))
```

### Prevent Overlapping

Prevent a recurring job from starting a new execution while a previous instance is still running. Useful for long-running jobs where concurrent executions are undesirable.

```csharp
// Via fluent API at registration
services
    .AddZapJobs()
    .AddJob<LongRunningReportJob>(job => job
        .WithPreventOverlapping()
        .WithTimeout(TimeSpan.FromHours(2)))
    .AddJob<QuickSyncJob>(); // Can overlap (default)

// Via recurring job options
await scheduler.ScheduleRecurringAsync(
    "data-sync",
    "*/5 * * * *",  // Every 5 minutes
    input: null,
    options: new RecurringJobOptions { PreventOverlapping = true });
```

**How it works:**
- Before creating a new run, the scheduler checks if there's an existing run with status `Pending` or `Running`
- If found, the new execution is skipped and logged
- The next scheduled time is still calculated and updated
- Jobs without `PreventOverlapping` can have multiple concurrent executions

**Use cases:**
- Database migrations that should never run concurrently
- Report generation that takes longer than the schedule interval
- Sync jobs where running multiple instances would cause conflicts

### Event Broadcasting

Subscribe to job lifecycle events for monitoring, notifications, or custom integrations. Events are dispatched asynchronously (fire-and-forget) and don't block job execution.

```csharp
// Create an event handler
public class SlackNotificationHandler :
    IJobEventHandler<JobFailedEvent>,
    IJobEventHandler<JobCompletedEvent>
{
    private readonly ISlackClient _slack;

    public SlackNotificationHandler(ISlackClient slack)
    {
        _slack = slack;
    }

    public async Task HandleAsync(JobFailedEvent @event, CancellationToken ct = default)
    {
        await _slack.SendAsync($"Job {@event.JobTypeId} failed: {@event.ErrorMessage}");
    }

    public async Task HandleAsync(JobCompletedEvent @event, CancellationToken ct = default)
    {
        if (@event.Duration > TimeSpan.FromMinutes(5))
            await _slack.SendAsync($"Slow job {@event.JobTypeId} took {@event.Duration}");
    }
}

// Register the handler
services.AddZapJobs()
    .AddJob<MyJob>()
    .AddEventHandler<SlackNotificationHandler>();
```

**Available Events:**
- `JobEnqueuedEvent` - Job was added to the queue
- `JobStartedEvent` - Job execution started
- `JobCompletedEvent` - Job completed successfully (includes duration, output)
- `JobFailedEvent` - Job failed (includes error message, retry info, dead letter status)
- `JobRetryingEvent` - Job will be retried (includes delay, attempt numbers)

**Event Properties:**
All events include: `RunId`, `JobTypeId`, `Timestamp`, `Queue`

Each event type has additional relevant properties:
- `JobCompletedEvent`: `Duration`, `OutputJson`
- `JobFailedEvent`: `ErrorMessage`, `ErrorType`, `WillRetry`, `MovedToDeadLetter`
- `JobRetryingEvent`: `FailedAttempt`, `NextAttempt`, `Delay`, `ErrorMessage`

**Handler Notes:**
- Handlers run asynchronously in the background (fire-and-forget)
- Handler exceptions are logged but don't affect job execution
- Multiple handlers can subscribe to the same event type
- Handlers are resolved from DI per-event (scoped lifetime supported)

### Checkpoints/Resume

Save job progress and resume from the last checkpoint after failures. This enables long-running jobs to survive crashes without losing work.

```csharp
public class DataImportJob : IJob<ImportInput>
{
    public string JobTypeId => "data-import";

    public async Task ExecuteAsync(JobExecutionContext<ImportInput> context, CancellationToken ct)
    {
        // Check if resuming from a previous attempt
        if (context.Resume.IsResuming)
        {
            await context.Logger.InfoAsync(
                $"Resuming from attempt {context.Resume.PreviousAttempt}, " +
                $"reason: {context.Resume.Reason}");
        }

        // Restore previous progress or start fresh
        var progress = await context.GetCheckpointAsync<ImportProgress>("progress")
            ?? new ImportProgress { ProcessedCount = 0 };

        var items = await LoadItemsAsync(context.Input.SourceFile);

        foreach (var item in items.Skip(progress.ProcessedCount))
        {
            await ProcessItemAsync(item);
            progress.ProcessedCount++;
            progress.LastItemId = item.Id;

            // Save checkpoint every 100 items
            if (progress.ProcessedCount % 100 == 0)
            {
                await context.CheckpointAsync("progress", progress);
                context.SetProgress(progress.ProcessedCount, items.Count);
            }
        }

        // Clear checkpoints on successful completion
        await context.ClearCheckpointsAsync();
        context.IncrementSucceeded();
    }
}

record ImportProgress
{
    public int ProcessedCount { get; set; }
    public string? LastItemId { get; set; }
}
```

**Checkpoint API (JobExecutionContext):**
```csharp
// Save checkpoint (auto-compresses large data)
await context.CheckpointAsync("progress", data);

// Save with options (TTL, version)
await context.CheckpointAsync("cursor", cursor, new CheckpointSaveOptions
{
    Ttl = TimeSpan.FromHours(24),
    Version = 2
});

// Retrieve checkpoint
var data = await context.GetCheckpointAsync<T>("progress");

// Check existence
bool exists = await context.HasCheckpointAsync("progress");

// Get all checkpoints for this run
var all = await context.GetAllCheckpointsAsync();

// Clear all checkpoints (typically on success)
await context.ClearCheckpointsAsync();
```

**Resume Context:**
```csharp
context.Resume.IsResuming       // True if this is a retry after failure
context.Resume.PreviousAttempt  // Previous attempt number (null if first run)
context.Resume.Reason           // Why job is resuming (RetryAfterFailure, WorkerRecovery, etc.)
context.Resume.LastCheckpointKey // Key of the most recent checkpoint
context.Resume.CheckpointCreatedAt // When last checkpoint was saved
```

**Resume Reasons:**
- `None` - First execution (not resuming)
- `RetryAfterFailure` - Retrying after an error
- `WorkerRecovery` - Worker restarted/recovered
- `ManualRestart` - Manually requeued
- `SystemRestart` - System restart detected

**Configuration:**
```csharp
services.Configure<CheckpointOptions>(options =>
{
    options.DefaultTtl = TimeSpan.FromDays(7);           // Checkpoint expiration
    options.MaxDataSizeBytes = 1024 * 1024;              // 1 MB max per checkpoint
    options.MaxCheckpointsPerRun = 100;                  // Limit per job run
    options.CompressionThresholdBytes = 1024;            // Compress data > 1 KB
    options.EnableCompression = true;                     // GZip compression
    options.CleanupInterval = TimeSpan.FromHours(1);     // Cleanup frequency
    options.DeleteAfterJobCompletion = TimeSpan.FromHours(24); // Keep after completion
});
```

**Features:**
- **Multiple Named Checkpoints** - Use different keys for different state ("progress", "cursor", "batch-state")
- **Automatic Compression** - Large checkpoint data is GZip compressed automatically
- **Schema Versioning** - Version field for checkpoint data migrations
- **TTL Expiration** - Checkpoints auto-expire after configurable time
- **Background Cleanup** - Periodic cleanup of expired and completed job checkpoints
- **Rich Resume Context** - Know why and how a job is resuming

### Event History/Replay

Complete immutable audit trail for job executions with time-travel debugging, structured queries, and analytics. Unlike Temporal (51,200 event limit) or Hangfire (text logs), ZapJobs provides unlimited structured events with built-in projections.

```csharp
public class OrderProcessingJob : IJob<OrderInput>
{
    public string JobTypeId => "process-order";

    public async Task ExecuteAsync(JobExecutionContext<OrderInput> context, CancellationToken ct)
    {
        // Access the event publisher from context
        var events = context.Events;

        // Publish custom business events (structured, queryable)
        await events.PublishBusinessEventAsync(
            context.RunId,
            "order.validated",
            "Order",
            context.Input.OrderId,
            new Dictionary<string, object?> { ["total"] = 150.00m });

        // Publish progress milestones
        await events.PublishMilestoneAsync(
            context.RunId,
            "payment-processed",
            50,
            "Payment successfully processed");

        // Publish custom metrics
        await events.PublishMetricAsync(
            context.RunId,
            "items_processed",
            context.Input.Items.Count);

        // Publish log messages (structured, searchable)
        await events.PublishLogAsync(
            context.RunId,
            "Starting inventory check",
            EventSeverity.Info);

        await ProcessOrder(context.Input);
        context.IncrementSucceeded();
    }
}
```

**Event Store API:**
```csharp
// Inject IEventStore
public class DebugService
{
    private readonly IEventStore _eventStore;

    // Get all events for a job run
    public async Task<IReadOnlyList<JobEvent>> GetJobTimeline(Guid runId)
    {
        return await _eventStore.GetTimelineAsync(runId);
    }

    // Query with filters
    public async Task<IReadOnlyList<JobEvent>> QueryEvents(Guid runId)
    {
        return await _eventStore.GetEventsAsync(runId, new EventQueryOptions
        {
            EventType = EventTypes.ProgressUpdated,
            Category = EventCategories.Progress,
            Limit = 100,
            Offset = 0
        });
    }

    // Full-text search across event payloads
    public async Task<IReadOnlyList<JobEvent>> SearchEvents(string query)
    {
        return await _eventStore.SearchAsync(query, new EventSearchOptions
        {
            From = DateTimeOffset.UtcNow.AddDays(-7),
            EventTypes = [EventTypes.BusinessEvent, EventTypes.JobFailed]
        });
    }

    // Get events by correlation ID (distributed tracing)
    public async Task<IReadOnlyList<JobEvent>> GetCorrelatedEvents(string correlationId)
    {
        return await _eventStore.GetByCorrelationIdAsync(correlationId);
    }

    // Get workflow events across multiple job runs
    public async Task<IReadOnlyList<JobEvent>> GetWorkflowEvents(Guid workflowId)
    {
        return await _eventStore.GetWorkflowEventsAsync(workflowId);
    }

    // Get aggregated summary
    public async Task<EventHistorySummary> GetSummary(Guid runId)
    {
        return await _eventStore.GetSummaryAsync(runId);
    }
}
```

**Time-Travel Debugging with EventReplayer:**
```csharp
// Inject IEventReplayer
public class TimeTravel
{
    private readonly IEventReplayer _replayer;

    // Reconstruct job state at any point in time
    public async Task<JobStateSnapshot> GetStateAtTime(Guid runId, DateTimeOffset timestamp)
    {
        return await _replayer.ReplayToAsync(runId, timestamp);
    }

    // Get state after specific event
    public async Task<JobStateSnapshot> GetStateAfterEvent(Guid runId, long sequenceNumber)
    {
        return await _replayer.ReplayToSequenceAsync(runId, sequenceNumber);
    }

    // Replay event by event with callback
    public async Task ReplayWithCallback(Guid runId, Func<JobEvent, JobStateSnapshot, Task> onEvent)
    {
        await _replayer.ReplayAsync(runId, onEvent);
    }
}
```

**Event Publisher with Context:**
```csharp
// Set correlation context for distributed tracing
var factory = services.GetRequiredService<EventPublisherFactory>();
var publisher = factory.Create(
    correlationId: HttpContext.TraceIdentifier,
    actor: User.Identity?.Name);

// Or for workflows
var workflowPublisher = factory.CreateForWorkflow(workflowId);

// Context flows to all published events
publisher.SetCausationId(parentEventId);  // Chain events together
await publisher.PublishJobStartedAsync(runId, payload);
```

**Built-in Event Types:**

| Category | Event Types |
|----------|-------------|
| Job | `job.enqueued`, `job.scheduled`, `job.started`, `job.completed`, `job.failed`, `job.retrying`, `job.cancelled`, `job.timed_out`, `job.skipped`, `job.delayed`, `job.dead_lettered`, `job.requeued` |
| Activity | `activity.started`, `activity.completed`, `activity.failed` |
| Workflow | `workflow.started`, `workflow.completed`, `step.completed`, `continuation.triggered` |
| State | `checkpoint.saved`, `checkpoint.restored`, `output.set` |
| Progress | `progress.updated`, `item.processed`, `milestone.reached`, `heartbeat` |
| System | `worker.assigned`, `rate_limit.hit`, `timer.scheduled`, `timer.fired` |
| Custom | `log.message`, `metric.recorded`, `business.event` |

**Typed Event Payloads:**
All events use strongly-typed payloads for structured querying:
```csharp
// Job events
record JobCompletedPayload(long DurationMs, string? OutputJson, int SucceededCount, int FailedCount, int? ItemsProcessed);
record JobFailedPayload(string ErrorMessage, string? ErrorType, string? StackTrace, int AttemptNumber, int MaxRetries, bool WillRetry, DateTimeOffset? NextRetryAt, bool MovedToDeadLetter);

// Progress events
record ProgressUpdatedPayload(int Current, int Total, double Percentage, string? Message, long? EstimatedRemainingMs);
record MilestoneReachedPayload(string MilestoneName, int ItemsProcessed, long ElapsedMs, string? Description);

// Custom events
record BusinessEventPayload(string EventName, string? EntityType, string? EntityId, Dictionary<string, object?>? Data);
record MetricRecordedPayload(string MetricName, double Value, string? Unit, Dictionary<string, string>? Tags);
```

**Event Summary and Analytics:**
```csharp
var summary = await _eventStore.GetSummaryAsync(runId);
// Returns:
// - TotalEvents: 150
// - EventsByType: { "job.started": 1, "progress.updated": 100, ... }
// - EventsByCategory: { "job": 4, "progress": 102, ... }
// - FirstEventAt, LastEventAt
// - TotalDurationMs: 45000
// - ErrorCount: 2
// - RetryCount: 1
// - EventsPerSecond: 3.33
```

**Competitive Advantages over Temporal/Hangfire:**
- **No Event Limit** - Unlike Temporal's 51,200 event limit
- **No Determinism Required** - Unlike Temporal's replay constraints
- **Structured Events** - Unlike Hangfire's text logs
- **Full-text Search** - Search across all event payloads
- **Built-in Projections** - Timeline, summaries, aggregations
- **Time-travel Queries** - Reconstruct state at any point
- **Correlation/Causation** - Full distributed tracing support
- **Configurable Retention** - Per-category cleanup policies

### Health Checks

ASP.NET Core health checks for monitoring ZapJobs in production. Useful for Kubernetes liveness/readiness probes and load balancer health monitoring.

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddZapJobs()
    .UsePostgreSqlStorage(connectionString);

// Add all ZapJobs health checks
builder.Services.AddHealthChecks()
    .AddZapJobsHealthChecks(opts =>
    {
        opts.MinimumWorkers = 2;
        opts.MaxPendingJobsDegraded = 50;
        opts.MaxPendingJobsUnhealthy = 500;
        opts.MaxDeadLetterDegraded = 10;
        opts.MaxDeadLetterUnhealthy = 100;
    });

var app = builder.Build();

// Map health endpoints
app.MapHealthChecks("/health");

// Separate endpoints for Kubernetes
app.MapHealthChecks("/health/live", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("storage")
});

app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("zapjobs")
});

app.Run();
```

**Health Checks Included:**

| Check | Tags | Description |
|-------|------|-------------|
| `zapjobs-storage` | zapjobs, storage, db | Database connectivity |
| `zapjobs-workers` | zapjobs, workers | Active worker count |
| `zapjobs-queue` | zapjobs, queue | Pending jobs backlog |
| `zapjobs-deadletter` | zapjobs, deadletter | Dead letter queue size |

**Health Status:**
- **Healthy** - All thresholds within normal range
- **Degraded** - Approaching thresholds (warning)
- **Unhealthy** - Thresholds exceeded (alert)

**Kubernetes Deployment:**
```yaml
livenessProbe:
  httpGet:
    path: /health/live
    port: 8080
  initialDelaySeconds: 10
  periodSeconds: 30
readinessProbe:
  httpGet:
    path: /health/ready
    port: 8080
  initialDelaySeconds: 5
  periodSeconds: 10
```

### OpenTelemetry

Distributed tracing and metrics for job execution using OpenTelemetry. Each job execution creates a span with semantic convention tags, and metrics track execution counts, durations, and failures.

**Installation:**
```bash
dotnet add package ZapJobs.OpenTelemetry
```

**Setup:**
```csharp
var builder = WebApplication.CreateBuilder(args);

// Add ZapJobs with OpenTelemetry instrumentation
builder.Services.AddZapJobs()
    .AddJob<MyJob>()
    .AddOpenTelemetry(opts =>
    {
        opts.RecordException = true;
        opts.Filter = run => run.JobTypeId != "health-check";
        opts.Enrich = (activity, run) =>
        {
            activity.SetTag("tenant.id", run.Queue.Split('-')[0]);
        };
    });

// Configure OpenTelemetry exporters
builder.Services.AddOpenTelemetry()
    .WithTracing(tracing => tracing
        .AddAspNetCoreInstrumentation()
        .AddZapJobsInstrumentation()
        .AddOtlpExporter())
    .WithMetrics(metrics => metrics
        .AddZapJobsInstrumentation()
        .AddPrometheusExporter());

var app = builder.Build();
app.Run();
```

**Span Tags:**

| Tag | Description |
|-----|-------------|
| `zapjobs.job.type_id` | Job type identifier |
| `zapjobs.job.run_id` | Unique run ID |
| `zapjobs.job.queue` | Job queue name |
| `zapjobs.job.trigger_type` | How the job was triggered |
| `zapjobs.job.attempt` | Current attempt number |
| `zapjobs.job.status` | Final status (completed/failed) |
| `zapjobs.job.duration_ms` | Execution duration |
| `zapjobs.batch.id` | Batch ID if part of a batch |
| `zapjobs.job.parent_run_id` | Parent run ID for continuations |

**Metrics:**

| Metric | Type | Description |
|--------|------|-------------|
| `zapjobs.jobs.processed` | Counter | Total jobs processed |
| `zapjobs.jobs.succeeded` | Counter | Jobs completed successfully |
| `zapjobs.jobs.failed` | Counter | Jobs failed permanently |
| `zapjobs.jobs.retried` | Counter | Job retry attempts |
| `zapjobs.jobs.duration` | Histogram | Execution duration (ms) |
| `zapjobs.jobs.running` | UpDownCounter | Currently running jobs |
| `zapjobs.jobs.scheduled` | Counter | Jobs scheduled/enqueued |
| `zapjobs.jobs.dead_lettered` | Counter | Jobs moved to dead letter queue |

All metrics include `job.type_id` and `queue` tags.

**Instrumentation Options:**
```csharp
public class ZapJobsInstrumentationOptions
{
    // Record exception details in span (default: true)
    bool RecordException { get; set; }

    // Enrich span with custom tags
    Action<Activity, JobRun>? Enrich { get; set; }

    // Filter which jobs to trace (return false to skip)
    Func<JobRun, bool>? Filter { get; set; }

    // Record job input/output in span (default: false for security)
    bool RecordInput { get; set; }
    bool RecordOutput { get; set; }

    // Max length of input/output to record (default: 1000)
    int MaxPayloadLength { get; set; }
}
```

**Context Propagation:**
Trace context automatically propagates through job continuations, so parent and child jobs appear in the same distributed trace.

### REST API

Complete REST API for managing jobs, runs, workers, dead letter queue, and statistics. Includes built-in Swagger/OpenAPI documentation.

**Setup:**
```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddZapJobs()
    .UsePostgreSqlStorage(connectionString);

// Add REST API with Swagger
builder.Services.AddZapJobsApi(options =>
{
    options.EnableSwagger = true;              // Enable Swagger UI (default: true)
    options.RequireAuthentication = true;      // Require API key (default: true)
    options.ApiKeys = ["my-secret-key"];       // Valid API keys
    options.ApiKeyHeaderName = "X-Api-Key";    // Header name (default)
});

var app = builder.Build();

app.UseZapJobsApi();  // Maps controllers and Swagger

app.Run();
```

**Endpoints:**

| Controller | Endpoints | Description |
|------------|-----------|-------------|
| `/api/v1/jobs` | GET, POST, PUT, DELETE | Job definitions, trigger, schedule |
| `/api/v1/runs` | GET, DELETE | Job runs, cancel, retry |
| `/api/v1/workers` | GET | Active workers |
| `/api/v1/deadletter` | GET, POST | Dead letter queue, requeue, discard |
| `/api/v1/stats` | GET | Queue statistics, metrics |
| `/api/v1/webhooks` | GET, POST, PUT, DELETE | Webhook management |

**Swagger UI:** Available at `/swagger` when enabled.

**Authentication:**
```bash
# Using API key
curl -H "X-Api-Key: my-secret-key" http://localhost:5000/api/v1/jobs

# Trigger a job
curl -X POST -H "X-Api-Key: my-secret-key" \
  -H "Content-Type: application/json" \
  -d '{"input": {"email": "test@example.com"}}' \
  http://localhost:5000/api/v1/jobs/send-email/trigger
```

**Configuration via appsettings.json:**
```json
{
  "ZapJobsApi": {
    "EnableSwagger": true,
    "RequireAuthentication": true,
    "ApiKeys": ["key1", "key2"],
    "ApiKeyHeaderName": "X-Api-Key"
  }
}
```

## Configuration Options

```csharp
public class ZapJobsOptions
{
    WorkerCount = 4;                              // Worker threads
    DefaultQueue = "default";                     // Default queue name
    Queues = ["critical", "default", "low"];      // Priority order
    PollingInterval = TimeSpan.FromSeconds(15);   // Job polling
    HeartbeatInterval = TimeSpan.FromSeconds(30); // Health check
    StaleWorkerThreshold = TimeSpan.FromMinutes(5);
    DefaultTimeout = TimeSpan.FromMinutes(60);
    JobRetention = TimeSpan.FromDays(30);         // Keep completed jobs
    LogRetention = TimeSpan.FromDays(7);          // Keep logs
    EnableScheduler = true;                       // CRON scheduling
    EnableProcessing = true;                      // Job execution
}
```

## Database Schema

PostgreSQL tables (see `Migrations/InitialCreate.sql`, `Migrations/AddBatches.sql`, `Migrations/AddRateLimiting.sql`, `Migrations/AddCheckpoints.sql`, and `Migrations/AddEvents.sql`):

| Table | Purpose |
|-------|---------|
| `zapjobs.definitions` | Job type configs and CRON schedules |
| `zapjobs.runs` | Individual job executions |
| `zapjobs.logs` | Structured execution logs |
| `zapjobs.heartbeats` | Worker health monitoring |
| `zapjobs.continuations` | Job continuation chains |
| `zapjobs.dead_letter` | Dead letter queue for failed jobs |
| `zapjobs.batches` | Batch job groupings |
| `zapjobs.batch_jobs` | Links runs to batches |
| `zapjobs.batch_continuations` | Batch-level continuations |
| `zapjobs.rate_limit_executions` | Rate limit tracking for sliding window |
| `zapjobs.checkpoints` | Checkpoint state for job resumption |
| `zapjobs.events` | Immutable event log for audit trail and time-travel debugging |

## Retry Policy

Default exponential backoff with jitter:

```csharp
public class RetryPolicy
{
    MaxRetries = 3;
    InitialDelay = TimeSpan.FromSeconds(30);
    MaxDelay = TimeSpan.FromHours(1);
    BackoffMultiplier = 2.0;
    UseJitter = true;  // Prevents thundering herd
}
```

## Project Structure

```
zapjobs/
├── src/
│   ├── ZapJobs.Core/              # Abstractions (no deps)
│   ├── ZapJobs/                   # Runtime implementation
│   ├── ZapJobs.Storage.InMemory/  # Dev/test storage
│   ├── ZapJobs.Storage.PostgreSQL/# Production storage
│   ├── ZapJobs.AspNetCore/        # ASP.NET Core integration (dashboard, health checks)
│   └── ZapJobs.OpenTelemetry/     # OpenTelemetry instrumentation
├── tests/
│   ├── ZapJobs.Tests/             # Core and runtime unit tests
│   └── ZapJobs.Storage.InMemory.Tests/ # Storage unit tests
├── CLAUDE.md                       # This file
└── ZapJobs.slnx                    # Solution file
```

## Conventions

- Use `var` for variable declarations
- Async/await with CancellationToken support throughout
- All storage operations are idempotent where possible
- Job IDs are strings (e.g., "send-email", "process-order")
- Run IDs are GUIDs

## Version Policy

Always prefer the **latest stable LTS versions** for new projects:

- **.NET**: Use the current LTS version (e.g., .NET 10 LTS)
- **NuGet packages**: Use latest stable versions, especially `Microsoft.Extensions.*`
- **Testing frameworks**: xUnit 2.9+, FluentAssertions 8+, Moq 4.20+
- **Database drivers**: Npgsql 10+ for .NET 10

The project uses:
- `global.json` to pin SDK version with `rollForward: latestMinor`
- `.mise.toml` for mise-based SDK management

## Integration Examples

### With ZapDo (via git submodule)

```csharp
// ZapDo.Infrastructure/DependencyInjection.cs
services
    .AddZapJobs(options => { ... })
    .AddJob<SendReminderJob>();

services.UseInMemoryStorage();
services.AddScoped<IReminderService, ZapJobsReminderService>();
```

### Standalone ASP.NET Core

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Services
    .AddZapJobs(builder.Configuration)
    .AddJob<MyJob>();

builder.Services.UsePostgreSqlStorage(
    builder.Configuration.GetConnectionString("Jobs"));

var app = builder.Build();
app.Run();
```

## Future Improvements

- [ ] Dashboard UI for monitoring
- [ ] Redis storage backend
- [x] Job continuations (run after another job completes)
- [x] Batch jobs (group and track multiple jobs as a unit)
- [x] Rate limiting per job type
- [x] Dead letter queue for failed jobs
- [x] Event broadcasting (pub/sub for job lifecycle events)
- [x] Checkpoints/Resume (durable state for long-running jobs)
- [x] Event History/Replay (immutable audit trail with time-travel debugging)
- [x] Prevent Overlapping (skip execution if previous still running)
- [x] Health Checks (ASP.NET Core health checks for K8s/monitoring)
- [x] OpenTelemetry (distributed tracing and metrics via OpenTelemetry)
- [x] REST API (complete management API with 6 controllers)
- [x] OpenAPI/Swagger (auto-generated API documentation)
