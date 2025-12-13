# Prompt: Implementar Job Continuations (Chains)

## Objetivo

Implementar suporte a job continuations, permitindo encadear jobs que executam quando o job pai completa. Similar ao Hangfire `ContinueWith`.

## Contexto

Atualmente o ZapJobs não suporta dependências entre jobs. Esta feature permite:
- Executar Job B após Job A completar
- Chains de múltiplos jobs
- Continuações condicionais (só em sucesso, só em falha, sempre)

## Requisitos

### Novas Entidades

```csharp
// ZapJobs.Core/Entities/JobContinuation.cs
public class JobContinuation
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Parent run that triggers this continuation</summary>
    public Guid ParentRunId { get; set; }

    /// <summary>Job type to execute as continuation</summary>
    public string ContinuationJobTypeId { get; set; } = string.Empty;

    /// <summary>When to execute the continuation</summary>
    public ContinuationCondition Condition { get; set; } = ContinuationCondition.OnSuccess;

    /// <summary>Input for continuation job (JSON)</summary>
    public string? InputJson { get; set; }

    /// <summary>Whether to pass parent output as input</summary>
    public bool PassParentOutput { get; set; }

    /// <summary>Queue for continuation job</summary>
    public string? Queue { get; set; }

    /// <summary>Status of continuation</summary>
    public ContinuationStatus Status { get; set; } = ContinuationStatus.Pending;

    /// <summary>Run ID created for this continuation</summary>
    public Guid? ContinuationRunId { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public enum ContinuationCondition
{
    OnSuccess = 0,     // Only if parent succeeds
    OnFailure = 1,     // Only if parent fails
    Always = 2         // Regardless of parent status
}

public enum ContinuationStatus
{
    Pending = 0,       // Waiting for parent
    Triggered = 1,     // Continuation run created
    Skipped = 2        // Condition not met
}
```

### Atualizar IJobScheduler

```csharp
public interface IJobScheduler
{
    // ... existing methods ...

    /// <summary>
    /// Create a continuation that runs when parentRunId completes
    /// </summary>
    Task<Guid> ContinueWithAsync(
        Guid parentRunId,
        string continuationJobTypeId,
        object? input = null,
        ContinuationCondition condition = ContinuationCondition.OnSuccess,
        bool passParentOutput = false,
        string? queue = null,
        CancellationToken ct = default);

    /// <summary>
    /// Create a continuation using generic job type
    /// </summary>
    Task<Guid> ContinueWithAsync<TJob>(
        Guid parentRunId,
        object? input = null,
        ContinuationCondition condition = ContinuationCondition.OnSuccess,
        bool passParentOutput = false,
        string? queue = null,
        CancellationToken ct = default) where TJob : IJob;
}
```

### Atualizar IJobStorage

```csharp
public interface IJobStorage
{
    // ... existing methods ...

    // Continuations
    Task AddContinuationAsync(JobContinuation continuation, CancellationToken ct = default);
    Task<IReadOnlyList<JobContinuation>> GetContinuationsAsync(Guid parentRunId, CancellationToken ct = default);
    Task UpdateContinuationAsync(JobContinuation continuation, CancellationToken ct = default);
}
```

### Nova Tabela PostgreSQL

```sql
CREATE TABLE IF NOT EXISTS {prefix}continuations (
    id UUID PRIMARY KEY,
    parent_run_id UUID NOT NULL,
    continuation_job_type_id VARCHAR(255) NOT NULL,
    condition INTEGER NOT NULL DEFAULT 0,
    input_json TEXT,
    pass_parent_output BOOLEAN NOT NULL DEFAULT FALSE,
    queue VARCHAR(100),
    status INTEGER NOT NULL DEFAULT 0,
    continuation_run_id UUID,
    created_at TIMESTAMP NOT NULL DEFAULT NOW()
);

CREATE INDEX IF NOT EXISTS idx_{prefix}continuations_parent
    ON {prefix}continuations(parent_run_id)
    WHERE status = 0;
```

### Lógica de Execução

No `JobExecutor`, após completar um job:

