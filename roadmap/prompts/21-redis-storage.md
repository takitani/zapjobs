# Prompt: Implementar Redis Storage Provider

## Objetivo

Criar um provider de armazenamento Redis para cenários de alta performance e escalabilidade, similar ao Hangfire.Pro com Redis.

## Contexto

Redis é ideal para:
- Alta throughput de jobs
- Escalabilidade horizontal
- Latência ultra-baixa
- Pub/Sub para real-time updates
- Cenários cloud-native

## Requisitos

### Novo Projeto

```xml
<!-- src/ZapJobs.Storage.Redis/ZapJobs.Storage.Redis.csproj -->
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net9.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="StackExchange.Redis" Version="2.8.0" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\ZapJobs.Core\ZapJobs.Core.csproj" />
  </ItemGroup>
</Project>
```

### Estrutura de Keys Redis

```
{prefix}:definitions:{jobTypeId}     -> Hash (job definition)
{prefix}:definitions:list            -> Set (all job type IDs)

{prefix}:runs:{runId}                -> Hash (job run)
{prefix}:runs:by-status:{status}     -> Sorted Set (score = created_at timestamp)
{prefix}:runs:by-job:{jobTypeId}     -> Sorted Set (score = created_at timestamp)

{prefix}:queue:{queueName}           -> Sorted Set (score = scheduled_at or created_at)
{prefix}:queue:{queueName}:processing -> Set (run IDs being processed)

{prefix}:logs:{runId}                -> List (job logs, FIFO)

{prefix}:heartbeats                  -> Hash (workerId -> last heartbeat timestamp)
{prefix}:workers:{workerId}          -> Hash (worker info)

{prefix}:stats:daily:{date}          -> Hash (daily statistics)
{prefix}:stats:hourly:{date}:{hour}  -> Hash (hourly statistics)

{prefix}:pubsub:jobs                 -> Pub/Sub channel for job events
```

### Implementação Principal

