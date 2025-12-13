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
    ├── Configuration/   → ZapJobsOptions, RetryPolicy
    ├── Context/         → JobExecutionContext<T>
    └── Entities/        → JobDefinition, JobRun, JobLog, JobHeartbeat, JobContinuation, DeadLetterEntry, JobBatch

ZapJobs (Runtime)
    ├── Batches/         → BatchBuilder, BatchService
    ├── DeadLetter/      → DeadLetterManager
    ├── Execution/       → JobExecutor, RetryHandler
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

PostgreSQL tables (see `Migrations/InitialCreate.sql` and `Migrations/AddBatches.sql`):

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
│   └── ZapJobs.Storage.PostgreSQL/# Production storage
├── tests/                          # Unit tests (TODO)
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
- [ ] Rate limiting per job type
- [x] Dead letter queue for failed jobs
- [ ] Metrics export (Prometheus/OpenTelemetry)
