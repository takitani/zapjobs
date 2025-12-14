# Prompt: Implementar Batch Jobs

## Objetivo

Implementar suporte a batch jobs - grupos de jobs criados atomicamente que são tratados como uma única unidade. Similar ao Hangfire Pro Batches.

## Contexto

Batch jobs permitem:
- Criar múltiplos jobs em uma transação
- Monitorar progresso do batch como um todo
- Executar ação quando todo o batch completa
- Nested batches (batches dentro de batches)

## Requisitos

### Novas Entidades

```csharp
// ZapJobs.Core/Entities/JobBatch.cs
public class JobBatch
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Name for identification</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Parent batch ID (for nested batches)</summary>
    public Guid? ParentBatchId { get; set; }

    /// <summary>Current status of the batch</summary>
    public BatchStatus Status { get; set; } = BatchStatus.Created;

    /// <summary>Total jobs in this batch</summary>
    public int TotalJobs { get; set; }

    /// <summary>Jobs completed successfully</summary>
    public int CompletedJobs { get; set; }

    /// <summary>Jobs that failed</summary>
    public int FailedJobs { get; set; }

    /// <summary>Who created this batch</summary>
    public string? CreatedBy { get; set; }

    /// <summary>When the batch was created</summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>When all jobs finished</summary>
    public DateTime? CompletedAt { get; set; }

    /// <summary>Expiration time for completed batches</summary>
    public DateTime? ExpiresAt { get; set; }
}

public enum BatchStatus
{
    Created = 0,
    Started = 1,
    Completed = 2,
    Failed = 3,    // At least one job failed
    Cancelled = 4
}

// Link between batch and runs
public class BatchJob
{
    public Guid BatchId { get; set; }
    public Guid RunId { get; set; }
    public int Order { get; set; }  // Order within batch
}
```

### Atualizar JobRun

```csharp
public class JobRun
{
    // ... existing ...

    /// <summary>Batch this run belongs to (if any)</summary>
    public Guid? BatchId { get; set; }
}
```

### Batch Builder

```csharp
// ZapJobs.Core/Batches/IBatchBuilder.cs
public interface IBatchBuilder
{
    /// <summary>Add a job to the batch</summary>
    IBatchBuilder Enqueue(string jobTypeId, object? input = null, string? queue = null);

    /// <summary>Add a job to the batch using generic type</summary>
    IBatchBuilder Enqueue<TJob>(object? input = null, string? queue = null) where TJob : IJob;

    /// <summary>Add a nested batch</summary>
    IBatchBuilder AddBatch(string name, Action<IBatchBuilder> configure);

    /// <summary>Set continuation to run when all jobs complete successfully</summary>
    IBatchBuilder OnSuccess(string jobTypeId, object? input = null);

    /// <summary>Set continuation to run when any job fails</summary>
    IBatchBuilder OnFailure(string jobTypeId, object? input = null);

    /// <summary>Set continuation to run when batch finishes (success or failure)</summary>
    IBatchBuilder OnComplete(string jobTypeId, object? input = null);
}

// ZapJobs/Batches/BatchBuilder.cs
internal class BatchBuilder : IBatchBuilder
{
    private readonly List<BatchJobInfo> _jobs = new();
    private readonly List<(string Name, Action<IBatchBuilder> Configure)> _nestedBatches = new();
    private BatchContinuation? _onSuccess;
    private BatchContinuation? _onFailure;
    private BatchContinuation? _onComplete;

    public IBatchBuilder Enqueue(string jobTypeId, object? input = null, string? queue = null)
    {
        _jobs.Add(new BatchJobInfo(jobTypeId, input, queue));
        return this;
    }

    public IBatchBuilder Enqueue<TJob>(object? input = null, string? queue = null) where TJob : IJob
    {
        var job = Activator.CreateInstance<TJob>();
        return Enqueue(job.JobTypeId, input, queue);
    }

    public IBatchBuilder AddBatch(string name, Action<IBatchBuilder> configure)
    {
        _nestedBatches.Add((name, configure));
        return this;
    }

    public IBatchBuilder OnSuccess(string jobTypeId, object? input = null)
    {
        _onSuccess = new BatchContinuation(jobTypeId, input);
        return this;
    }

    // ... similar for OnFailure and OnComplete
}
```

### Batch Service