```csharp
// ZapJobs.Storage.Redis/RedisJobStorage.cs
using StackExchange.Redis;
using System.Text.Json;
using ZapJobs.Core.Abstractions;
using ZapJobs.Core.Entities;

namespace ZapJobs.Storage.Redis;

public class RedisJobStorage : IJobStorage, IJobStoragePubSub
{
    private readonly IConnectionMultiplexer _redis;
    private readonly IDatabase _db;
    private readonly string _prefix;
    private readonly ILogger<RedisJobStorage> _logger;
    private readonly JsonSerializerOptions _jsonOptions;

    public RedisJobStorage(
        IConnectionMultiplexer redis,
        RedisStorageOptions options,
        ILogger<RedisJobStorage> logger)
    {
        _redis = redis;
        _db = redis.GetDatabase(options.Database);
        _prefix = options.KeyPrefix;
        _logger = logger;
        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };
    }

    #region Keys

    private string DefinitionKey(string jobTypeId) => $"{_prefix}:definitions:{jobTypeId}";
    private string DefinitionsListKey => $"{_prefix}:definitions:list";
    private string RunKey(Guid runId) => $"{_prefix}:runs:{runId}";
    private string RunsByStatusKey(JobRunStatus status) => $"{_prefix}:runs:by-status:{(int)status}";
    private string RunsByJobKey(string jobTypeId) => $"{_prefix}:runs:by-job:{jobTypeId}";
    private string QueueKey(string queue) => $"{_prefix}:queue:{queue}";
    private string QueueProcessingKey(string queue) => $"{_prefix}:queue:{queue}:processing";
    private string LogsKey(Guid runId) => $"{_prefix}:logs:{runId}";
    private string HeartbeatsKey => $"{_prefix}:heartbeats";
    private string WorkerKey(string workerId) => $"{_prefix}:workers:{workerId}";
    private string PubSubChannel => $"{_prefix}:pubsub:jobs";

    #endregion

    #region Job Definitions

    public async Task<IReadOnlyList<JobDefinition>> GetAllDefinitionsAsync(CancellationToken ct = default)
    {
        var jobTypeIds = await _db.SetMembersAsync(DefinitionsListKey);
        var definitions = new List<JobDefinition>();

        foreach (var id in jobTypeIds)
        {
            var definition = await GetJobDefinitionAsync(id.ToString()!, ct);
            if (definition != null)
                definitions.Add(definition);
        }

        return definitions.OrderBy(d => d.DisplayName).ToList();
    }

    public async Task<JobDefinition?> GetJobDefinitionAsync(string jobTypeId, CancellationToken ct = default)
    {
        var hash = await _db.HashGetAllAsync(DefinitionKey(jobTypeId));
        if (hash.Length == 0) return null;

        return HashToDefinition(hash);
    }

    public async Task UpsertDefinitionAsync(JobDefinition definition, CancellationToken ct = default)
    {
        var hash = DefinitionToHash(definition);
        var key = DefinitionKey(definition.JobTypeId);

        var tran = _db.CreateTransaction();
        _ = tran.HashSetAsync(key, hash);
        _ = tran.SetAddAsync(DefinitionsListKey, definition.JobTypeId);
        await tran.ExecuteAsync();

        await PublishEventAsync(new JobEvent
        {
            Type = "definition_updated",
            JobTypeId = definition.JobTypeId
        });
    }

    public async Task DeleteDefinitionAsync(string jobTypeId, CancellationToken ct = default)
    {
        var tran = _db.CreateTransaction();
        _ = tran.KeyDeleteAsync(DefinitionKey(jobTypeId));
        _ = tran.SetRemoveAsync(DefinitionsListKey, jobTypeId);
        await tran.ExecuteAsync();
    }

    #endregion

    #region Job Runs

    public async Task<Guid> EnqueueAsync(JobRun run, CancellationToken ct = default)
    {
        run.Id = run.Id == Guid.Empty ? Guid.NewGuid() : run.Id;
        run.CreatedAt = DateTime.UtcNow;

        var hash = RunToHash(run);
        var key = RunKey(run.Id);
        var score = (run.ScheduledAt ?? run.CreatedAt).Ticks;

        var tran = _db.CreateTransaction();
        _ = tran.HashSetAsync(key, hash);
        _ = tran.SortedSetAddAsync(QueueKey(run.Queue), run.Id.ToString(), score);
        _ = tran.SortedSetAddAsync(RunsByStatusKey(run.Status), run.Id.ToString(), run.CreatedAt.Ticks);
        _ = tran.SortedSetAddAsync(RunsByJobKey(run.JobTypeId), run.Id.ToString(), run.CreatedAt.Ticks);
        await tran.ExecuteAsync();

        await PublishEventAsync(new JobEvent
        {
            Type = "job_enqueued",
            RunId = run.Id,
            JobTypeId = run.JobTypeId,
            Queue = run.Queue
        });

        return run.Id;
    }

    public async Task<JobRun?> DequeueAsync(string queue, string workerId, CancellationToken ct = default)
    {
        var now = DateTime.UtcNow.Ticks;
        var queueKey = QueueKey(queue);
        var processingKey = QueueProcessingKey(queue);

        // Lua script for atomic dequeue
        var script = @"
            local item = redis.call('ZRANGEBYSCORE', KEYS[1], '-inf', ARGV[1], 'LIMIT', 0, 1)
            if #item == 0 then return nil end
            local runId = item[1]
            redis.call('ZREM', KEYS[1], runId)
            redis.call('SADD', KEYS[2], runId)
            return runId
        ";

        var result = await _db.ScriptEvaluateAsync(script,
            new RedisKey[] { queueKey, processingKey },
            new RedisValue[] { now });

        if (result.IsNull) return null;

        var runId = Guid.Parse(result.ToString()!);
        var run = await GetRunAsync(runId, ct);

        if (run == null) return null;

        // Update run status
        run.Status = JobRunStatus.Running;
        run.StartedAt = DateTime.UtcNow;
        run.WorkerId = workerId;

        await UpdateRunInternalAsync(run);

        await PublishEventAsync(new JobEvent
        {
            Type = "job_started",
            RunId = run.Id,
            JobTypeId = run.JobTypeId,
            WorkerId = workerId
        });

        return run;
    }

    public async Task<JobRun?> GetRunAsync(Guid runId, CancellationToken ct = default)
    {
        var hash = await _db.HashGetAllAsync(RunKey(runId));
        if (hash.Length == 0) return null;

        return HashToRun(hash);
    }

    public async Task UpdateRunAsync(JobRun run, CancellationToken ct = default)
    {
        await UpdateRunInternalAsync(run);

        var eventType = run.Status switch
        {
            JobRunStatus.Completed => "job_completed",
            JobRunStatus.Failed => "job_failed",
            JobRunStatus.Cancelled => "job_cancelled",
            _ => "job_updated"
        };

        await PublishEventAsync(new JobEvent
        {
            Type = eventType,
            RunId = run.Id,
            JobTypeId = run.JobTypeId,
            Status = run.Status.ToString()
        });
    }

    private async Task UpdateRunInternalAsync(JobRun run)
    {
        var hash = RunToHash(run);
        var key = RunKey(run.Id);

        var tran = _db.CreateTransaction();
        _ = tran.HashSetAsync(key, hash);

        // Update status index
        foreach (JobRunStatus status in Enum.GetValues<JobRunStatus>())
        {
            _ = tran.SortedSetRemoveAsync(RunsByStatusKey(status), run.Id.ToString());
        }
        _ = tran.SortedSetAddAsync(RunsByStatusKey(run.Status), run.Id.ToString(), run.CreatedAt.Ticks);

        // Remove from processing if completed
        if (run.Status is JobRunStatus.Completed or JobRunStatus.Failed or JobRunStatus.Cancelled)
        {
            _ = tran.SetRemoveAsync(QueueProcessingKey(run.Queue), run.Id.ToString());
        }

        await tran.ExecuteAsync();
    }

    public async Task<IReadOnlyList<JobRun>> GetRunsByStatusAsync(
        JobRunStatus status,
        int limit = 100,
        int offset = 0,
        CancellationToken ct = default)
    {
        var runIds = await _db.SortedSetRangeByRankAsync(
            RunsByStatusKey(status),
            offset,
            offset + limit - 1,
            Order.Descending);

        var runs = new List<JobRun>();
        foreach (var id in runIds)
        {
            var run = await GetRunAsync(Guid.Parse(id.ToString()!), ct);
            if (run != null) runs.Add(run);
        }

        return runs;
    }

    #endregion

    #region Job Logs

    public async Task AddLogAsync(JobLog log, CancellationToken ct = default)
    {
        log.Id = log.Id == Guid.Empty ? Guid.NewGuid() : log.Id;
        log.Timestamp = DateTime.UtcNow;

        var json = JsonSerializer.Serialize(log, _jsonOptions);
        await _db.ListRightPushAsync(LogsKey(log.RunId), json);

        // Trim to keep only last N logs per run
        await _db.ListTrimAsync(LogsKey(log.RunId), -10000, -1);
    }

    public async Task<IReadOnlyList<JobLog>> GetLogsAsync(
        Guid runId,
        int limit = 1000,
        CancellationToken ct = default)
    {
        var logs = await _db.ListRangeAsync(LogsKey(runId), 0, limit - 1);

        return logs
            .Select(l => JsonSerializer.Deserialize<JobLog>(l.ToString()!, _jsonOptions)!)
            .ToList();
    }

    #endregion

    #region Heartbeats

    public async Task UpdateHeartbeatAsync(string workerId, CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;

        var tran = _db.CreateTransaction();
        _ = tran.HashSetAsync(HeartbeatsKey, workerId, now.Ticks);
        _ = tran.HashSetAsync(WorkerKey(workerId), new HashEntry[]
        {
            new("lastHeartbeat", now.Ticks),
            new("workerId", workerId)
        });
        await tran.ExecuteAsync();
    }

    public async Task<IReadOnlyList<WorkerInfo>> GetActiveWorkersAsync(
        TimeSpan timeout,
        CancellationToken ct = default)
    {
        var cutoff = (DateTime.UtcNow - timeout).Ticks;
        var heartbeats = await _db.HashGetAllAsync(HeartbeatsKey);

        var workers = new List<WorkerInfo>();
        foreach (var entry in heartbeats)
        {
            if ((long)entry.Value >= cutoff)
            {
                var workerHash = await _db.HashGetAllAsync(WorkerKey(entry.Name.ToString()!));
                workers.Add(HashToWorker(workerHash));
            }
        }

        return workers;
    }

    public async Task RemoveStaleHeartbeatsAsync(TimeSpan timeout, CancellationToken ct = default)
    {
        var cutoff = (DateTime.UtcNow - timeout).Ticks;
        var heartbeats = await _db.HashGetAllAsync(HeartbeatsKey);

        foreach (var entry in heartbeats)
        {
            if ((long)entry.Value < cutoff)
            {
                await _db.HashDeleteAsync(HeartbeatsKey, entry.Name);
                await _db.KeyDeleteAsync(WorkerKey(entry.Name.ToString()!));
            }
        }
    }

    #endregion

    #region Cleanup

    public async Task CleanupOldRunsAsync(TimeSpan retention, CancellationToken ct = default)
    {
        var cutoff = (DateTime.UtcNow - retention).Ticks;

        var completedStatuses = new[]
        {
            JobRunStatus.Completed,
            JobRunStatus.Failed,
            JobRunStatus.Cancelled
        };

        foreach (var status in completedStatuses)
        {
            var oldRuns = await _db.SortedSetRangeByScoreAsync(
                RunsByStatusKey(status),
                double.NegativeInfinity,
                cutoff,
                take: 1000);

            foreach (var runIdValue in oldRuns)
            {
                var runId = Guid.Parse(runIdValue.ToString()!);
                var run = await GetRunAsync(runId, ct);

                if (run != null)
                {
                    var tran = _db.CreateTransaction();
                    _ = tran.KeyDeleteAsync(RunKey(runId));
                    _ = tran.KeyDeleteAsync(LogsKey(runId));
                    _ = tran.SortedSetRemoveAsync(RunsByStatusKey(status), runIdValue);
                    _ = tran.SortedSetRemoveAsync(RunsByJobKey(run.JobTypeId), runIdValue);
                    await tran.ExecuteAsync();
                }
            }
        }
    }

    #endregion

    #region Pub/Sub

    public async Task PublishEventAsync(JobEvent jobEvent)
    {
        var json = JsonSerializer.Serialize(jobEvent, _jsonOptions);
        await _redis.GetSubscriber().PublishAsync(
            RedisChannel.Literal(PubSubChannel),
            json);
    }

    public async Task SubscribeAsync(
        Action<JobEvent> handler,
        CancellationToken ct = default)
    {
        var subscriber = _redis.GetSubscriber();
        await subscriber.SubscribeAsync(
            RedisChannel.Literal(PubSubChannel),
            (channel, message) =>
            {
                var jobEvent = JsonSerializer.Deserialize<JobEvent>(message.ToString()!, _jsonOptions);
                if (jobEvent != null)
                    handler(jobEvent);
            });
    }

    #endregion

    #region Hash Conversion

    private HashEntry[] DefinitionToHash(JobDefinition d) => new[]
    {
        new HashEntry("jobTypeId", d.JobTypeId),
        new HashEntry("displayName", d.DisplayName),
        new HashEntry("description", d.Description ?? ""),
        new HashEntry("queue", d.Queue),
        new HashEntry("scheduleType", (int)d.ScheduleType),
        new HashEntry("cronExpression", d.CronExpression ?? ""),
        new HashEntry("intervalMinutes", d.IntervalMinutes ?? 0),
        new HashEntry("timeoutSeconds", d.TimeoutSeconds ?? 3600),
        new HashEntry("maxRetries", d.MaxRetries),
        new HashEntry("retryDelaySeconds", d.RetryDelaySeconds),
        new HashEntry("isEnabled", d.IsEnabled ? 1 : 0),
        new HashEntry("defaultInputJson", d.DefaultInputJson ?? ""),
        new HashEntry("tags", string.Join(",", d.Tags ?? new())),
        new HashEntry("lastRunAt", d.LastRunAt?.Ticks ?? 0),
        new HashEntry("nextRunAt", d.NextRunAt?.Ticks ?? 0),
        new HashEntry("lastRunStatus", d.LastRunStatus?.ToString() ?? "")
    };

    private JobDefinition HashToDefinition(HashEntry[] hash)
    {
        var dict = hash.ToDictionary(h => h.Name.ToString(), h => h.Value);
        return new JobDefinition
        {
            JobTypeId = dict["jobTypeId"].ToString()!,
            DisplayName = dict["displayName"].ToString()!,
            Description = dict.GetValueOrDefault("description").ToString() is { Length: > 0 } desc ? desc : null,
            Queue = dict["queue"].ToString()!,
            ScheduleType = (ScheduleType)(int)dict["scheduleType"],
            CronExpression = dict.GetValueOrDefault("cronExpression").ToString() is { Length: > 0 } cron ? cron : null,
            IntervalMinutes = (int)dict["intervalMinutes"] > 0 ? (int)dict["intervalMinutes"] : null,
            TimeoutSeconds = (int)dict["timeoutSeconds"],
            MaxRetries = (int)dict["maxRetries"],
            RetryDelaySeconds = (int)dict["retryDelaySeconds"],
            IsEnabled = (int)dict["isEnabled"] == 1,
            DefaultInputJson = dict.GetValueOrDefault("defaultInputJson").ToString() is { Length: > 0 } json ? json : null,
            Tags = dict.GetValueOrDefault("tags").ToString()?.Split(',', StringSplitOptions.RemoveEmptyEntries).ToList() ?? new(),
            LastRunAt = (long)dict["lastRunAt"] > 0 ? new DateTime((long)dict["lastRunAt"], DateTimeKind.Utc) : null,
            NextRunAt = (long)dict["nextRunAt"] > 0 ? new DateTime((long)dict["nextRunAt"], DateTimeKind.Utc) : null,
            LastRunStatus = Enum.TryParse<JobRunStatus>(dict.GetValueOrDefault("lastRunStatus").ToString(), out var s) ? s : null
        };
    }

    private HashEntry[] RunToHash(JobRun r) => new[]
    {
        new HashEntry("id", r.Id.ToString()),
        new HashEntry("jobTypeId", r.JobTypeId),
        new HashEntry("status", (int)r.Status),
        new HashEntry("triggerType", (int)r.TriggerType),
        new HashEntry("queue", r.Queue),
        new HashEntry("inputJson", r.InputJson ?? ""),
        new HashEntry("outputJson", r.OutputJson ?? ""),
        new HashEntry("scheduledAt", r.ScheduledAt?.Ticks ?? 0),
        new HashEntry("createdAt", r.CreatedAt.Ticks),
        new HashEntry("startedAt", r.StartedAt?.Ticks ?? 0),
        new HashEntry("completedAt", r.CompletedAt?.Ticks ?? 0),
        new HashEntry("durationMs", r.DurationMs ?? 0),
        new HashEntry("errorMessage", r.ErrorMessage ?? ""),
        new HashEntry("errorDetails", r.ErrorDetails ?? ""),
        new HashEntry("workerId", r.WorkerId ?? ""),
        new HashEntry("attemptNumber", r.AttemptNumber),
        new HashEntry("correlationId", r.CorrelationId ?? ""),
        new HashEntry("parentRunId", r.ParentRunId?.ToString() ?? ""),
        new HashEntry("nextRetryAt", r.NextRetryAt?.Ticks ?? 0)
    };

    private JobRun HashToRun(HashEntry[] hash)
    {
        var dict = hash.ToDictionary(h => h.Name.ToString(), h => h.Value);
        return new JobRun
        {
            Id = Guid.Parse(dict["id"].ToString()!),
            JobTypeId = dict["jobTypeId"].ToString()!,
            Status = (JobRunStatus)(int)dict["status"],
            TriggerType = (JobTriggerType)(int)dict["triggerType"],
            Queue = dict["queue"].ToString()!,
            InputJson = dict.GetValueOrDefault("inputJson").ToString() is { Length: > 0 } inp ? inp : null,
            OutputJson = dict.GetValueOrDefault("outputJson").ToString() is { Length: > 0 } outp ? outp : null,
            ScheduledAt = (long)dict["scheduledAt"] > 0 ? new DateTime((long)dict["scheduledAt"], DateTimeKind.Utc) : null,
            CreatedAt = new DateTime((long)dict["createdAt"], DateTimeKind.Utc),
            StartedAt = (long)dict["startedAt"] > 0 ? new DateTime((long)dict["startedAt"], DateTimeKind.Utc) : null,
            CompletedAt = (long)dict["completedAt"] > 0 ? new DateTime((long)dict["completedAt"], DateTimeKind.Utc) : null,
            DurationMs = (int)dict["durationMs"] > 0 ? (int)dict["durationMs"] : null,
            ErrorMessage = dict.GetValueOrDefault("errorMessage").ToString() is { Length: > 0 } err ? err : null,
            ErrorDetails = dict.GetValueOrDefault("errorDetails").ToString() is { Length: > 0 } det ? det : null,
            WorkerId = dict.GetValueOrDefault("workerId").ToString() is { Length: > 0 } wid ? wid : null,
            AttemptNumber = (int)dict["attemptNumber"],
            CorrelationId = dict.GetValueOrDefault("correlationId").ToString() is { Length: > 0 } cid ? cid : null,
            ParentRunId = Guid.TryParse(dict.GetValueOrDefault("parentRunId").ToString(), out var pid) ? pid : null,
            NextRetryAt = (long)dict["nextRetryAt"] > 0 ? new DateTime((long)dict["nextRetryAt"], DateTimeKind.Utc) : null
        };
    }

    private WorkerInfo HashToWorker(HashEntry[] hash)
    {
        var dict = hash.ToDictionary(h => h.Name.ToString(), h => h.Value);
        return new WorkerInfo
        {
            WorkerId = dict["workerId"].ToString()!,
            LastHeartbeat = new DateTime((long)dict["lastHeartbeat"], DateTimeKind.Utc)
        };
    }

    #endregion
}
```

