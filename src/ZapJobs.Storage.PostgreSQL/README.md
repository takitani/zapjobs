# ZapJobs.Storage.PostgreSQL

PostgreSQL storage backend for ZapJobs - recommended for production use.

## Installation

```bash
dotnet add package ZapJobs
dotnet add package ZapJobs.Storage.PostgreSQL
```

## Usage

```csharp
services
    .AddZapJobs(options =>
    {
        options.WorkerCount = 4;
        options.PollingInterval = TimeSpan.FromSeconds(5);
    })
    .AddJob<MyJob>();

// Use PostgreSQL storage
var connectionString = builder.Configuration.GetConnectionString("Jobs");
services.UsePostgreSqlStorage(connectionString);
```

## Database Schema

The package includes automatic migrations. Tables created:

| Table | Purpose |
|-------|---------|
| `zapjobs_definitions` | Job type configs and CRON schedules |
| `zapjobs_runs` | Individual job executions |
| `zapjobs_logs` | Structured execution logs |
| `zapjobs_heartbeats` | Worker health monitoring |

## Features

- Durable job storage with ACID guarantees
- Automatic schema migrations
- Optimized queries with proper indexing
- Support for multiple worker instances
- Heartbeat-based worker health monitoring
- Configurable retention policies

## Configuration

Connection string format:

```
Host=localhost;Database=myapp;Username=postgres;Password=secret
```

## Requirements

- PostgreSQL 12 or later
- Npgsql 9.x

## License

MIT - See [LICENSE](https://github.com/takitani/zapjobs/blob/master/LICENSE)
