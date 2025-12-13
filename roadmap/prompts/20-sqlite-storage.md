# Prompt: Implementar SQLite Storage Provider

## Objetivo

Criar um provider de armazenamento SQLite para cenários onde PostgreSQL não é necessário ou desejado - aplicações menores, desenvolvimento local, ou deployments simplificados.

## Contexto

SQLite é ideal para:
- Aplicações single-instance
- Desenvolvimento local sem dependências
- Testes de integração rápidos
- Deployments simplificados (single file)
- Aplicações embarcadas

## Requisitos

### Novo Projeto

```xml
<!-- src/ZapJobs.Storage.SQLite/ZapJobs.Storage.SQLite.csproj -->
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net9.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.Data.Sqlite" Version="9.0.0" />
    <PackageReference Include="Dapper" Version="2.1.35" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\ZapJobs.Core\ZapJobs.Core.csproj" />
  </ItemGroup>
</Project>
```

### Implementação Principal

```csharp
// ZapJobs.Storage.SQLite/SqliteJobStorage.cs
using Microsoft.Data.Sqlite;
using Dapper;
using ZapJobs.Core.Abstractions;
using ZapJobs.Core.Entities;

namespace ZapJobs.Storage.SQLite;

public class SqliteJobStorage : IJobStorage, IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly string _tablePrefix;
    private readonly ILogger<SqliteJobStorage> _logger;
    private readonly object _lock = new();

    public SqliteJobStorage(
        SqliteStorageOptions options,
        ILogger<SqliteJobStorage> logger)
    {
        _tablePrefix = options.TablePrefix;
        _logger = logger;

        var connectionString = options.ConnectionString
            ?? $"Data Source={options.DatabasePath};Cache=Shared";

        _connection = new SqliteConnection(connectionString);
        _connection.Open();

        // Enable WAL mode for better concurrency
        ExecuteNonQuery("PRAGMA journal_mode=WAL;");
        ExecuteNonQuery("PRAGMA synchronous=NORMAL;");
        ExecuteNonQuery("PRAGMA cache_size=10000;");
        ExecuteNonQuery("PRAGMA temp_store=MEMORY;");
    }

    #region Job Definitions

    public async Task<IReadOnlyList<JobDefinition>> GetAllDefinitionsAsync(CancellationToken ct = default)
    {
        var sql = $"SELECT * FROM {_tablePrefix}definitions ORDER BY display_name";
        var results = await _connection.QueryAsync<JobDefinitionRow>(sql);
        return results.Select(MapToDefinition).ToList();
    }

    public async Task<JobDefinition?> GetJobDefinitionAsync(string jobTypeId, CancellationToken ct = default)
    {
        var sql = $"SELECT * FROM {_tablePrefix}definitions WHERE job_type_id = @jobTypeId";
        var row = await _connection.QueryFirstOrDefaultAsync<JobDefinitionRow>(sql, new { jobTypeId });
        return row != null ? MapToDefinition(row) : null;
    }

    public async Task UpsertDefinitionAsync(JobDefinition definition, CancellationToken ct = default)
    {
        var sql = $@"
            INSERT INTO {_tablePrefix}definitions (
                job_type_id, display_name, description, queue, schedule_type,
                cron_expression, interval_minutes, timeout_seconds, max_retries,
                retry_delay_seconds, is_enabled, default_input_json, tags,
                last_run_at, next_run_at, last_run_status
            ) VALUES (
                @JobTypeId, @DisplayName, @Description, @Queue, @ScheduleType,
                @CronExpression, @IntervalMinutes, @TimeoutSeconds, @MaxRetries,
                @RetryDelaySeconds, @IsEnabled, @DefaultInputJson, @Tags,
                @LastRunAt, @NextRunAt, @LastRunStatus
            )
            ON CONFLICT(job_type_id) DO UPDATE SET
                display_name = excluded.display_name,
                description = excluded.description,
                queue = excluded.queue,
                schedule_type = excluded.schedule_type,
                cron_expression = excluded.cron_expression,
                interval_minutes = excluded.interval_minutes,
                timeout_seconds = excluded.timeout_seconds,
                max_retries = excluded.max_retries,
                retry_delay_seconds = excluded.retry_delay_seconds,
                is_enabled = excluded.is_enabled,
                default_input_json = excluded.default_input_json,
                tags = excluded.tags";

        await _connection.ExecuteAsync(sql, new
        {
            definition.JobTypeId,
            definition.DisplayName,
            definition.Description,
            definition.Queue,
            ScheduleType = (int)definition.ScheduleType,
            definition.CronExpression,
            definition.IntervalMinutes,
            definition.TimeoutSeconds,
            definition.MaxRetries,
            definition.RetryDelaySeconds,
            definition.IsEnabled,
            definition.DefaultInputJson,
            Tags = string.Join(",", definition.Tags ?? new List<string>()),
            definition.LastRunAt,
            definition.NextRunAt,
            LastRunStatus = definition.LastRunStatus?.ToString()
        });
    }

    #endregion

    #region Job Runs

    public async Task<Guid> EnqueueAsync(JobRun run, CancellationToken ct = default)
    {
        var sql = $@"
            INSERT INTO {_tablePrefix}runs (
                id, job_type_id, status, trigger_type, queue, input_json,
                scheduled_at, created_at, attempt_number, correlation_id,
                parent_run_id
            ) VALUES (
                @Id, @JobTypeId, @Status, @TriggerType, @Queue, @InputJson,
                @ScheduledAt, @CreatedAt, @AttemptNumber, @CorrelationId,
                @ParentRunId
            )";

        run.Id = run.Id == Guid.Empty ? Guid.NewGuid() : run.Id;
        run.CreatedAt = DateTime.UtcNow;

        await _connection.ExecuteAsync(sql, new
        {
            run.Id,
            run.JobTypeId,
            Status = (int)run.Status,
            TriggerType = (int)run.TriggerType,
            run.Queue,
            run.InputJson,
            run.ScheduledAt,
            run.CreatedAt,
            run.AttemptNumber,
            run.CorrelationId,
            run.ParentRunId
        });

        return run.Id;
    }

    public async Task<JobRun?> DequeueAsync(string queue, string workerId, CancellationToken ct = default)
    {
        // SQLite doesn't support SELECT FOR UPDATE, use transaction with immediate lock
        lock (_lock)
        {
            var selectSql = $@"
                SELECT * FROM {_tablePrefix}runs
                WHERE status = @Pending
                  AND queue = @queue
                  AND (scheduled_at IS NULL OR scheduled_at <= @now)
                ORDER BY created_at
                LIMIT 1";

            var row = _connection.QueryFirstOrDefault<JobRunRow>(selectSql, new
            {
                Pending = (int)JobRunStatus.Pending,
                queue,
                now = DateTime.UtcNow
            });

            if (row == null) return null;

            var updateSql = $@"
                UPDATE {_tablePrefix}runs
                SET status = @Running,
                    started_at = @now,
                    worker_id = @workerId
                WHERE id = @id AND status = @Pending";

            var affected = _connection.Execute(updateSql, new
            {
                Running = (int)JobRunStatus.Running,
                now = DateTime.UtcNow,
                workerId,
                id = row.Id,
                Pending = (int)JobRunStatus.Pending
            });

            if (affected == 0) return null;

            var run = MapToRun(row);
            run.Status = JobRunStatus.Running;
            run.StartedAt = DateTime.UtcNow;
            run.WorkerId = workerId;
            return run;
        }
    }

    public async Task<IReadOnlyList<JobRun>> GetPendingRunsAsync(
        string queue,
        int limit = 100,
        CancellationToken ct = default)
    {
        var sql = $@"
            SELECT * FROM {_tablePrefix}runs
            WHERE status = @Pending
              AND queue = @queue
              AND (scheduled_at IS NULL OR scheduled_at <= @now)
            ORDER BY created_at
            LIMIT @limit";

        var results = await _connection.QueryAsync<JobRunRow>(sql, new
        {
            Pending = (int)JobRunStatus.Pending,
            queue,
            now = DateTime.UtcNow,
            limit
        });

        return results.Select(MapToRun).ToList();
    }

    public async Task UpdateRunAsync(JobRun run, CancellationToken ct = default)
    {
        var sql = $@"
            UPDATE {_tablePrefix}runs SET
                status = @Status,
                started_at = @StartedAt,
                completed_at = @CompletedAt,
                duration_ms = @DurationMs,
                output_json = @OutputJson,
                error_message = @ErrorMessage,
                error_details = @ErrorDetails,
                worker_id = @WorkerId,
                attempt_number = @AttemptNumber,
                next_retry_at = @NextRetryAt
            WHERE id = @Id";

        await _connection.ExecuteAsync(sql, new
        {
            run.Id,
            Status = (int)run.Status,
            run.StartedAt,
            run.CompletedAt,
            run.DurationMs,
            run.OutputJson,
            run.ErrorMessage,
            run.ErrorDetails,
            run.WorkerId,
            run.AttemptNumber,
            run.NextRetryAt
        });
    }

    public async Task<JobRun?> GetRunAsync(Guid runId, CancellationToken ct = default)
    {
        var sql = $"SELECT * FROM {_tablePrefix}runs WHERE id = @runId";
        var row = await _connection.QueryFirstOrDefaultAsync<JobRunRow>(sql, new { runId });
        return row != null ? MapToRun(row) : null;
    }

    #endregion

    #region Job Logs

    public async Task AddLogAsync(JobLog log, CancellationToken ct = default)
    {
        var sql = $@"
            INSERT INTO {_tablePrefix}logs (
                id, run_id, level, message, timestamp, category, exception
            ) VALUES (
                @Id, @RunId, @Level, @Message, @Timestamp, @Category, @Exception
            )";

        log.Id = log.Id == Guid.Empty ? Guid.NewGuid() : log.Id;
        log.Timestamp = DateTime.UtcNow;

        await _connection.ExecuteAsync(sql, new
        {
            log.Id,
            log.RunId,
            Level = (int)log.Level,
            log.Message,
            log.Timestamp,
            log.Category,
            log.Exception
        });
    }

    public async Task<IReadOnlyList<JobLog>> GetLogsAsync(
        Guid runId,
        int limit = 1000,
        CancellationToken ct = default)
    {
        var sql = $@"
            SELECT * FROM {_tablePrefix}logs
            WHERE run_id = @runId
            ORDER BY timestamp
            LIMIT @limit";

        var results = await _connection.QueryAsync<JobLogRow>(sql, new { runId, limit });
        return results.Select(MapToLog).ToList();
    }

    #endregion

    #region Heartbeats

    public async Task UpdateHeartbeatAsync(string workerId, CancellationToken ct = default)
    {
        var sql = $@"
            INSERT INTO {_tablePrefix}heartbeats (worker_id, last_heartbeat, started_at)
            VALUES (@workerId, @now, @now)
            ON CONFLICT(worker_id) DO UPDATE SET last_heartbeat = @now";

        await _connection.ExecuteAsync(sql, new { workerId, now = DateTime.UtcNow });
    }

    public async Task<IReadOnlyList<WorkerInfo>> GetActiveWorkersAsync(
        TimeSpan timeout,
        CancellationToken ct = default)
    {
        var cutoff = DateTime.UtcNow - timeout;
        var sql = $@"
            SELECT worker_id, last_heartbeat, started_at, current_job_id, current_job_started_at
            FROM {_tablePrefix}heartbeats
            WHERE last_heartbeat >= @cutoff";

        var results = await _connection.QueryAsync<HeartbeatRow>(sql, new { cutoff });
        return results.Select(r => new WorkerInfo
        {
            WorkerId = r.WorkerId,
            LastHeartbeat = r.LastHeartbeat,
            StartedAt = r.StartedAt,
            CurrentJobId = r.CurrentJobId,
            CurrentJobStartedAt = r.CurrentJobStartedAt
        }).ToList();
    }

    #endregion

    #region Cleanup

    public async Task CleanupOldRunsAsync(TimeSpan retention, CancellationToken ct = default)
    {
        var cutoff = DateTime.UtcNow - retention;

        // Delete logs first (foreign key)
        var deleteLogsSql = $@"
            DELETE FROM {_tablePrefix}logs
            WHERE run_id IN (
                SELECT id FROM {_tablePrefix}runs
                WHERE completed_at < @cutoff
                  AND status IN (@Completed, @Failed, @Cancelled)
            )";

        var deleteRunsSql = $@"
            DELETE FROM {_tablePrefix}runs
            WHERE completed_at < @cutoff
              AND status IN (@Completed, @Failed, @Cancelled)";

        await _connection.ExecuteAsync(deleteLogsSql, new
        {
            cutoff,
            Completed = (int)JobRunStatus.Completed,
            Failed = (int)JobRunStatus.Failed,
            Cancelled = (int)JobRunStatus.Cancelled
        });

        var deleted = await _connection.ExecuteAsync(deleteRunsSql, new
        {
            cutoff,
            Completed = (int)JobRunStatus.Completed,
            Failed = (int)JobRunStatus.Failed,
            Cancelled = (int)JobRunStatus.Cancelled
        });

        _logger.LogInformation("Cleaned up {Count} old job runs", deleted);
    }

    #endregion

    #region Mapping

    private static JobDefinition MapToDefinition(JobDefinitionRow row) => new()
    {
        JobTypeId = row.JobTypeId,
        DisplayName = row.DisplayName,
        Description = row.Description,
        Queue = row.Queue,
        ScheduleType = (ScheduleType)row.ScheduleType,
        CronExpression = row.CronExpression,
        IntervalMinutes = row.IntervalMinutes,
        TimeoutSeconds = row.TimeoutSeconds,
        MaxRetries = row.MaxRetries,
        RetryDelaySeconds = row.RetryDelaySeconds,
        IsEnabled = row.IsEnabled,
        DefaultInputJson = row.DefaultInputJson,
        Tags = row.Tags?.Split(',', StringSplitOptions.RemoveEmptyEntries).ToList() ?? new(),
        LastRunAt = row.LastRunAt,
        NextRunAt = row.NextRunAt,
        LastRunStatus = Enum.TryParse<JobRunStatus>(row.LastRunStatus, out var status) ? status : null
    };

    private static JobRun MapToRun(JobRunRow row) => new()
    {
        Id = row.Id,
        JobTypeId = row.JobTypeId,
        Status = (JobRunStatus)row.Status,
        TriggerType = (JobTriggerType)row.TriggerType,
        Queue = row.Queue,
        InputJson = row.InputJson,
        OutputJson = row.OutputJson,
        ScheduledAt = row.ScheduledAt,
        CreatedAt = row.CreatedAt,
        StartedAt = row.StartedAt,
        CompletedAt = row.CompletedAt,
        DurationMs = row.DurationMs,
        ErrorMessage = row.ErrorMessage,
        ErrorDetails = row.ErrorDetails,
        WorkerId = row.WorkerId,
        AttemptNumber = row.AttemptNumber,
        CorrelationId = row.CorrelationId,
        ParentRunId = row.ParentRunId,
        NextRetryAt = row.NextRetryAt
    };

    private static JobLog MapToLog(JobLogRow row) => new()
    {
        Id = row.Id,
        RunId = row.RunId,
        Level = (LogLevel)row.Level,
        Message = row.Message,
        Timestamp = row.Timestamp,
        Category = row.Category,
        Exception = row.Exception
    };

    #endregion

    private void ExecuteNonQuery(string sql)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    public void Dispose()
    {
        _connection?.Dispose();
    }
}
```

