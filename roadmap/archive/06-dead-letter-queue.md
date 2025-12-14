# Prompt: Implementar Dead Letter Queue

## Objetivo

Criar uma Dead Letter Queue (DLQ) para jobs que falharam permanentemente após esgotar todas as tentativas de retry.

## Contexto

Atualmente, quando um job falha permanentemente:
- Status vai para `Failed`
- Fica na tabela `runs` com os outros
- Não há visibilidade especial ou ações de recuperação

Com DLQ:
- Jobs com falha permanente vão para uma área separada
- Operadores podem revisar e re-enfileirar
- Métricas específicas de DLQ
- Possibilidade de notificações

## Requisitos

### Nova Entidade

```csharp
// ZapJobs.Core/Entities/DeadLetterEntry.cs
public class DeadLetterEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Original run ID that failed</summary>
    public Guid OriginalRunId { get; set; }

    /// <summary>Job type that failed</summary>
    public string JobTypeId { get; set; } = string.Empty;

    /// <summary>Queue the job was in</summary>
    public string Queue { get; set; } = "default";

    /// <summary>Original input</summary>
    public string? InputJson { get; set; }

    /// <summary>Last error message</summary>
    public string ErrorMessage { get; set; } = string.Empty;

    /// <summary>Last error type</summary>
    public string? ErrorType { get; set; }

    /// <summary>Last stack trace</summary>
    public string? StackTrace { get; set; }

    /// <summary>Number of attempts before going to DLQ</summary>
    public int AttemptCount { get; set; }

    /// <summary>When moved to DLQ</summary>
    public DateTime MovedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Status in DLQ</summary>
    public DeadLetterStatus Status { get; set; } = DeadLetterStatus.Pending;

    /// <summary>When requeued (if applicable)</summary>
    public DateTime? RequeuedAt { get; set; }

    /// <summary>New run ID after requeue</summary>
    public Guid? RequeuedRunId { get; set; }

    /// <summary>Notes from operator</summary>
    public string? Notes { get; set; }
}

public enum DeadLetterStatus
{
    Pending = 0,      // Waiting for review
    Requeued = 1,     // Sent back to processing
    Discarded = 2,    // Manually discarded
    Archived = 3      // Archived for records
}
```

### Atualizar IJobStorage

```csharp
public interface IJobStorage
{
    // ... existing ...

    // Dead Letter Queue
    Task MoveToDeadLetterAsync(JobRun failedRun, CancellationToken ct = default);
    Task<DeadLetterEntry?> GetDeadLetterEntryAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<DeadLetterEntry>> GetDeadLetterEntriesAsync(
        DeadLetterStatus? status = null,
        string? jobTypeId = null,
        int limit = 100,
        int offset = 0,
        CancellationToken ct = default);
    Task<int> GetDeadLetterCountAsync(CancellationToken ct = default);
    Task UpdateDeadLetterEntryAsync(DeadLetterEntry entry, CancellationToken ct = default);
}
```

### Nova Tabela PostgreSQL

```sql
CREATE TABLE IF NOT EXISTS {prefix}dead_letter (
    id UUID PRIMARY KEY,
    original_run_id UUID NOT NULL,
    job_type_id VARCHAR(255) NOT NULL,
    queue VARCHAR(100) NOT NULL DEFAULT 'default',
    input_json TEXT,
    error_message TEXT NOT NULL,
    error_type VARCHAR(500),
    stack_trace TEXT,
    attempt_count INTEGER NOT NULL,
    moved_at TIMESTAMP NOT NULL DEFAULT NOW(),
    status INTEGER NOT NULL DEFAULT 0,
    requeued_at TIMESTAMP,
    requeued_run_id UUID,
    notes TEXT
);

CREATE INDEX IF NOT EXISTS idx_{prefix}dead_letter_status
    ON {prefix}dead_letter(status);
CREATE INDEX IF NOT EXISTS idx_{prefix}dead_letter_job_type
    ON {prefix}dead_letter(job_type_id);
```

### Atualizar JobExecutor

