# Checkpoints / Resume (Durable State)

**Prioridade:** P3
**Esforco:** A (Alto)
**Pilar:** CORE
**Gerado em:** 2025-12-19

## Contexto

Jobs longos (ETL, processamento de arquivos grandes, sync de dados) podem falhar no meio da execucao. Sem checkpoints, o job precisa recomecar do zero, desperdicando tempo e recursos.

Temporal.io resolve isso com "durable execution" - cada step e persistido automaticamente. Se o worker crashar, o job retoma exatamente de onde parou.

## Objetivo

Implementar sistema de checkpoints que permite jobs salvarem progresso e retomarem execucao apos falhas ou restarts.

## Requisitos

### Funcionais
- [ ] API para salvar checkpoint dentro do job
- [ ] Restauracao automatica do ultimo checkpoint ao retomar
- [ ] Multiplos checkpoints por job (historico)
- [ ] Checkpoint data e typed (generico)
- [ ] TTL configuravel para limpeza de checkpoints antigos
- [ ] API para listar checkpoints de um job

### Nao-Funcionais
- [ ] Performance: Checkpoint nao pode bloquear execucao (< 50ms)
- [ ] Storage: Suportar checkpoints grandes (ate 1MB de dados)
- [ ] Resiliencia: Checkpoint atomico (tudo ou nada)

## Arquivos Relevantes

```
src/ZapJobs.Core/
├── Checkpoints/
│   └── ICheckpointService.cs        # NOVO: Interface de servico
│   └── Checkpoint.cs                # NOVO: Entidade
│   └── CheckpointOptions.cs         # NOVO: Configuracao
├── Context/
│   └── JobExecutionContext.cs       # MODIFICAR: Adicionar checkpoint API

src/ZapJobs/
├── Checkpoints/
│   └── CheckpointService.cs         # NOVO: Implementacao
│   └── CheckpointCleanupService.cs  # NOVO: Limpeza periodica

src/ZapJobs.Storage.PostgreSQL/
└── Migrations/AddCheckpoints.sql    # NOVO: Schema de checkpoints
```

## Implementacao Sugerida

### Passo 1: Modelo de Dados

```csharp
// Checkpoint.cs
public class Checkpoint
{
    public Guid Id { get; set; }
    public Guid RunId { get; set; }           // Job run associado
    public string Key { get; set; } = string.Empty;  // Identificador do checkpoint
    public string DataJson { get; set; } = string.Empty;  // Estado serializado
    public int SequenceNumber { get; set; }   // Ordem dos checkpoints
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? ExpiresAt { get; set; }
}

// CheckpointOptions.cs
public class CheckpointOptions
{
    public TimeSpan DefaultTtl { get; set; } = TimeSpan.FromDays(7);
    public int MaxCheckpointsPerRun { get; set; } = 100;
    public int MaxDataSizeBytes { get; set; } = 1_048_576; // 1MB
}
```

### Passo 2: Interface de Servico

```csharp
// ICheckpointService.cs
public interface ICheckpointService
{
    Task SaveAsync<T>(Guid runId, string key, T data, CancellationToken ct = default);
    Task<T?> LoadAsync<T>(Guid runId, string key, CancellationToken ct = default);
    Task<T?> LoadLatestAsync<T>(Guid runId, CancellationToken ct = default);
    Task<IReadOnlyList<Checkpoint>> GetAllAsync(Guid runId, CancellationToken ct = default);
    Task DeleteAsync(Guid runId, string? key = null, CancellationToken ct = default);
}
```

### Passo 3: API no Context do Job

```csharp
// JobExecutionContext.cs - adicionar metodos
public class JobExecutionContext<TInput>
{
    private readonly ICheckpointService _checkpointService;

    // Salvar checkpoint
    public async Task CheckpointAsync<TState>(string key, TState state)
    {
        await _checkpointService.SaveAsync(RunId, key, state);
        await Logger.DebugAsync($"Checkpoint saved: {key}");
    }

    // Carregar checkpoint (retorna null se nao existir)
    public async Task<TState?> GetCheckpointAsync<TState>(string key)
    {
        return await _checkpointService.LoadAsync<TState>(RunId, key);
    }

    // Carregar ultimo checkpoint (qualquer key)
    public async Task<TState?> GetLatestCheckpointAsync<TState>()
    {
        return await _checkpointService.LoadLatestAsync<TState>(RunId);
    }

    // Verificar se esta retomando de checkpoint
    public bool IsResuming => AttemptNumber > 1;
}
```

### Passo 4: Uso no Job

