# ZapJobs

**Database-driven job scheduler for .NET** - A production-ready, distributed background job system with PostgreSQL persistence, web dashboard, and CLI tooling.

## Features

- **Database-First Design** - Jobs, schedules, and execution history stored in PostgreSQL
- **Multiple Scheduling Options** - Manual triggers, fixed intervals, or CRON expressions with timezone support
- **Distributed Workers** - Run multiple worker instances with automatic job acquisition and heartbeat monitoring
- **Retry with Exponential Backoff** - Configurable retry policies with jitter to prevent thundering herd
- **Priority Queues** - Route jobs to different queues (critical, default, low)
- **Web Dashboard** - Beautiful, real-time monitoring UI with job management
- **CLI Tool** - Interactive setup, migrations, and status monitoring
- **Typed Jobs** - Generic job interfaces with input/output type safety
- **Structured Logging** - Per-job logging with context and log levels
- **Progress Tracking** - Track job progress and item processing metrics

## Quick Start

### 1. Install Packages

```bash
dotnet add package ZapJobs
dotnet add package ZapJobs.Storage.PostgreSQL
dotnet add package ZapJobs.AspNetCore  # Optional: for dashboard
```

### 2. Configure Services

```csharp
var builder = WebApplication.CreateBuilder(args);

// Add ZapJobs with PostgreSQL storage
builder.Services.AddZapJobs(opts =>
{
    opts.WorkerCount = Environment.ProcessorCount;
    opts.Queues = ["critical", "default", "low"];
})
.UsePostgreSqlStorage(opts =>
{
    opts.ConnectionString = builder.Configuration.GetConnectionString("ZapJobs")!;
    opts.AutoMigrate = true;
})
.AddJob<MyEmailJob>()
.AddJob<MyReportJob>();

// Optional: Add dashboard
builder.Services.AddZapJobsDashboard();

var app = builder.Build();

// Optional: Map dashboard
app.MapZapJobsDashboard("/zapjobs");

app.Run();
```

### 3. Create a Job

```csharp
public class MyEmailJob : IJob<EmailInput>
{
    public string JobTypeId => "email-sender";

    public async Task ExecuteAsync(JobExecutionContext<EmailInput> context, CancellationToken ct)
    {
        var input = context.Input;

        await context.Logger.InfoAsync($"Sending email to {input.To}");

        // Your email sending logic here

        await context.Logger.InfoAsync("Email sent successfully");
    }
}

public record EmailInput(string To, string Subject, string Body);
```

### 4. Schedule Jobs

```csharp
public class MyController : ControllerBase
{
    private readonly IJobScheduler _scheduler;

    public MyController(IJobScheduler scheduler) => _scheduler = scheduler;

    // Enqueue for immediate execution
    [HttpPost("send-email")]
    public async Task<IActionResult> SendEmail(EmailInput input)
    {
        var runId = await _scheduler.EnqueueAsync<MyEmailJob>(input);
        return Ok(new { runId });
    }

    // Schedule for later
    [HttpPost("schedule-report")]
    public async Task<IActionResult> ScheduleReport()
    {
        var runId = await _scheduler.ScheduleAsync("report-generator",
            delay: TimeSpan.FromHours(1));
        return Ok(new { runId });
    }

    // Setup recurring job (call once on startup)
    public async Task SetupRecurringJobs()
    {
        // Every day at 2 AM
        await _scheduler.RecurringAsync("daily-cleanup", "0 2 * * *");

        // Every 30 minutes
        await _scheduler.RecurringAsync("sync-data", TimeSpan.FromMinutes(30));
    }
}
```

## Testing

Run all tests:

```bash
dotnet test
```

Run tests with coverage:

```bash
dotnet test --collect:"XPlat Code Coverage"
```

Run specific test class:

```bash
dotnet test --filter "FullyQualifiedName~RetryPolicyTests"
```

Generate coverage report (requires `reportgenerator` tool):

```bash
dotnet tool install -g dotnet-reportgenerator-globaltool
reportgenerator -reports:"**/coverage.cobertura.xml" -targetdir:"coverage" -reporttypes:Html
```

### Test Projects

| Project | Description |
|---------|-------------|
| `ZapJobs.Core.Tests` | Tests for core abstractions, entities, and configuration |
| `ZapJobs.Tests` | Tests for scheduling, execution, and retry logic |
| `ZapJobs.Storage.InMemory.Tests` | Tests for in-memory storage implementation |

## Project Structure

| Package | Description |
|---------|-------------|
| `ZapJobs.Core` | Core abstractions, entities, and configuration |
| `ZapJobs` | Main implementation with scheduling, execution, and tracking |
| `ZapJobs.Storage.PostgreSQL` | PostgreSQL storage backend (production) |
| `ZapJobs.Storage.InMemory` | In-memory storage (development/testing) |
| `ZapJobs.AspNetCore` | Web dashboard and ASP.NET Core integration |
| `ZapJobs.Cli` | Command-line tool for setup and monitoring |

## CLI Tool

Install the CLI tool:

```bash
dotnet tool install --global ZapJobs.Cli
```

Available commands:

```bash
# Interactive setup wizard
zapjobs init

# Run database migrations
zapjobs migrate -c "Host=localhost;Database=myapp;User Id=postgres;Password=postgres"

# Show status (with optional watch mode)
zapjobs status -c "..." --watch
```

## Configuration

