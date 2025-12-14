# Estado Atual do ZapJobs

Documento detalhado do estado atual do projeto, incluindo arquitetura, features implementadas e estrutura de código.

## Visão Geral

ZapJobs é um job scheduler database-driven para .NET, com foco em:
- Persistência em banco de dados (PostgreSQL)
- Execução distribuída com múltiplos workers
- Dashboard web para monitoramento
- CLI para gerenciamento

## Estrutura de Projetos

```
src/
├── ZapJobs.Core/               # Abstrações e entidades core
│   ├── Abstractions/           # Interfaces (IJob, IJobStorage, etc.)
│   ├── Configuration/          # ZapJobsOptions, RetryPolicy
│   ├── Context/                # JobExecutionContext
│   └── Entities/               # JobDefinition, JobRun, JobLog, etc.
│
├── ZapJobs/                    # Implementação principal
│   ├── Execution/              # JobExecutor, RetryHandler
│   ├── HostedServices/         # JobProcessorHostedService, etc.
│   ├── Scheduling/             # JobSchedulerService, CronScheduler
│   └── Tracking/               # HeartbeatService, JobTrackerService
│
├── ZapJobs.Storage.PostgreSQL/ # Storage PostgreSQL
│   ├── PostgreSqlJobStorage.cs
│   ├── MigrationRunner.cs
│   └── ServiceCollectionExtensions.cs
│
├── ZapJobs.Storage.InMemory/   # Storage em memória
│   └── InMemoryJobStorage.cs
│
├── ZapJobs.AspNetCore/         # Dashboard web
│   ├── Dashboard/              # DashboardMiddleware
│   └── ZapJobsDashboardOptions.cs
│
└── ZapJobs.Cli/                # CLI tool
    ├── Commands/               # InitCommand, MigrateCommand, StatusCommand
    └── Program.cs
```

## Interfaces Principais

### IJob
```csharp
public interface IJob
{
    string JobTypeId { get; }
    Task ExecuteAsync(JobExecutionContext context, CancellationToken ct);
}

public interface IJob<TInput> : IJob where TInput : class
{
    Task ExecuteAsync(JobExecutionContext<TInput> context, CancellationToken ct);
}

public interface IJob<TInput, TOutput> : IJob<TInput>
    where TInput : class
    where TOutput : class
{
    new Task<TOutput> ExecuteAsync(JobExecutionContext<TInput> context, CancellationToken ct);
}
```

### IJobScheduler
```csharp
public interface IJobScheduler
{
    Task<Guid> EnqueueAsync(string jobTypeId, object? input = null, string? queue = null, CancellationToken ct = default);
    Task<Guid> EnqueueAsync<TJob>(object? input = null, string? queue = null, CancellationToken ct = default);
    Task<Guid> ScheduleAsync(string jobTypeId, TimeSpan delay, object? input = null, string? queue = null, CancellationToken ct = default);
    Task<Guid> ScheduleAsync(string jobTypeId, DateTimeOffset runAt, object? input = null, string? queue = null, CancellationToken ct = default);
    Task<string> RecurringAsync(string jobTypeId, TimeSpan interval, object? input = null, CancellationToken ct = default);
    Task<string> RecurringAsync(string jobTypeId, string cronExpression, object? input = null, TimeZoneInfo? timeZone = null, CancellationToken ct = default);
    Task<bool> CancelAsync(Guid runId, CancellationToken ct = default);
    Task<bool> RemoveRecurringAsync(string jobTypeId, CancellationToken ct = default);
    Task<Guid> TriggerAsync(string jobTypeId, object? input = null, CancellationToken ct = default);
}
```