### Options

```csharp
// ZapJobs.Storage.Redis/RedisStorageOptions.cs
public class RedisStorageOptions
{
    /// <summary>Redis connection string</summary>
    public string ConnectionString { get; set; } = "localhost:6379";

    /// <summary>Redis database number (0-15)</summary>
    public int Database { get; set; } = 0;

    /// <summary>Prefix for all keys</summary>
    public string KeyPrefix { get; set; } = "zapjobs";

    /// <summary>Connection timeout</summary>
    public TimeSpan ConnectTimeout { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>Sync timeout for operations</summary>
    public TimeSpan SyncTimeout { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>Enable SSL/TLS</summary>
    public bool Ssl { get; set; }

    /// <summary>Password for authentication</summary>
    public string? Password { get; set; }
}
```

### Pub/Sub Interface

```csharp
// ZapJobs.Core/Abstractions/IJobStoragePubSub.cs
public interface IJobStoragePubSub
{
    Task PublishEventAsync(JobEvent jobEvent);
    Task SubscribeAsync(Action<JobEvent> handler, CancellationToken ct = default);
}

public class JobEvent
{
    public string Type { get; set; } = string.Empty;
    public Guid? RunId { get; set; }
    public string? JobTypeId { get; set; }
    public string? Queue { get; set; }
    public string? WorkerId { get; set; }
    public string? Status { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}
```