```csharp
private async Task ProcessContinuationsAsync(JobRun parentRun, CancellationToken ct)
{
    var continuations = await _storage.GetContinuationsAsync(parentRun.Id, ct);

    foreach (var continuation in continuations.Where(c => c.Status == ContinuationStatus.Pending))
    {
        var shouldTrigger = continuation.Condition switch
        {
            ContinuationCondition.OnSuccess => parentRun.Status == JobRunStatus.Completed,
            ContinuationCondition.OnFailure => parentRun.Status == JobRunStatus.Failed,
            ContinuationCondition.Always => true,
            _ => false
        };

        if (shouldTrigger)
        {
            var input = continuation.PassParentOutput
                ? parentRun.OutputJson
                : continuation.InputJson;

            var run = new JobRun
            {
                JobTypeId = continuation.ContinuationJobTypeId,
                Status = JobRunStatus.Pending,
                TriggerType = JobTriggerType.Continuation,
                TriggeredBy = $"continuation:{parentRun.Id}",
                Queue = continuation.Queue ?? "default",
                InputJson = input
            };

            var runId = await _storage.EnqueueAsync(run, ct);
            continuation.ContinuationRunId = runId;
            continuation.Status = ContinuationStatus.Triggered;
        }
        else
        {
            continuation.Status = ContinuationStatus.Skipped;
        }

        await _storage.UpdateContinuationAsync(continuation, ct);
    }
}
```

## Exemplo de Uso

```csharp
public class OrderController : ControllerBase
{
    private readonly IJobScheduler _scheduler;

    [HttpPost("process-order")]
    public async Task<IActionResult> ProcessOrder(OrderRequest request)
    {
        // Job 1: Validate order
        var validateRunId = await _scheduler.EnqueueAsync<ValidateOrderJob>(
            new { OrderId = request.OrderId });

        // Job 2: Process payment (only if validation succeeds)
        var paymentRunId = await _scheduler.ContinueWithAsync<ProcessPaymentJob>(
            parentRunId: validateRunId,
            input: new { OrderId = request.OrderId },
            condition: ContinuationCondition.OnSuccess);

        // Job 3: Send confirmation (only if payment succeeds)
        await _scheduler.ContinueWithAsync<SendConfirmationJob>(
            parentRunId: paymentRunId,
            passParentOutput: true,  // Pass payment result
            condition: ContinuationCondition.OnSuccess);

        // Job 4: Notify support (only if payment fails)
        await _scheduler.ContinueWithAsync<NotifySupportJob>(
            parentRunId: paymentRunId,
            passParentOutput: true,
            condition: ContinuationCondition.OnFailure);

        return Accepted(new { validateRunId, paymentRunId });
    }
}
```

## Arquivos a Modificar

1. `ZapJobs.Core/Entities/JobContinuation.cs` - Nova entidade
2. `ZapJobs.Core/Entities/Enums.cs` - Adicionar enums
3. `ZapJobs.Core/Abstractions/IJobScheduler.cs` - Adicionar métodos
4. `ZapJobs.Core/Abstractions/IJobStorage.cs` - Adicionar métodos
5. `ZapJobs/Scheduling/JobSchedulerService.cs` - Implementar
6. `ZapJobs/Execution/JobExecutor.cs` - Processar continuations
7. `ZapJobs.Storage.PostgreSQL/PostgreSqlJobStorage.cs` - Implementar storage
8. `ZapJobs.Storage.PostgreSQL/MigrationRunner.cs` - Adicionar tabela
9. `ZapJobs.Storage.InMemory/InMemoryJobStorage.cs` - Implementar storage

## Critérios de Aceitação

1. [ ] Continuations são criadas e armazenadas
2. [ ] OnSuccess só dispara quando parent completa com sucesso
3. [ ] OnFailure só dispara quando parent falha
4. [ ] Always dispara independente do resultado
5. [ ] PassParentOutput passa o output do parent como input
6. [ ] Chains de 3+ jobs funcionam corretamente
7. [ ] Dashboard mostra continuations pendentes
8. [ ] Testes unitários para lógica de condição
9. [ ] Testes de integração para chain completo