### IJobStorage
```csharp
public interface IJobStorage
{
    // Job Definitions
    Task<JobDefinition?> GetJobDefinitionAsync(string jobTypeId, CancellationToken ct = default);
    Task<IReadOnlyList<JobDefinition>> GetAllDefinitionsAsync(CancellationToken ct = default);
    Task UpsertDefinitionAsync(JobDefinition definition, CancellationToken ct = default);
    Task DeleteDefinitionAsync(string jobTypeId, CancellationToken ct = default);

    // Job Runs
    Task<Guid> EnqueueAsync(JobRun run, CancellationToken ct = default);
    Task<JobRun?> GetRunAsync(Guid runId, CancellationToken ct = default);
    Task<IReadOnlyList<JobRun>> GetPendingRunsAsync(string[] queues, int limit = 100, CancellationToken ct = default);
    Task<IReadOnlyList<JobRun>> GetRunsForRetryAsync(CancellationToken ct = default);
    Task<IReadOnlyList<JobRun>> GetRunsByStatusAsync(JobRunStatus status, int limit = 100, int offset = 0, CancellationToken ct = default);
    Task<IReadOnlyList<JobRun>> GetRunsByJobTypeAsync(string jobTypeId, int limit = 100, int offset = 0, CancellationToken ct = default);
    Task UpdateRunAsync(JobRun run, CancellationToken ct = default);
    Task<bool> TryAcquireRunAsync(Guid runId, string workerId, CancellationToken ct = default);

    // Scheduling
    Task<IReadOnlyList<JobDefinition>> GetDueJobsAsync(DateTime asOf, CancellationToken ct = default);
    Task UpdateNextRunAsync(string jobTypeId, DateTime? nextRun, DateTime? lastRun = null, JobRunStatus? lastStatus = null, CancellationToken ct = default);

    // Logs
    Task AddLogAsync(JobLog log, CancellationToken ct = default);
    Task AddLogsAsync(IEnumerable<JobLog> logs, CancellationToken ct = default);
    Task<IReadOnlyList<JobLog>> GetLogsAsync(Guid runId, int limit = 500, CancellationToken ct = default);

    // Heartbeats
    Task SendHeartbeatAsync(JobHeartbeat heartbeat, CancellationToken ct = default);
    Task<IReadOnlyList<JobHeartbeat>> GetHeartbeatsAsync(CancellationToken ct = default);
    Task<IReadOnlyList<JobHeartbeat>> GetStaleHeartbeatsAsync(TimeSpan threshold, CancellationToken ct = default);
    Task CleanupStaleHeartbeatsAsync(TimeSpan threshold, CancellationToken ct = default);

    // Maintenance
    Task<int> CleanupOldRunsAsync(TimeSpan retention, CancellationToken ct = default);
    Task<int> CleanupOldLogsAsync(TimeSpan retention, CancellationToken ct = default);
    Task<JobStorageStats> GetStatsAsync(CancellationToken ct = default);
}
```

## Entidades

### JobDefinition
```csharp
public class JobDefinition
{
    public string JobTypeId { get; set; }
    public string DisplayName { get; set; }
    public string? Description { get; set; }
    public string Queue { get; set; } = "default";

    // Scheduling
    public ScheduleType ScheduleType { get; set; } = ScheduleType.Manual;
    public int? IntervalMinutes { get; set; }
    public string? CronExpression { get; set; }
    public string? TimeZoneId { get; set; }

    // Behavior
    public bool IsEnabled { get; set; } = true;
    public int MaxRetries { get; set; } = 3;
    public int TimeoutSeconds { get; set; } = 3600;
    public int MaxConcurrency { get; set; } = 1;

    // State
    public DateTime? LastRunAt { get; set; }
    public DateTime? NextRunAt { get; set; }
    public JobRunStatus? LastRunStatus { get; set; }

    // Metadata
    public string? ConfigJson { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
```

### JobRun
```csharp
public class JobRun
{
    public Guid Id { get; set; }
    public string JobTypeId { get; set; }

    // Execution state
    public JobRunStatus Status { get; set; } = JobRunStatus.Pending;
    public JobTriggerType TriggerType { get; set; } = JobTriggerType.Manual;
    public string? TriggeredBy { get; set; }
    public string? WorkerId { get; set; }
    public string Queue { get; set; } = "default";

    // Timing
    public DateTime CreatedAt { get; set; }
    public DateTime? ScheduledAt { get; set; }
    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public int? DurationMs { get; set; }

    // Progress
    public int Progress { get; set; }
    public int Total { get; set; }
    public string? ProgressMessage { get; set; }

    // Metrics
    public int ItemsProcessed { get; set; }
    public int ItemsSucceeded { get; set; }
    public int ItemsFailed { get; set; }

    // Retry
    public int AttemptNumber { get; set; } = 1;
    public DateTime? NextRetryAt { get; set; }

    // Error
    public string? ErrorMessage { get; set; }
    public string? StackTrace { get; set; }
    public string? ErrorType { get; set; }

    // Data
    public string? InputJson { get; set; }
    public string? OutputJson { get; set; }
    public string? MetadataJson { get; set; }
}
```

