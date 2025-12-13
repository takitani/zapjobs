# ZapJobs.Core

Core abstractions and entities for ZapJobs - the database-driven job scheduler for .NET.

## Installation

```bash
dotnet add package ZapJobs.Core
```

## This Package Contains

- `IJob`, `IJob<T>`, `IJob<TIn, TOut>` - Job interfaces
- `IJobScheduler` - Scheduling API
- `IJobStorage` - Storage abstraction
- `IJobTracker` - Monitoring API
- `JobDefinition`, `JobRun`, `JobLog` - Entities
- `ZapJobsOptions`, `RetryPolicy` - Configuration

## Usage

This is a core package containing abstractions. For a complete solution, install:

- `ZapJobs` - Main implementation
- `ZapJobs.Storage.PostgreSQL` - PostgreSQL storage (production)
- `ZapJobs.Storage.InMemory` - In-memory storage (development/testing)

## Example Job Implementation

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

## License

MIT - See [LICENSE](https://github.com/takitani/zapjobs/blob/master/LICENSE)