### Options

```csharp
// ZapJobs.Storage.SQLite/SqliteStorageOptions.cs
public class SqliteStorageOptions
{
    /// <summary>Path to the SQLite database file</summary>
    public string DatabasePath { get; set; } = "zapjobs.db";

    /// <summary>Full connection string (overrides DatabasePath)</summary>
    public string? ConnectionString { get; set; }

    /// <summary>Prefix for table names</summary>
    public string TablePrefix { get; set; } = "zj_";

    /// <summary>Run migrations on startup</summary>
    public bool AutoMigrate { get; set; } = true;
}
```

### Migration Runner

```csharp
// ZapJobs.Storage.SQLite/SqliteMigrationRunner.cs
public class SqliteMigrationRunner
{
    private readonly SqliteConnection _connection;
    private readonly string _prefix;

    public async Task RunMigrationsAsync(CancellationToken ct = default)
    {
        // Create migrations table
        await ExecuteAsync($@"
            CREATE TABLE IF NOT EXISTS {_prefix}migrations (
                version INTEGER PRIMARY KEY,
                applied_at TEXT NOT NULL
            )");

        var currentVersion = await GetCurrentVersionAsync();

        if (currentVersion < 1)
        {
            await ApplyMigration1Async();
        }

        // Add more migrations as needed
    }

    private async Task ApplyMigration1Async()
    {
        await ExecuteAsync($@"
            CREATE TABLE IF NOT EXISTS {_prefix}definitions (
                job_type_id TEXT PRIMARY KEY,
                display_name TEXT NOT NULL,
                description TEXT,
                queue TEXT NOT NULL DEFAULT 'default',
                schedule_type INTEGER NOT NULL DEFAULT 0,
                cron_expression TEXT,
                interval_minutes INTEGER,
                timeout_seconds INTEGER DEFAULT 3600,
                max_retries INTEGER DEFAULT 3,
                retry_delay_seconds INTEGER DEFAULT 30,
                is_enabled INTEGER NOT NULL DEFAULT 1,
                default_input_json TEXT,
                tags TEXT,
                last_run_at TEXT,
                next_run_at TEXT,
                last_run_status TEXT
            )");

        await ExecuteAsync($@"
            CREATE TABLE IF NOT EXISTS {_prefix}runs (
                id TEXT PRIMARY KEY,
                job_type_id TEXT NOT NULL,
                status INTEGER NOT NULL DEFAULT 0,
                trigger_type INTEGER NOT NULL DEFAULT 0,
                queue TEXT NOT NULL DEFAULT 'default',
                input_json TEXT,
                output_json TEXT,
                scheduled_at TEXT,
                created_at TEXT NOT NULL,
                started_at TEXT,
                completed_at TEXT,
                duration_ms INTEGER,
                error_message TEXT,
                error_details TEXT,
                worker_id TEXT,
                attempt_number INTEGER NOT NULL DEFAULT 1,
                correlation_id TEXT,
                parent_run_id TEXT,
                next_retry_at TEXT
            )");

        await ExecuteAsync($@"
            CREATE INDEX IF NOT EXISTS idx_{_prefix}runs_status_queue
            ON {_prefix}runs(status, queue, scheduled_at)");

        await ExecuteAsync($@"
            CREATE INDEX IF NOT EXISTS idx_{_prefix}runs_job_type
            ON {_prefix}runs(job_type_id, created_at DESC)");

        await ExecuteAsync($@"
            CREATE TABLE IF NOT EXISTS {_prefix}logs (
                id TEXT PRIMARY KEY,
                run_id TEXT NOT NULL,
                level INTEGER NOT NULL,
                message TEXT NOT NULL,
                timestamp TEXT NOT NULL,
                category TEXT,
                exception TEXT,
                FOREIGN KEY (run_id) REFERENCES {_prefix}runs(id)
            )");

        await ExecuteAsync($@"
            CREATE INDEX IF NOT EXISTS idx_{_prefix}logs_run
            ON {_prefix}logs(run_id, timestamp)");

        await ExecuteAsync($@"
            CREATE TABLE IF NOT EXISTS {_prefix}heartbeats (
                worker_id TEXT PRIMARY KEY,
                last_heartbeat TEXT NOT NULL,
                started_at TEXT NOT NULL,
                current_job_id TEXT,
                current_job_started_at TEXT
            )");

        await RecordMigrationAsync(1);
    }
}
```