### Enums
```csharp
public enum JobRunStatus
{
    Pending = 0,
    Scheduled = 1,
    Running = 2,
    Completed = 3,
    Failed = 4,
    Cancelled = 5,
    AwaitingRetry = 6
}

public enum JobTriggerType
{
    Manual = 0,
    Scheduled = 1,
    Cron = 2,
    Api = 3,
    Retry = 4,
    Continuation = 5
}

public enum ScheduleType
{
    Manual = 0,
    Interval = 1,
    Cron = 2
}

public enum JobLogLevel
{
    Trace = 0,
    Debug = 1,
    Info = 2,
    Warning = 3,
    Error = 4,
    Critical = 5
}
```

## Features Implementadas

### Scheduling
- [x] Enqueue imediato
- [x] Schedule com delay
- [x] Schedule em data/hora específica
- [x] Jobs recorrentes por intervalo
- [x] Jobs recorrentes por CRON (usando Cronos)
- [x] Suporte a timezone para CRON
- [x] Cancelamento de jobs
- [x] Trigger manual de jobs recorrentes

### Execution
- [x] Execução com timeout configurável
- [x] Retry com backoff exponencial
- [x] Jitter para prevenir thundering herd
- [x] Exceções não-retentáveis configuráveis
- [x] Semáforo para controle de concorrência
- [x] Worker ID único por instância
- [x] Acquisition atômico de jobs (previne duplicação)

### Tracking
- [x] Heartbeat de workers
- [x] Detecção de workers stale/dead
- [x] Logging estruturado por job
- [x] Métricas de items (processed, succeeded, failed)
- [x] Progress tracking
- [x] Estatísticas agregadas

### Storage
- [x] PostgreSQL com Npgsql
- [x] InMemory para testes/desenvolvimento
- [x] Schema customizável
- [x] Table prefix customizável
- [x] Auto-migration
- [x] Migration runner manual
- [x] Índices otimizados
- [x] Cleanup de runs/logs antigos

### Dashboard Web
- [x] Overview com estatísticas
- [x] Lista de job definitions
- [x] Lista de runs com filtros
- [x] Lista de workers ativos
- [x] Detalhes de run com logs
- [x] Trigger manual de jobs
- [x] Cancelamento de jobs
- [x] Setup wizard para migração
- [x] Auto-refresh configurável
- [x] Autenticação/autorização opcional

### CLI
- [x] Comando `init` - setup interativo
- [x] Comando `migrate` - execução de migrations
- [x] Comando `status` - monitoramento
- [x] Watch mode com refresh automático
- [x] Dry-run para migrations
- [x] Drop tables com confirmação

## Dependências

### ZapJobs.Core
- Nenhuma dependência externa (apenas .NET)

### ZapJobs
- Cronos 0.8.4 - CRON expression parsing
- Microsoft.Extensions.* 9.0.0 - DI, Configuration, Hosting, Logging

### ZapJobs.Storage.PostgreSQL
- Npgsql 9.0.2

### ZapJobs.Cli
- Spectre.Console 0.49.1
- Spectre.Console.Cli 0.49.1

### ZapJobs.AspNetCore
- Microsoft.AspNetCore.App (FrameworkReference)

## Schema PostgreSQL

