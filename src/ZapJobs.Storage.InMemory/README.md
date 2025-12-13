# ZapJobs.Storage.InMemory

In-memory storage backend for ZapJobs - ideal for development and testing.

## Installation

```bash
dotnet add package ZapJobs
dotnet add package ZapJobs.Storage.InMemory
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

// Use in-memory storage
services.UseInMemoryStorage();
```

## Features

- No external dependencies
- Fast execution for testing
- Automatic cleanup when application stops
- Thread-safe operations

## When to Use

- **Development**: Quick iteration without database setup
- **Unit Tests**: Isolated testing of job logic
- **Integration Tests**: Fast test execution
- **Demos**: Quick prototyping and demonstrations

## When NOT to Use

- **Production**: Data is lost on restart
- **Multi-instance**: No shared state between instances
- **Long-running jobs**: No persistence across restarts

For production use, install `ZapJobs.Storage.PostgreSQL` instead.

## License

MIT - See [LICENSE](https://github.com/takitani/zapjobs/blob/master/LICENSE)