### Extension Methods

```csharp
// ZapJobs.Storage.SQLite/SqliteStorageExtensions.cs
public static class SqliteStorageExtensions
{
    public static ZapJobsBuilder UseSqliteStorage(
        this ZapJobsBuilder builder,
        Action<SqliteStorageOptions>? configure = null)
    {
        var options = new SqliteStorageOptions();
        configure?.Invoke(options);

        builder.Services.AddSingleton(options);
        builder.Services.AddSingleton<IJobStorage, SqliteJobStorage>();

        if (options.AutoMigrate)
        {
            builder.Services.AddHostedService<SqliteMigrationService>();
        }

        return builder;
    }

    public static ZapJobsBuilder UseSqliteStorage(
        this ZapJobsBuilder builder,
        string databasePath)
    {
        return builder.UseSqliteStorage(opts => opts.DatabasePath = databasePath);
    }
}
```

## Exemplo de Uso

```csharp
// Program.cs
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddZapJobs()
    .UseSqliteStorage(opts =>
    {
        opts.DatabasePath = "data/zapjobs.db";
        opts.TablePrefix = "jobs_";
        opts.AutoMigrate = true;
    })
    .AddJob<MyJob>();

// Or simpler
builder.Services.AddZapJobs()
    .UseSqliteStorage("zapjobs.db")
    .AddJob<MyJob>();

// For in-memory testing
builder.Services.AddZapJobs()
    .UseSqliteStorage(opts =>
    {
        opts.ConnectionString = "Data Source=:memory:;Cache=Shared";
    })
    .AddJob<MyJob>();
```

## Considerações de Concorrência

SQLite tem limitações de concorrência:
1. Usar WAL mode para melhor performance
2. Lock interno no DequeueAsync
3. Pool de conexões compartilhado
4. Evitar deployments multi-processo

## Arquivos a Criar

```
src/ZapJobs.Storage.SQLite/
├── ZapJobs.Storage.SQLite.csproj
├── SqliteJobStorage.cs
├── SqliteStorageOptions.cs
├── SqliteMigrationRunner.cs
├── SqliteMigrationService.cs
├── SqliteStorageExtensions.cs
└── Rows/
    ├── JobDefinitionRow.cs
    ├── JobRunRow.cs
    ├── JobLogRow.cs
    └── HeartbeatRow.cs
```

## Critérios de Aceitação

1. [ ] Todas as operações de IJobStorage implementadas
2. [ ] WAL mode configurado por padrão
3. [ ] Migrations automáticas funcionam
4. [ ] Dequeue thread-safe
5. [ ] Cleanup de runs antigos funciona
6. [ ] Testes com SQLite in-memory
7. [ ] Performance aceitável para single instance
8. [ ] Documentação de limitações
9. [ ] Exemplos de uso no README