```csharp
public class ImportLargeFileJob : IJob<ImportInput>
{
    public async Task ExecuteAsync(JobExecutionContext<ImportInput> context, CancellationToken ct)
    {
        var input = context.Input;

        // Tentar carregar checkpoint se estiver retomando
        var state = await context.GetCheckpointAsync<ImportState>("progress");
        int startLine = state?.ProcessedLines ?? 0;

        await context.Logger.InfoAsync($"Starting from line {startLine}");

        var lines = await File.ReadLinesAsync(input.FilePath);
        int currentLine = 0;

        foreach (var line in lines.Skip(startLine))
        {
            ct.ThrowIfCancellationRequested();

            await ProcessLineAsync(line);
            currentLine++;

            // Checkpoint a cada 1000 linhas
            if (currentLine % 1000 == 0)
            {
                await context.CheckpointAsync("progress", new ImportState
                {
                    ProcessedLines = startLine + currentLine,
                    LastProcessedAt = DateTimeOffset.UtcNow
                });

                context.SetProgress(startLine + currentLine, lines.Count, "Processing...");
            }
        }

        await context.Logger.InfoAsync($"Completed: {startLine + currentLine} lines processed");
    }
}

record ImportState(int ProcessedLines, DateTimeOffset LastProcessedAt);
```

### Passo 5: Storage Implementation

```csharp
// CheckpointService.cs
public class CheckpointService : ICheckpointService
{
    private readonly IJobStorage _storage;
    private readonly CheckpointOptions _options;

    public async Task SaveAsync<T>(Guid runId, string key, T data, CancellationToken ct)
    {
        var json = JsonSerializer.Serialize(data);

        if (json.Length > _options.MaxDataSizeBytes)
            throw new CheckpointTooLargeException($"Checkpoint exceeds {_options.MaxDataSizeBytes} bytes");

        var checkpoint = new Checkpoint
        {
            Id = Guid.NewGuid(),
            RunId = runId,
            Key = key,
            DataJson = json,
            SequenceNumber = await GetNextSequenceAsync(runId, ct),
            CreatedAt = DateTimeOffset.UtcNow,
            ExpiresAt = DateTimeOffset.UtcNow + _options.DefaultTtl
        };

        await _storage.SaveCheckpointAsync(checkpoint, ct);
    }

    public async Task<T?> LoadAsync<T>(Guid runId, string key, CancellationToken ct)
    {
        var checkpoint = await _storage.GetCheckpointAsync(runId, key, ct);
        if (checkpoint == null) return default;

        return JsonSerializer.Deserialize<T>(checkpoint.DataJson);
    }
}
```

### Passo 6: Migration SQL

```sql
-- AddCheckpoints.sql
CREATE TABLE IF NOT EXISTS zapjobs.checkpoints (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    run_id UUID NOT NULL REFERENCES zapjobs.runs(id) ON DELETE CASCADE,
    key VARCHAR(255) NOT NULL,
    data_json JSONB NOT NULL,
    sequence_number INT NOT NULL DEFAULT 0,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    expires_at TIMESTAMPTZ,

    CONSTRAINT uq_checkpoint_run_key UNIQUE (run_id, key)
);

CREATE INDEX idx_checkpoints_run_id ON zapjobs.checkpoints(run_id);
CREATE INDEX idx_checkpoints_expires_at ON zapjobs.checkpoints(expires_at) WHERE expires_at IS NOT NULL;

-- Cleanup de checkpoints expirados (rodar periodicamente)
-- DELETE FROM zapjobs.checkpoints WHERE expires_at < NOW();
```

## Casos de Uso

### 1. ETL de Arquivo Grande
```csharp
// Checkpoint por batch processado
await context.CheckpointAsync("batch", new { LastBatchId = batchId, RecordsProcessed = count });
```

### 2. Sync de API Externa com Paginacao
```csharp
// Checkpoint por pagina
await context.CheckpointAsync("page", new { LastPage = page, LastCursor = cursor });
```

### 3. Processamento de Queue com Offset
```csharp
// Checkpoint por mensagem
await context.CheckpointAsync("offset", new { Offset = offset, Partition = partition });
```

## Criterios de Aceite

- [ ] Jobs podem salvar checkpoints durante execucao
- [ ] Checkpoints sao restaurados automaticamente em retries
- [ ] Context expoe API simples (CheckpointAsync/GetCheckpointAsync)
- [ ] Checkpoints expiram automaticamente (TTL)
- [ ] Dashboard mostra checkpoints de um job
- [ ] Storage suporta ate 1MB por checkpoint
- [ ] Cleanup service remove checkpoints expirados

## Checklist Pre-Commit

- [ ] Entidade Checkpoint criada
- [ ] ICheckpointService interface criada
- [ ] CheckpointService implementado
- [ ] JobExecutionContext estendido
- [ ] IJobStorage estendido com metodos de checkpoint
- [ ] PostgreSqlJobStorage implementado
- [ ] InMemoryJobStorage implementado
- [ ] Migration SQL criada
- [ ] CheckpointCleanupService implementado
- [ ] Testes unitarios
- [ ] CLAUDE.md atualizado
- [ ] Atualizar roadmap (marcar como concluido)
- [ ] Mover este prompt para `roadmap/archive/`

## Referencias

- [Temporal Durable Execution](https://docs.temporal.io/concepts/what-is-durable-execution)
- [Azure Durable Functions Checkpointing](https://learn.microsoft.com/en-us/azure/azure-functions/durable/durable-functions-checkpointing-and-replay)
- [Kafka Consumer Checkpoints](https://docs.confluent.io/platform/current/clients/consumer.html#offset-management)