```csharp
// ZapJobs.Core/Abstractions/IBatchService.cs
public interface IBatchService
{
    /// <summary>Create a new batch with jobs</summary>
    Task<Guid> CreateBatchAsync(
        string name,
        Action<IBatchBuilder> configure,
        CancellationToken ct = default);

    /// <summary>Get batch by ID</summary>
    Task<JobBatch?> GetBatchAsync(Guid batchId, CancellationToken ct = default);

    /// <summary>Get jobs in a batch</summary>
    Task<IReadOnlyList<JobRun>> GetBatchJobsAsync(Guid batchId, CancellationToken ct = default);

    /// <summary>Add more jobs to an existing batch</summary>
    Task AddJobsToBatchAsync(
        Guid batchId,
        Action<IBatchBuilder> configure,
        CancellationToken ct = default);

    /// <summary>Cancel all pending jobs in batch</summary>
    Task CancelBatchAsync(Guid batchId, CancellationToken ct = default);

    /// <summary>Continue a batch with more jobs</summary>
    Task<Guid> ContinueBatchWithAsync(
        Guid batchId,
        string name,
        Action<IBatchBuilder> configure,
        CancellationToken ct = default);
}
```

### Implementação

```csharp
// ZapJobs/Batches/BatchService.cs
public class BatchService : IBatchService
{
    public async Task<Guid> CreateBatchAsync(
        string name,
        Action<IBatchBuilder> configure,
        CancellationToken ct = default)
    {
        var builder = new BatchBuilder();
        configure(builder);

        var batch = new JobBatch
        {
            Name = name,
            TotalJobs = builder.Jobs.Count
        };

        // Use transaction to create batch and all jobs atomically
        await using var conn = await _storage.BeginTransactionAsync(ct);

        try
        {
            await _storage.CreateBatchAsync(batch, ct);

            var order = 0;
            foreach (var jobInfo in builder.Jobs)
            {
                var run = new JobRun
                {
                    JobTypeId = jobInfo.JobTypeId,
                    Status = JobRunStatus.Pending,
                    TriggerType = JobTriggerType.Batch,
                    Queue = jobInfo.Queue ?? "default",
                    InputJson = jobInfo.Input != null
                        ? JsonSerializer.Serialize(jobInfo.Input)
                        : null,
                    BatchId = batch.Id
                };

                await _storage.EnqueueAsync(run, ct);
                await _storage.AddBatchJobAsync(new BatchJob
                {
                    BatchId = batch.Id,
                    RunId = run.Id,
                    Order = order++
                }, ct);
            }

            // Handle nested batches recursively
            foreach (var (nestedName, nestedConfigure) in builder.NestedBatches)
            {
                await CreateNestedBatchAsync(batch.Id, nestedName, nestedConfigure, ct);
            }

            // Store continuations
            if (builder.OnSuccess != null)
            {
                await _storage.AddBatchContinuationAsync(batch.Id, "success", builder.OnSuccess, ct);
            }

            await conn.CommitAsync(ct);
            return batch.Id;
        }
        catch
        {
            await conn.RollbackAsync(ct);
            throw;
        }
    }
}
```

### Processar Batch Completion

```csharp
// No JobExecutor, após completar job que pertence a batch
private async Task CheckBatchCompletionAsync(JobRun run, CancellationToken ct)
{
    if (!run.BatchId.HasValue) return;

    var batch = await _storage.GetBatchAsync(run.BatchId.Value, ct);
    if (batch == null) return;

    // Update batch counters
    if (run.Status == JobRunStatus.Completed)
        batch.CompletedJobs++;
    else if (run.Status == JobRunStatus.Failed)
        batch.FailedJobs++;

    // Check if all jobs are done
    var totalDone = batch.CompletedJobs + batch.FailedJobs;
    if (totalDone >= batch.TotalJobs)
    {
        batch.Status = batch.FailedJobs > 0 ? BatchStatus.Failed : BatchStatus.Completed;
        batch.CompletedAt = DateTime.UtcNow;
        batch.ExpiresAt = DateTime.UtcNow.AddDays(7);

        // Execute continuations
        await ExecuteBatchContinuationsAsync(batch, ct);
    }

    await _storage.UpdateBatchAsync(batch, ct);
}
```

### Nova Tabela PostgreSQL