```sql
-- Job Definitions
CREATE TABLE zapjobs_definitions (
    job_type_id VARCHAR(255) PRIMARY KEY,
    display_name VARCHAR(255) NOT NULL,
    description TEXT,
    queue VARCHAR(100) NOT NULL DEFAULT 'default',
    schedule_type INTEGER NOT NULL DEFAULT 0,
    interval_minutes INTEGER,
    cron_expression VARCHAR(100),
    time_zone_id VARCHAR(100),
    is_enabled BOOLEAN NOT NULL DEFAULT TRUE,
    max_retries INTEGER NOT NULL DEFAULT 3,
    timeout_seconds INTEGER NOT NULL DEFAULT 3600,
    max_concurrency INTEGER NOT NULL DEFAULT 1,
    last_run_at TIMESTAMP,
    next_run_at TIMESTAMP,
    last_run_status INTEGER,
    config_json TEXT,
    created_at TIMESTAMP NOT NULL DEFAULT NOW(),
    updated_at TIMESTAMP NOT NULL DEFAULT NOW()
);

-- Job Runs
CREATE TABLE zapjobs_runs (
    id UUID PRIMARY KEY,
    job_type_id VARCHAR(255) NOT NULL,
    status INTEGER NOT NULL DEFAULT 0,
    trigger_type INTEGER NOT NULL DEFAULT 0,
    triggered_by VARCHAR(255),
    worker_id VARCHAR(255),
    queue VARCHAR(100) NOT NULL DEFAULT 'default',
    created_at TIMESTAMP NOT NULL DEFAULT NOW(),
    scheduled_at TIMESTAMP,
    started_at TIMESTAMP,
    completed_at TIMESTAMP,
    duration_ms INTEGER,
    progress INTEGER NOT NULL DEFAULT 0,
    total INTEGER NOT NULL DEFAULT 0,
    progress_message TEXT,
    items_processed INTEGER NOT NULL DEFAULT 0,
    items_succeeded INTEGER NOT NULL DEFAULT 0,
    items_failed INTEGER NOT NULL DEFAULT 0,
    attempt_number INTEGER NOT NULL DEFAULT 1,
    next_retry_at TIMESTAMP,
    error_message TEXT,
    stack_trace TEXT,
    error_type VARCHAR(500),
    input_json TEXT,
    output_json TEXT,
    metadata_json TEXT
);

-- Job Logs
CREATE TABLE zapjobs_logs (
    id UUID PRIMARY KEY,
    run_id UUID NOT NULL,
    level INTEGER NOT NULL DEFAULT 2,
    message TEXT NOT NULL,
    category VARCHAR(100),
    context_json TEXT,
    duration_ms INTEGER,
    exception TEXT,
    timestamp TIMESTAMP NOT NULL DEFAULT NOW()
);

-- Worker Heartbeats
CREATE TABLE zapjobs_heartbeats (
    id UUID,
    worker_id VARCHAR(255) PRIMARY KEY,
    hostname VARCHAR(255) NOT NULL,
    process_id INTEGER NOT NULL,
    current_job_type VARCHAR(255),
    current_run_id UUID,
    timestamp TIMESTAMP NOT NULL DEFAULT NOW(),
    queues TEXT[] NOT NULL DEFAULT ARRAY['default'],
    jobs_processed INTEGER NOT NULL DEFAULT 0,
    jobs_failed INTEGER NOT NULL DEFAULT 0,
    is_shutting_down BOOLEAN NOT NULL DEFAULT FALSE,
    started_at TIMESTAMP NOT NULL DEFAULT NOW()
);
```

## O Que Falta

### Qualidade
- [ ] Testes unitários
- [ ] Testes de integração
- [ ] Benchmarks
- [ ] Code coverage

### Distribuição
- [ ] Publicação NuGet
- [ ] CI/CD (GitHub Actions)
- [ ] Versionamento semântico

### Features Avançadas
- [ ] Job dependencies (chains)
- [ ] Dead letter queue
- [ ] Rate limiting
- [ ] Job priority (dentro da fila)
- [ ] Bulk operations

### Observabilidade
- [ ] OpenTelemetry
- [ ] Prometheus metrics
- [ ] Health checks

### Extensibilidade
- [ ] Webhooks
- [ ] API REST standalone
- [ ] gRPC
- [ ] SignalR real-time
- [ ] Job templates
- [ ] Multi-tenancy

### Outros Storages
- [ ] SQLite
- [ ] MySQL/MariaDB
- [ ] MongoDB
- [ ] Redis