```csharp
// Em HandleErrorAsync, quando falha permanentemente:
if (!shouldRetry)
{
    // ... existing failure handling ...

    // Move to dead letter queue
    await _storage.MoveToDeadLetterAsync(run, ct);

    _logger.LogWarning(
        "Job {JobTypeId} run {RunId} moved to dead letter queue after {Attempts} attempts",
        run.JobTypeId, run.Id, run.AttemptNumber);
}
```

### API para Gerenciamento

```csharp
// IJobTracker ou novo IDeadLetterManager
public interface IDeadLetterManager
{
    Task<IReadOnlyList<DeadLetterEntry>> GetEntriesAsync(
        DeadLetterStatus? status = null,
        string? jobTypeId = null,
        int limit = 100,
        CancellationToken ct = default);

    Task<int> GetCountAsync(CancellationToken ct = default);

    /// <summary>Requeue a dead letter entry for processing</summary>
    Task<Guid> RequeueAsync(Guid deadLetterId, string? newInput = null, CancellationToken ct = default);

    /// <summary>Discard entry (won't be processed)</summary>
    Task DiscardAsync(Guid deadLetterId, string? notes = null, CancellationToken ct = default);

    /// <summary>Archive entry (keep for records)</summary>
    Task ArchiveAsync(Guid deadLetterId, string? notes = null, CancellationToken ct = default);

    /// <summary>Bulk requeue all pending entries for a job type</summary>
    Task<int> RequeueAllAsync(string jobTypeId, CancellationToken ct = default);
}
```

### Dashboard

Adicionar página `/zapjobs/deadletter`:

1. Contagem de entradas por status
2. Tabela com entradas pendentes
3. Filtro por job type
4. Ações: Requeue, Discard, Archive
5. Modal com detalhes do erro

### Atualizar Stats

```csharp
public record JobStorageStats(
    // ... existing ...
    int DeadLetterCount  // Add this
);
```

## Exemplo de Uso

```csharp
// Via API
public class AdminController : ControllerBase
{
    private readonly IDeadLetterManager _dlq;

    [HttpGet("dead-letter")]
    public async Task<IActionResult> GetDeadLetterEntries()
    {
        var entries = await _dlq.GetEntriesAsync(
            status: DeadLetterStatus.Pending,
            limit: 50);
        return Ok(entries);
    }

    [HttpPost("dead-letter/{id}/requeue")]
    public async Task<IActionResult> Requeue(Guid id)
    {
        var newRunId = await _dlq.RequeueAsync(id);
        return Ok(new { newRunId });
    }

    [HttpPost("dead-letter/{id}/discard")]
    public async Task<IActionResult> Discard(Guid id, [FromBody] string? notes)
    {
        await _dlq.DiscardAsync(id, notes);
        return NoContent();
    }
}
```

## Arquivos a Criar/Modificar

1. `ZapJobs.Core/Entities/DeadLetterEntry.cs` - Nova entidade
2. `ZapJobs.Core/Abstractions/IDeadLetterManager.cs` - Nova interface
3. `ZapJobs.Core/Abstractions/IJobStorage.cs` - Adicionar métodos
4. `ZapJobs/DeadLetter/DeadLetterManager.cs` - Implementação
5. `ZapJobs/Execution/JobExecutor.cs` - Mover para DLQ
6. `ZapJobs/ServiceCollectionExtensions.cs` - Registrar serviço
7. `ZapJobs.Storage.PostgreSQL/PostgreSqlJobStorage.cs` - Implementar
8. `ZapJobs.Storage.PostgreSQL/MigrationRunner.cs` - Nova tabela
9. `ZapJobs.Storage.InMemory/InMemoryJobStorage.cs` - Implementar
10. `ZapJobs.AspNetCore/Dashboard/DashboardMiddleware.cs` - Nova página

## Critérios de Aceitação

1. [ ] Jobs com falha permanente vão para DLQ automaticamente
2. [ ] Dashboard mostra contador de DLQ no overview
3. [ ] Página de DLQ lista entradas com detalhes
4. [ ] Requeue cria novo run com mesmo input
5. [ ] Discard remove da fila de processamento
6. [ ] Stats inclui contagem de DLQ
7. [ ] Testes para fluxo completo
8. [ ] API REST para operações de DLQ