### Extension Methods

```csharp
// ZapJobs.Storage.Redis/RedisStorageExtensions.cs
public static class RedisStorageExtensions
{
    public static ZapJobsBuilder UseRedisStorage(
        this ZapJobsBuilder builder,
        Action<RedisStorageOptions>? configure = null)
    {
        var options = new RedisStorageOptions();
        configure?.Invoke(options);

        builder.Services.AddSingleton(options);

        builder.Services.AddSingleton<IConnectionMultiplexer>(sp =>
        {
            var opts = sp.GetRequiredService<RedisStorageOptions>();
            var config = ConfigurationOptions.Parse(opts.ConnectionString);
            config.DefaultDatabase = opts.Database;
            config.ConnectTimeout = (int)opts.ConnectTimeout.TotalMilliseconds;
            config.SyncTimeout = (int)opts.SyncTimeout.TotalMilliseconds;
            config.Ssl = opts.Ssl;
            if (!string.IsNullOrEmpty(opts.Password))
                config.Password = opts.Password;

            return ConnectionMultiplexer.Connect(config);
        });

        builder.Services.AddSingleton<IJobStorage, RedisJobStorage>();
        builder.Services.AddSingleton<IJobStoragePubSub>(sp =>
            (IJobStoragePubSub)sp.GetRequiredService<IJobStorage>());

        return builder;
    }

    public static ZapJobsBuilder UseRedisStorage(
        this ZapJobsBuilder builder,
        string connectionString)
    {
        return builder.UseRedisStorage(opts => opts.ConnectionString = connectionString);
    }
}
```

