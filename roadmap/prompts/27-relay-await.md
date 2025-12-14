# Relay/Await (RPC via Jobs)

**Prioridade:** P3
**Esforco:** A (Alto)
**Pilar:** CORE
**Gerado em:** 2025-12-13

## Contexto

Normalmente jobs sao fire-and-forget. Porem, as vezes queremos enfileirar um job e aguardar seu resultado, como uma chamada RPC. Oban Pro chama isso de "Relay".

Casos de uso:
- Offload processamento pesado mas aguardar resultado
- Coordenar entre servicos via job queue
- Timeout e retry automaticos em chamadas externas

## Objetivo

Implementar capacidade de enfileirar um job e aguardar seu resultado de forma sincrona (com timeout).

## Requisitos

### Funcionais
- [ ] API para enqueue + await resultado
- [ ] Timeout configuravel
- [ ] Retorno do output do job
- [ ] Propagacao de excecoes
- [ ] Funciona cross-node (em cluster)

### Nao-Funcionais
- [ ] Performance: Polling eficiente ou notification-based
- [ ] Timeout: Default 30s, configuravel
- [ ] Memoria: Nao vazar recursos em timeout

## Arquivos Relevantes

```
src/ZapJobs.Core/
├── Abstractions/
│   └── IJobScheduler.cs             # Adicionar EnqueueAndWaitAsync
│   └── IJobRelay.cs                 # NOVO: Interface especializada
├── Relay/
│   └── JobRelayResult.cs            # NOVO: Resultado do relay

src/ZapJobs/
├── Relay/
│   └── JobRelayService.cs           # NOVO: Implementacao
│   └── RelayCompletionTracker.cs    # NOVO: Rastreamento

src/ZapJobs.Storage.PostgreSQL/
└── PostgreSqlJobStorage.cs          # Adicionar LISTEN/NOTIFY
```

## Implementacao Sugerida

### Passo 1: Interface

```csharp
// IJobRelay.cs
public interface IJobRelay
{
    Task<TOutput> EnqueueAndWaitAsync<TInput, TOutput>(
        string jobTypeId,
        TInput input,
        TimeSpan? timeout = null,
        CancellationToken ct = default);

    Task<string?> EnqueueAndWaitAsync<TInput>(
        string jobTypeId,
        TInput input,
        TimeSpan? timeout = null,
        CancellationToken ct = default);
}

// JobRelayResult.cs
public class JobRelayResult<T>
{
    public bool Success { get; init; }
    public T? Value { get; init; }
    public string? Error { get; init; }
    public TimeSpan Duration { get; init; }
}
```

### Passo 2: Tracker de Completions

```csharp
// RelayCompletionTracker.cs
public class RelayCompletionTracker
{
    private readonly ConcurrentDictionary<Guid, TaskCompletionSource<JobRelayResult<string>>> _pending = new();

    public TaskCompletionSource<JobRelayResult<string>> Register(Guid runId)
    {
        var tcs = new TaskCompletionSource<JobRelayResult<string>>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[runId] = tcs;
        return tcs;
    }

    public void Complete(Guid runId, JobRelayResult<string> result)
    {
        if (_pending.TryRemove(runId, out var tcs))
        {
            tcs.TrySetResult(result);
        }
    }

    public void Cancel(Guid runId)
    {
        if (_pending.TryRemove(runId, out var tcs))
        {
            tcs.TrySetCanceled();
        }
    }

    public void Fail(Guid runId, Exception ex)
    {
        if (_pending.TryRemove(runId, out var tcs))
        {
            tcs.TrySetException(ex);
        }
    }
}
```

### Passo 3: Servico de Relay

```csharp
// JobRelayService.cs
public class JobRelayService : IJobRelay
{
    private readonly IJobScheduler _scheduler;
    private readonly IJobStorage _storage;
    private readonly RelayCompletionTracker _tracker;
    private readonly TimeSpan _defaultTimeout = TimeSpan.FromSeconds(30);

    public async Task<string?> EnqueueAndWaitAsync<TInput>(
        string jobTypeId,
        TInput input,
        TimeSpan? timeout = null,
        CancellationToken ct = default)
    {
        var actualTimeout = timeout ?? _defaultTimeout;

        // Enfileirar job
        var runId = await _scheduler.EnqueueAsync(jobTypeId, input);

        // Registrar para aguardar resultado
        var tcs = _tracker.Register(runId);

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(actualTimeout);

        try
        {
            var result = await tcs.Task.WaitAsync(cts.Token);

            if (!result.Success)
                throw new JobRelayException(result.Error ?? "Job failed");

            return result.Value;
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested)
        {
            _tracker.Cancel(runId);
            throw new TimeoutException($"Job {jobTypeId} did not complete within {actualTimeout}");
        }
    }

    public async Task<TOutput> EnqueueAndWaitAsync<TInput, TOutput>(
        string jobTypeId,
        TInput input,
        TimeSpan? timeout = null,
        CancellationToken ct = default)
    {
        var json = await EnqueueAndWaitAsync(jobTypeId, input, timeout, ct);
        return JsonSerializer.Deserialize<TOutput>(json!)!;
    }
}
```

