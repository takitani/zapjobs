# ZapJobs.AspNetCore

ASP.NET Core integration for ZapJobs - the database-driven job scheduler for .NET.

## Installation

```bash
dotnet add package ZapJobs.AspNetCore
```

## Features

- **Dashboard UI** - Built-in web dashboard for monitoring jobs
- **Health Checks** - ASP.NET Core health check integration
- **REST API** - Complete REST API for job management
- **Swagger/OpenAPI** - Auto-generated API documentation

## Quick Start

```csharp
var builder = WebApplication.CreateBuilder(args);

// Add ZapJobs with storage
builder.Services
    .AddZapJobs(options =>
    {
        options.WorkerCount = 4;
        options.PollingInterval = TimeSpan.FromSeconds(5);
    })
    .AddJob<MyJob>();

builder.Services.UsePostgreSqlStorage(
    builder.Configuration.GetConnectionString("Jobs"));

var app = builder.Build();

// Enable dashboard at /zapjobs
app.UseZapJobsDashboard();

// Enable API at /api/zapjobs
app.UseZapJobsApi();

app.Run();
```

## Dashboard

Access the dashboard at `/zapjobs` to:
- View running, pending, and completed jobs
- Monitor job queues and workers
- Trigger manual job execution
- View job logs and history
- Manage dead letter queue

## Health Checks

```csharp
builder.Services.AddHealthChecks()
    .AddZapJobsHealthCheck();
```

## Related Packages

- `ZapJobs.Core` - Core abstractions
- `ZapJobs` - Main implementation
- `ZapJobs.Storage.PostgreSQL` - PostgreSQL storage
- `ZapJobs.Storage.InMemory` - In-memory storage (dev/test)

## License

MIT License - See [LICENSE](https://github.com/takitani/zapjobs/blob/master/LICENSE)