```sql
CREATE TABLE IF NOT EXISTS {prefix}batches (
    id UUID PRIMARY KEY,
    name VARCHAR(255) NOT NULL,
    parent_batch_id UUID REFERENCES {prefix}batches(id),
    status INTEGER NOT NULL DEFAULT 0,
    total_jobs INTEGER NOT NULL DEFAULT 0,
    completed_jobs INTEGER NOT NULL DEFAULT 0,
    failed_jobs INTEGER NOT NULL DEFAULT 0,
    created_by VARCHAR(255),
    created_at TIMESTAMP NOT NULL DEFAULT NOW(),
    completed_at TIMESTAMP,
    expires_at TIMESTAMP
);

CREATE TABLE IF NOT EXISTS {prefix}batch_jobs (
    batch_id UUID NOT NULL REFERENCES {prefix}batches(id),
    run_id UUID NOT NULL REFERENCES {prefix}runs(id),
    order_num INTEGER NOT NULL,
    PRIMARY KEY (batch_id, run_id)
);

CREATE TABLE IF NOT EXISTS {prefix}batch_continuations (
    id UUID PRIMARY KEY,
    batch_id UUID NOT NULL REFERENCES {prefix}batches(id),
    trigger_type VARCHAR(50) NOT NULL, -- 'success', 'failure', 'complete'
    job_type_id VARCHAR(255) NOT NULL,
    input_json TEXT,
    status INTEGER NOT NULL DEFAULT 0
);

-- Add batch_id to runs
ALTER TABLE {prefix}runs ADD COLUMN IF NOT EXISTS batch_id UUID;
CREATE INDEX IF NOT EXISTS idx_{prefix}runs_batch ON {prefix}runs(batch_id) WHERE batch_id IS NOT NULL;
```

## Exemplo de Uso

```csharp
// Create a batch for order processing
var batchId = await batchService.CreateBatchAsync("process-order-123", batch =>
{
    // Add multiple jobs
    batch.Enqueue<ValidateOrderJob>(new { OrderId = 123 });
    batch.Enqueue<CheckInventoryJob>(new { OrderId = 123 });
    batch.Enqueue<CalculateShippingJob>(new { OrderId = 123 });

    // Add nested batch for payment processing
    batch.AddBatch("payment-processing", nested =>
    {
        nested.Enqueue<ProcessPaymentJob>(new { OrderId = 123 });
        nested.Enqueue<SendReceiptJob>(new { OrderId = 123 });
    });

    // Continuations
    batch.OnSuccess("complete-order", new { OrderId = 123 });
    batch.OnFailure("notify-failure", new { OrderId = 123 });
});

// Check batch status
var batch = await batchService.GetBatchAsync(batchId);
Console.WriteLine($"Batch {batch.Name}: {batch.CompletedJobs}/{batch.TotalJobs} completed");

// Add more jobs to running batch
await batchService.AddJobsToBatchAsync(batchId, batch =>
{
    batch.Enqueue<AdditionalProcessingJob>(new { OrderId = 123 });
});
```

## Arquivos a Criar/Modificar

1. `ZapJobs.Core/Entities/JobBatch.cs`
2. `ZapJobs.Core/Entities/BatchJob.cs`
3. `ZapJobs.Core/Abstractions/IBatchService.cs`
4. `ZapJobs.Core/Batches/IBatchBuilder.cs`
5. `ZapJobs/Batches/BatchService.cs`
6. `ZapJobs/Batches/BatchBuilder.cs`
7. `ZapJobs.Core/Entities/JobRun.cs` - Adicionar BatchId
8. `ZapJobs/Execution/JobExecutor.cs` - Check batch completion
9. `ZapJobs.Storage.PostgreSQL/PostgreSqlJobStorage.cs`
10. `ZapJobs.Storage.PostgreSQL/MigrationRunner.cs`
11. `ZapJobs.AspNetCore/Dashboard/DashboardMiddleware.cs` - Página de batches

## Critérios de Aceitação

1. [ ] Batches são criados atomicamente (transação)
2. [ ] Todos os jobs no batch são linkados
3. [ ] Contadores de batch atualizam corretamente
4. [ ] Nested batches funcionam
5. [ ] Continuations disparam corretamente
6. [ ] Dashboard mostra batches e seu progresso
7. [ ] Add jobs to existing batch funciona
8. [ ] Cancel batch cancela todos os jobs pendentes
9. [ ] Testes para fluxos completos