### Passo 4: Notificacao de Completion

**Opcao A: Polling (Simples)**

```csharp
// JobRelayPollingService.cs (BackgroundService)
protected override async Task ExecuteAsync(CancellationToken ct)
{
    while (!ct.IsCancellationRequested)
    {
        var completed = await _storage.GetRecentlyCompletedAsync(since: _lastCheck);

        foreach (var run in completed)
        {
            _tracker.Complete(run.Id, new JobRelayResult<string>
            {
                Success = run.Status == JobStatus.Completed,
                Value = run.OutputJson,
                Error = run.Error,
                Duration = run.CompletedAt!.Value - run.StartedAt!.Value
            });
        }

        _lastCheck = DateTimeOffset.UtcNow;
        await Task.Delay(100, ct); // Poll a cada 100ms
    }
}
```

**Opcao B: PostgreSQL NOTIFY (Eficiente)**

```csharp
// PostgreSqlJobStorage.cs
public async Task NotifyJobCompletedAsync(Guid runId)
{
    await using var conn = await _dataSource.OpenConnectionAsync();
    await using var cmd = conn.CreateCommand();
    cmd.CommandText = $"NOTIFY zapjobs_completed, '{runId}'";
    await cmd.ExecuteNonQueryAsync();
}

// RelayNotificationService.cs
protected override async Task ExecuteAsync(CancellationToken ct)
{
    await using var conn = await _dataSource.OpenConnectionAsync();
    conn.Notification += (_, e) =>
    {
        if (Guid.TryParse(e.Payload, out var runId))
        {
            _ = CompleteRelayAsync(runId);
        }
    };

    await using var cmd = conn.CreateCommand();
    cmd.CommandText = "LISTEN zapjobs_completed";
    await cmd.ExecuteNonQueryAsync();

    while (!ct.IsCancellationRequested)
    {
        await conn.WaitAsync(ct);
    }
}
```

### Passo 5: Integracao no JobExecutor

```csharp
// JobExecutor.cs - apos completar job
if (run.Status == JobStatus.Completed || run.Status == JobStatus.Failed)
{
    await _storage.NotifyJobCompletedAsync(run.Id);
}
```

### Passo 6: Uso

```csharp
public class MyService
{
    private readonly IJobRelay _relay;

    public async Task<ProcessResult> ProcessDataAsync(DataInput input)
    {
        // Offload para worker, aguarda resultado
        var result = await _relay.EnqueueAndWaitAsync<DataInput, ProcessResult>(
            "heavy-processing-job",
            input,
            timeout: TimeSpan.FromMinutes(5));

        return result;
    }
}
```

## Criterios de Aceite

- [ ] EnqueueAndWaitAsync retorna output do job
- [ ] Timeout funciona corretamente
- [ ] Excecoes do job sao propagadas
- [ ] Funciona em cluster (NOTIFY ou polling)
- [ ] Recursos sao liberados em timeout
- [ ] Performance aceitavel (< 100ms overhead)
- [ ] Documentacao clara de uso

## Checklist Pre-Commit

- [ ] Interface IJobRelay criada
- [ ] RelayCompletionTracker implementado
- [ ] JobRelayService implementado
- [ ] Mecanismo de notificacao (NOTIFY ou polling)
- [ ] Integracao no JobExecutor
- [ ] Testes unitarios
- [ ] Testes de timeout
- [ ] CLAUDE.md atualizado
- [ ] Atualizar roadmap (marcar como concluido)
- [ ] Mover este prompt para `roadmap/archive/`

## Referencias

- [Oban Relay](https://oban.pro/features/relay)
- [PostgreSQL NOTIFY](https://www.postgresql.org/docs/current/sql-notify.html)
- [Npgsql Notifications](https://www.npgsql.org/doc/wait.html)
