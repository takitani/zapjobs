# Prompt: Implementar Prevent Overlapping

## Objetivo

Adicionar opção para prevenir que um job recorrente execute novamente enquanto uma instância anterior ainda está rodando. Similar ao Coravel `.PreventOverlapping()`.

## Contexto

Atualmente, se um job CRON está configurado para rodar a cada 5 minutos mas a execução demora 7 minutos, haverá duas instâncias rodando simultaneamente. Para muitos casos isso é indesejado.

## Requisitos

### Atualizar JobDefinition

```csharp
public class JobDefinition
{
    // ... existing properties ...

    /// <summary>
    /// If true, a new run won't be created while another is still running
    /// </summary>
    public bool PreventOverlapping { get; set; } = false;
}
```

### Atualizar Schema

```sql
ALTER TABLE {prefix}definitions
ADD COLUMN IF NOT EXISTS prevent_overlapping BOOLEAN NOT NULL DEFAULT FALSE;
```

### Lógica de Verificação

No `JobSchedulerService` ou `JobProcessorHostedService`, antes de criar um novo run:

```csharp
private async Task<bool> CanCreateRunAsync(JobDefinition definition, CancellationToken ct)
{
    if (!definition.PreventOverlapping)
        return true;

    // Check if there's a running instance
    var runningRuns = await _storage.GetRunsByJobTypeAsync(
        definition.JobTypeId,
        limit: 1,
        ct: ct);

    var hasRunning = runningRuns.Any(r =>
        r.Status == JobRunStatus.Running ||
        r.Status == JobRunStatus.Pending);

    if (hasRunning)
    {
        _logger.LogDebug(
            "Skipping job {JobTypeId} due to prevent overlapping - instance already running",
            definition.JobTypeId);
        return false;
    }

    return true;
}
```

### Otimização com Query Específica

Para melhor performance, adicionar método dedicado:

```csharp
public interface IJobStorage
{
    // ... existing ...

    /// <summary>
    /// Check if there are any running or pending jobs of this type
    /// </summary>
    Task<bool> HasActiveRunAsync(string jobTypeId, CancellationToken ct = default);
}
```

Implementação PostgreSQL:
```sql
SELECT EXISTS (
    SELECT 1 FROM {prefix}runs
    WHERE job_type_id = @jobTypeId
    AND status IN (0, 2)  -- Pending or Running
)
```

### Fluent API

```csharp
public static class ZapJobsBuilderExtensions
{
    public static IZapJobsBuilder AddJob<TJob>(
        this IZapJobsBuilder builder,
        Action<JobDefinitionBuilder>? configure = null) where TJob : IJob
    {
        var jobBuilder = new JobDefinitionBuilder();
        configure?.Invoke(jobBuilder);
        // ... register with options
    }
}

public class JobDefinitionBuilder
{
    private readonly JobDefinition _definition = new();

    public JobDefinitionBuilder PreventOverlapping()
    {
        _definition.PreventOverlapping = true;
        return this;
    }

    public JobDefinitionBuilder WithQueue(string queue)
    {
        _definition.Queue = queue;
        return this;
    }

    public JobDefinitionBuilder WithMaxRetries(int retries)
    {
        _definition.MaxRetries = retries;
        return this;
    }

    public JobDefinitionBuilder WithTimeout(TimeSpan timeout)
    {
        _definition.TimeoutSeconds = (int)timeout.TotalSeconds;
        return this;
    }
}
```

## Exemplo de Uso

```csharp
// Via configuration
builder.Services.AddZapJobs()
    .AddJob<LongRunningReportJob>(opts => opts
        .PreventOverlapping()
        .WithTimeout(TimeSpan.FromHours(2)))
    .AddJob<QuickSyncJob>(); // Can overlap

// Via scheduler
await scheduler.RecurringAsync(
    "long-report",
    "0 */5 * * *",  // Every 5 minutes
    options: new RecurringJobOptions { PreventOverlapping = true });
```

### Atualizar RecurringAsync

```csharp
public class RecurringJobOptions
{
    public bool PreventOverlapping { get; set; }
    public TimeZoneInfo? TimeZone { get; set; }
    public string? Queue { get; set; }
}

Task<string> RecurringAsync(
    string jobTypeId,
    string cronExpression,
    object? input = null,
    RecurringJobOptions? options = null,
    CancellationToken ct = default);
```

## Dashboard

Na página de Jobs, mostrar indicador:
- Ícone de "lock" ou badge "No Overlap" para jobs com PreventOverlapping
- Na coluna Status, mostrar "Skipped (overlap)" quando aplicável

## Arquivos a Modificar

1. `ZapJobs.Core/Entities/JobDefinition.cs` - Nova propriedade
2. `ZapJobs.Core/Abstractions/IJobStorage.cs` - Novo método
3. `ZapJobs/Scheduling/JobSchedulerService.cs` - Verificar overlap
4. `ZapJobs/HostedServices/JobProcessorHostedService.cs` - Verificar antes de criar run
5. `ZapJobs.Storage.PostgreSQL/PostgreSqlJobStorage.cs` - Implementar
6. `ZapJobs.Storage.PostgreSQL/MigrationRunner.cs` - Adicionar coluna
7. `ZapJobs.Storage.InMemory/InMemoryJobStorage.cs` - Implementar

## Critérios de Aceitação

1. [ ] JobDefinition tem propriedade PreventOverlapping
2. [ ] Jobs com overlap prevention não criam nova run se já existe uma ativa
3. [ ] Query otimizada para verificar runs ativas
4. [ ] Dashboard mostra indicador visual
5. [ ] Log indica quando job foi "skipped" por overlap
6. [ ] Fluent API funciona no registro de jobs
7. [ ] Testes para cenário de overlap