### Real-time Dashboard Integration

```csharp
// Com Pub/Sub, o SignalR hub pode assinar eventos
public class JobsHub : Hub
{
    private readonly IJobStoragePubSub _pubSub;

    public override async Task OnConnectedAsync()
    {
        await _pubSub.SubscribeAsync(async evt =>
        {
            await Clients.All.SendAsync("JobEvent", evt);
        });

        await base.OnConnectedAsync();
    }
}
```

## Exemplo de Uso

```csharp
// Program.cs
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddZapJobs()
    .UseRedisStorage(opts =>
    {
        opts.ConnectionString = "localhost:6379,password=secret";
        opts.Database = 1;
        opts.KeyPrefix = "myapp:jobs";
        opts.Ssl = true;
    })
    .AddJob<MyJob>();

// Or simpler
builder.Services.AddZapJobs()
    .UseRedisStorage("redis.example.com:6379")
    .AddJob<MyJob>();

// Using Azure Cache for Redis
builder.Services.AddZapJobs()
    .UseRedisStorage(opts =>
    {
        opts.ConnectionString = "myredis.redis.cache.windows.net:6380";
        opts.Password = builder.Configuration["Redis:Password"];
        opts.Ssl = true;
    })
    .AddJob<MyJob>();
```

## Considerações

1. **Atomicidade**: Lua scripts para operações complexas
2. **Pub/Sub**: Real-time updates para dashboard
3. **Sorted Sets**: Eficiente para filas com prioridade/schedule
4. **TTL**: Considerar expiração automática para logs
5. **Cluster**: Suporte a Redis Cluster (usar hash tags)

## Arquivos a Criar

```
src/ZapJobs.Storage.Redis/
├── ZapJobs.Storage.Redis.csproj
├── RedisJobStorage.cs
├── RedisStorageOptions.cs
├── RedisStorageExtensions.cs
└── Scripts/
    └── dequeue.lua
```

## Critérios de Aceitação

1. [ ] Todas as operações de IJobStorage implementadas
2. [ ] Dequeue atômico com Lua script
3. [ ] Pub/Sub para eventos em tempo real
4. [ ] Índices por status e job type
5. [ ] Cleanup de runs antigos funciona
6. [ ] Suporte a Redis Sentinel/Cluster
7. [ ] Performance superior a PostgreSQL
8. [ ] Testes com Redis Testcontainers
9. [ ] Documentação de estrutura de keys
10. [ ] Integração com SignalR demonstrada
