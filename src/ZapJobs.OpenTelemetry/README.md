# ZapJobs.OpenTelemetry

OpenTelemetry instrumentation for ZapJobs - the database-driven job scheduler for .NET.

## Installation

```bash
dotnet add package ZapJobs.OpenTelemetry
```

## Features

- **Distributed Tracing** - Spans for every job execution with full context propagation
- **Semantic Conventions** - Tags follow OpenTelemetry semantic conventions
- **Metrics** - Job execution counters, duration histograms, and gauges
- **Customizable** - Filter jobs, enrich spans, and configure exception recording

## Quick Start

```csharp
var builder = WebApplication.CreateBuilder(args);

// Add ZapJobs
builder.Services
    .AddZapJobs(options => { ... })
    .AddJob<MyJob>()
    .AddOpenTelemetry(); // Enable instrumentation

// Configure OpenTelemetry exporters
builder.Services.AddOpenTelemetry()
    .WithTracing(tracing => tracing
        .AddZapJobsInstrumentation()
        .AddOtlpExporter())
    .WithMetrics(metrics => metrics
        .AddZapJobsInstrumentation()
        .AddPrometheusExporter());

var app = builder.Build();
app.Run();
```

## Tracing

Each job execution creates a span with the following tags:

| Tag | Description |
|-----|-------------|
| `zapjobs.job.type_id` | Job type identifier |
| `zapjobs.job.run_id` | Unique run ID |
| `zapjobs.job.queue` | Job queue name |
| `zapjobs.job.trigger_type` | How the job was triggered |
| `zapjobs.job.attempt` | Current attempt number |
| `zapjobs.job.status` | Final status (completed/failed) |
| `zapjobs.job.duration_ms` | Execution duration |

### Configuration

```csharp
builder.Services.AddOpenTelemetry()
    .WithTracing(tracing => tracing
        .AddZapJobsInstrumentation(opts =>
        {
            // Record exception details in span
            opts.RecordException = true;

            // Add custom tags
            opts.Enrich = (activity, run) =>
            {
                activity.SetTag("tenant.id", run.Queue.Split('-')[0]);
            };

            // Filter which jobs to trace
            opts.Filter = run => run.JobTypeId != "health-check";
        }));
```

## Metrics

The following metrics are exported:

| Metric | Type | Description |
|--------|------|-------------|
| `zapjobs.jobs.processed` | Counter | Total jobs processed |
| `zapjobs.jobs.failed` | Counter | Total jobs failed |
| `zapjobs.jobs.duration` | Histogram | Job execution duration (ms) |
| `zapjobs.jobs.running` | UpDownCounter | Currently running jobs |

### Tags

All metrics include these tags:
- `job.type_id` - Job type identifier
- `queue` - Job queue name

## Context Propagation

Trace context propagates automatically through job continuations:

```csharp
// Parent job creates trace context
var runId = await scheduler.EnqueueAsync("parent-job", input);

// Continuation inherits parent trace
await scheduler.ContinueWithAsync(runId, "child-job");
```

Both jobs appear in the same trace in your APM tool.

## Related Packages

- `ZapJobs.Core` - Core abstractions
- `ZapJobs` - Main implementation
- `ZapJobs.AspNetCore` - ASP.NET Core integration

## License

MIT License - See [LICENSE](https://github.com/takitani/zapjobs/blob/master/LICENSE)