```json
{
  "ZapJobs": {
    "WorkerCount": 4,
    "DefaultQueue": "default",
    "Queues": ["critical", "default", "low"],
    "PollingInterval": "00:00:15",
    "HeartbeatInterval": "00:00:30",
    "DefaultTimeout": "01:00:00",
    "JobRetention": "30.00:00:00",
    "LogRetention": "7.00:00:00"
  },
  "ConnectionStrings": {
    "ZapJobs": "Host=localhost;Database=myapp;User Id=postgres;Password=postgres"
  }
}
```

### Configuration Options

| Option | Default | Description |
|--------|---------|-------------|
| `WorkerCount` | `ProcessorCount` | Number of concurrent workers |
| `DefaultQueue` | `"default"` | Default queue for jobs |
| `Queues` | `["critical", "default", "low"]` | Queue priority order |
| `PollingInterval` | `15s` | How often to check for jobs |
| `HeartbeatInterval` | `30s` | Worker heartbeat frequency |
| `StaleWorkerThreshold` | `5m` | Mark worker as dead after |
| `DefaultTimeout` | `60m` | Default job timeout |
| `JobRetention` | `30d` | Keep completed runs for |
| `LogRetention` | `7d` | Keep logs for |
| `EnableScheduler` | `true` | Process recurring jobs |
| `EnableProcessing` | `true` | Execute jobs |

## Dashboard

Access the dashboard at `/zapjobs` (configurable):

- **Overview** - Statistics, recent runs, system health
- **Jobs** - All job definitions with schedules and status
- **Runs** - Execution history with logs and details
- **Workers** - Active worker instances with heartbeat status

### Dashboard Options

```csharp
services.AddZapJobsDashboard(opts =>
{
    opts.BasePath = "/admin/jobs";
    opts.Title = "My App Jobs";
    opts.RefreshInterval = 5; // seconds
    opts.RequireAuthentication = true;
    opts.AuthorizationPolicy = "AdminOnly";
});
```

## Job Types

### Basic Job

```csharp
public class SimpleJob : IJob
{
    public string JobTypeId => "simple-job";

    public async Task ExecuteAsync(JobExecutionContext context, CancellationToken ct)
    {
        await context.Logger.InfoAsync("Doing work...");
    }
}
```

### Job with Input

```csharp
public class ProcessOrderJob : IJob<OrderInput>
{
    public string JobTypeId => "process-order";

    public async Task ExecuteAsync(JobExecutionContext<OrderInput> context, CancellationToken ct)
    {
        var order = context.Input;
        await context.Logger.InfoAsync($"Processing order {order.OrderId}");
    }
}
```

### Job with Input and Output

```csharp
public class GenerateReportJob : IJob<ReportParams, ReportResult>
{
    public string JobTypeId => "generate-report";

    public async Task<ReportResult> ExecuteAsync(
        JobExecutionContext<ReportParams> context,
        CancellationToken ct)
    {
        var result = await GenerateReport(context.Input);
        return result;
    }
}
```

### Progress Tracking

```csharp
public class BatchProcessJob : IJob<BatchInput>
{
    public string JobTypeId => "batch-process";

    public async Task ExecuteAsync(JobExecutionContext<BatchInput> context, CancellationToken ct)
    {
        var items = context.Input.Items;

        foreach (var item in items)
        {
            try
            {
                await ProcessItem(item, ct);
                context.IncrementSucceeded();
            }
            catch
            {
                context.IncrementFailed();
            }

            context.IncrementProcessed();
        }
    }
}
```

## Retry Policies

Configure default retry policy:

```csharp
services.AddZapJobs(opts =>
{
    opts.DefaultRetryPolicy = new RetryPolicy
    {
        MaxRetries = 5,
        InitialDelay = TimeSpan.FromSeconds(30),
        BackoffMultiplier = 2.0,
        MaxDelay = TimeSpan.FromHours(1),
        UseJitter = true
    };
});
```

Or per-job:

```csharp
var definition = new JobDefinition
{
    JobTypeId = "critical-job",
    MaxRetries = 10,
    TimeoutSeconds = 7200
};
await storage.UpsertDefinitionAsync(definition);
```

## Architecture

```
┌─────────────────┐     ┌─────────────────┐
│   Your App      │     │    Dashboard    │
│   (Producer)    │     │    (Monitor)    │
└────────┬────────┘     └────────┬────────┘
         │                       │
         ▼                       ▼
┌─────────────────────────────────────────┐
│            IJobScheduler                 │
│  EnqueueAsync, ScheduleAsync, etc.       │
└────────────────────┬────────────────────┘
                     │
                     ▼
┌─────────────────────────────────────────┐
│            IJobStorage                   │
│  PostgreSQL / InMemory                   │
└────────────────────┬────────────────────┘
                     │
                     ▼
┌─────────────────────────────────────────┐
│      JobProcessorHostedService           │
│  (Workers polling for jobs)              │
└────────────────────┬────────────────────┘
                     │
                     ▼
┌─────────────────────────────────────────┐
│           JobExecutor                    │
│  Timeout, Retry, Logging                 │
└────────────────────┬────────────────────┘
                     │
                     ▼
┌─────────────────────────────────────────┐
│           Your IJob                      │
│  ExecuteAsync(context, ct)               │
└─────────────────────────────────────────┘
```

## Database Schema

The PostgreSQL storage creates these tables (with configurable prefix):

- `zapjobs_definitions` - Job type configurations and schedules
- `zapjobs_runs` - Individual job executions
- `zapjobs_logs` - Execution logs per run
- `zapjobs_heartbeats` - Worker health monitoring

## Requirements

- .NET 9.0+
- PostgreSQL 12+ (for production)

## License

MIT

## Contributing

Contributions are welcome! See [roadmap](./roadmap/README.md) for planned features.
