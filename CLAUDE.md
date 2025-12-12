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
    ├── Abstractions/    → IJob, IJobStorage, IJobScheduler, IJobTracker, IJobLogger
    ├── Configuration/   → ZapJobsOptions, RetryPolicy
    ├── Context/         → JobExecutionContext<T>
    └── Entities/        → JobDefinition, JobRun, JobLog, JobHeartbeat

ZapJobs (Runtime)
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

PostgreSQL tables (see `Migrations/InitialCreate.sql`):

| Table | Purpose |
|-------|---------|
| `zapjobs_definitions` | Job type configs and CRON schedules |
| `zapjobs_runs` | Individual job executions |
| `zapjobs_logs` | Structured execution logs |
| `zapjobs_heartbeats` | Worker health monitoring |

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
- [ ] Job dependencies (run after another job completes)
- [ ] Rate limiting per job type
- [ ] Dead letter queue for failed jobs
- [ ] Metrics export (Prometheus/OpenTelemetry)
