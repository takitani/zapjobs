# ZapJobs

Database-driven job scheduler for .NET - Main implementation with scheduling, execution, and tracking.

## Installation

```bash
dotnet add package ZapJobs
dotnet add package ZapJobs.Storage.PostgreSQL  # or ZapJobs.Storage.InMemory
```

## Quick Start

### 1. Define a Job

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

### 2. Register Services

```csharp
services
    .AddZapJobs(options =>
    {
        options.WorkerCount = 4;
        options.PollingInterval = TimeSpan.FromSeconds(5);
        options.DefaultQueue = "default";
        options.Queues = ["critical", "default", "low"];
    })
    .AddJob<SendEmailJob>();

// Choose storage backend
services.UseInMemoryStorage();  // Development
// OR
services.UsePostgreSqlStorage(connectionString);  // Production
```

### 3. Schedule Jobs

```csharp
public class MyService
{
    private readonly IJobScheduler _scheduler;

    public MyService(IJobScheduler scheduler) => _scheduler = scheduler;

    // Enqueue for immediate execution
    public async Task SendNow(SendEmailInput input)
    {
        await _scheduler.EnqueueAsync("send-email", input);
    }

    // Schedule for later
    public async Task ScheduleEmail(SendEmailInput input)
    {
        await _scheduler.ScheduleAsync(
            "send-email",
            DateTimeOffset.UtcNow.AddMinutes(30),
            input);
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

## License

MIT - See [LICENSE](https://github.com/takitani/zapjobs/blob/master/LICENSE)
