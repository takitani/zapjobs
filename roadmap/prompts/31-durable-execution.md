# Durable Execution

**Prioridade:** P3
**Esforco:** AA (Muito Alto)
**Pilar:** CORE
**Gerado em:** 2025-12-19

## Contexto

Checkpoints (#30) exigem que o desenvolvedor explicitamente salve estado. Durable Execution vai alem: o runtime automaticamente persiste cada "step" do job, permitindo replay deterministico.

Temporal.io popularizou esse conceito: ao inves de escrever "salve checkpoint aqui", voce escreve codigo normal e o runtime garante que cada chamada a atividades externas e persistida. Se o worker crashar, o job continua exatamente de onde parou, reexecutando apenas as operacoes que nao foram completadas.

## Objetivo

Implementar execucao duravel onde cada "activity" (chamada externa) e automaticamente persistida e pode ser reexecutada em caso de falha.

## Requisitos

### Funcionais
- [ ] Definir "Activities" como operacoes duraveis
- [ ] Persistir resultado de cada activity automaticamente
- [ ] Replay de activities ja executadas (sem reexecutar)
- [ ] Suportar timeouts por activity
- [ ] Retry policy por activity
- [ ] Passar dados entre activities

### Nao-Funcionais
- [ ] Performance: Replay em memoria quando possivel
- [ ] Determinismo: Mesmo input = mesmo resultado (activities sao idempotentes)
- [ ] Observabilidade: Log de cada activity executada/replayed

## Arquivos Relevantes

```
src/ZapJobs.Core/
├── Durable/
│   └── IDurableContext.cs           # NOVO: Context com activities
│   └── IActivity.cs                 # NOVO: Interface de activity
│   └── ActivityResult.cs            # NOVO: Resultado persistido
│   └── DurableOptions.cs            # NOVO: Configuracao
├── Entities/
│   └── ActivityExecution.cs         # NOVO: Registro de execucao

src/ZapJobs/
├── Durable/
│   └── DurableJobExecutor.cs        # NOVO: Executor com replay
│   └── ActivityRegistry.cs          # NOVO: Registro de activities
│   └── ReplayEngine.cs              # NOVO: Motor de replay

src/ZapJobs.Storage.PostgreSQL/
└── Migrations/AddDurableExecution.sql  # NOVO: Schema
```

## Implementacao Sugerida

### Passo 1: Conceito de Activity

Activity e uma operacao que:
1. Tem efeitos colaterais (chamada de API, escrita em banco)
2. Pode falhar transientemente
3. Seu resultado deve ser persistido

```csharp
// IActivity.cs
public interface IActivity<TInput, TOutput>
{
    string ActivityId { get; }
    Task<TOutput> ExecuteAsync(TInput input, CancellationToken ct);
}

// Exemplo de Activity
public class SendEmailActivity : IActivity<EmailInput, EmailResult>
{
    public string ActivityId => "send-email";

    public async Task<EmailResult> ExecuteAsync(EmailInput input, CancellationToken ct)
    {
        // Chamada real ao servico de email
        var messageId = await _emailService.SendAsync(input.To, input.Subject, input.Body);
        return new EmailResult(messageId);
    }
}
```

### Passo 2: Modelo de Dados

```csharp
// ActivityExecution.cs
public class ActivityExecution
{
    public Guid Id { get; set; }
    public Guid RunId { get; set; }              // Job run
    public string ActivityId { get; set; } = string.Empty;
    public int SequenceNumber { get; set; }      // Ordem de execucao
    public string InputJson { get; set; } = string.Empty;
    public string? OutputJson { get; set; }
    public ActivityStatus Status { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
}

public enum ActivityStatus { Running, Completed, Failed }
```

### Passo 3: Durable Context

```csharp
// IDurableContext.cs
public interface IDurableContext
{
    // Executa activity com replay automatico
    Task<TOutput> ExecuteActivityAsync<TInput, TOutput>(
        string activityId,
        TInput input,
        ActivityOptions? options = null,
        CancellationToken ct = default);

    // Versao com Activity tipado
    Task<TOutput> ExecuteActivityAsync<TActivity, TInput, TOutput>(
        TInput input,
        ActivityOptions? options = null,
        CancellationToken ct = default)
        where TActivity : IActivity<TInput, TOutput>;

    // Sleep duravel (persiste tempo restante)
    Task SleepAsync(TimeSpan duration, CancellationToken ct = default);

    // Timer duravel
    Task<bool> WaitForTimerAsync(string timerId, DateTimeOffset fireAt, CancellationToken ct = default);
}

// ActivityOptions.cs
public class ActivityOptions
{
    public TimeSpan? Timeout { get; set; }
    public RetryPolicy? RetryPolicy { get; set; }
    public string? TaskQueue { get; set; }  // Queue especifica para activity
}
```

### Passo 4: Job Duravel

```csharp
// IDurableJob.cs - Nova interface para jobs duraveis
public interface IDurableJob<TInput>
{
    string JobTypeId { get; }
    Task ExecuteAsync(IDurableContext context, TInput input, CancellationToken ct);
}

// Exemplo de uso
public class OrderProcessingJob : IDurableJob<OrderInput>
{
    public string JobTypeId => "process-order";

    public async Task ExecuteAsync(IDurableContext context, OrderInput input, CancellationToken ct)
    {
        // Activity 1: Validar estoque
        var stock = await context.ExecuteActivityAsync<CheckStockActivity, StockInput, StockResult>(
            new StockInput(input.ProductId, input.Quantity));

        if (!stock.Available)
            throw new InsufficientStockException();

        // Activity 2: Reservar estoque (se crashar aqui, Activity 1 NAO reexecuta)
        var reservation = await context.ExecuteActivityAsync<ReserveStockActivity, ReserveInput, ReservationResult>(
            new ReserveInput(input.ProductId, input.Quantity));

        // Activity 3: Processar pagamento
        var payment = await context.ExecuteActivityAsync<ProcessPaymentActivity, PaymentInput, PaymentResult>(
            new PaymentInput(input.CustomerId, input.Amount));

        // Activity 4: Confirmar pedido
        await context.ExecuteActivityAsync<ConfirmOrderActivity, ConfirmInput, ConfirmResult>(
            new ConfirmInput(input.OrderId, reservation.Id, payment.TransactionId));
    }
}
```

### Passo 5: Replay Engine

```csharp
// ReplayEngine.cs
public class ReplayEngine
{
    private readonly IJobStorage _storage;
    private readonly IActivityRegistry _activities;

    public async Task<TOutput> ExecuteOrReplayAsync<TInput, TOutput>(
        Guid runId,
        string activityId,
        TInput input,
        int sequenceNumber,
        CancellationToken ct)
    {
        // Verificar se activity ja foi executada
        var existing = await _storage.GetActivityExecutionAsync(runId, sequenceNumber, ct);

        if (existing != null)
        {
            if (existing.Status == ActivityStatus.Completed)
            {
                // Replay: retornar resultado armazenado
                _logger.Debug("Replaying activity {ActivityId} seq {Seq}", activityId, sequenceNumber);
                return JsonSerializer.Deserialize<TOutput>(existing.OutputJson!);
            }

            if (existing.Status == ActivityStatus.Failed)
            {
                throw new ActivityFailedException(existing.ErrorMessage);
            }
        }

        // Nova execucao
        var execution = new ActivityExecution
        {
            Id = Guid.NewGuid(),
            RunId = runId,
            ActivityId = activityId,
            SequenceNumber = sequenceNumber,
            InputJson = JsonSerializer.Serialize(input),
            Status = ActivityStatus.Running,
            StartedAt = DateTimeOffset.UtcNow
        };

        await _storage.SaveActivityExecutionAsync(execution, ct);

        try
        {
            var activity = _activities.GetActivity<TInput, TOutput>(activityId);
            var result = await activity.ExecuteAsync(input, ct);

            execution.OutputJson = JsonSerializer.Serialize(result);
            execution.Status = ActivityStatus.Completed;
            execution.CompletedAt = DateTimeOffset.UtcNow;

            await _storage.UpdateActivityExecutionAsync(execution, ct);

            return result;
        }
        catch (Exception ex)
        {
            execution.Status = ActivityStatus.Failed;
            execution.ErrorMessage = ex.Message;
            execution.CompletedAt = DateTimeOffset.UtcNow;

            await _storage.UpdateActivityExecutionAsync(execution, ct);
            throw;
        }
    }
}
```

### Passo 6: Durable Sleep

```csharp
// Implementacao de sleep duravel
public async Task SleepAsync(TimeSpan duration, CancellationToken ct)
{
    var timerId = $"sleep-{_sequenceNumber++}";
    var fireAt = DateTimeOffset.UtcNow + duration;

    // Verificar se timer ja foi salvo (replay)
    var existing = await _storage.GetTimerAsync(_runId, timerId, ct);

    if (existing != null)
    {
        if (existing.FiredAt.HasValue)
        {
            // Timer ja disparou no replay
            return;
        }

        fireAt = existing.FireAt;  // Usar tempo original
    }
    else
    {
        // Salvar timer
        await _storage.SaveTimerAsync(new DurableTimer
        {
            RunId = _runId,
            TimerId = timerId,
            FireAt = fireAt
        }, ct);
    }

    // Aguardar tempo restante
    var remaining = fireAt - DateTimeOffset.UtcNow;
    if (remaining > TimeSpan.Zero)
    {
        await Task.Delay(remaining, ct);
    }

    // Marcar como disparado
    await _storage.MarkTimerFiredAsync(_runId, timerId, ct);
}
```

### Passo 7: Migration SQL

```sql
-- AddDurableExecution.sql
CREATE TABLE IF NOT EXISTS zapjobs.activity_executions (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    run_id UUID NOT NULL REFERENCES zapjobs.runs(id) ON DELETE CASCADE,
    activity_id VARCHAR(255) NOT NULL,
    sequence_number INT NOT NULL,
    input_json JSONB NOT NULL,
    output_json JSONB,
    status VARCHAR(50) NOT NULL DEFAULT 'Running',
    error_message TEXT,
    started_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    completed_at TIMESTAMPTZ,

    CONSTRAINT uq_activity_run_seq UNIQUE (run_id, sequence_number)
);

CREATE INDEX idx_activity_executions_run_id ON zapjobs.activity_executions(run_id);
CREATE INDEX idx_activity_executions_status ON zapjobs.activity_executions(status);

-- Timers duraveis
CREATE TABLE IF NOT EXISTS zapjobs.durable_timers (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    run_id UUID NOT NULL REFERENCES zapjobs.runs(id) ON DELETE CASCADE,
    timer_id VARCHAR(255) NOT NULL,
    fire_at TIMESTAMPTZ NOT NULL,
    fired_at TIMESTAMPTZ,

    CONSTRAINT uq_timer_run_id UNIQUE (run_id, timer_id)
);

CREATE INDEX idx_durable_timers_fire_at ON zapjobs.durable_timers(fire_at) WHERE fired_at IS NULL;
```

## Comparacao: Checkpoint vs Durable

| Aspecto | Checkpoint (#30) | Durable Execution (#31) |
|---------|------------------|-------------------------|
| Controle | Explicito (dev chama) | Automatico (runtime) |
| Granularidade | Por checkpoint | Por activity |
| Determinismo | Nao garantido | Garantido (replay) |
| Complexidade | Baixa | Alta |
| Performance | Melhor | Overhead por activity |
| Uso ideal | Batch processing | Workflows criticos |

## Criterios de Aceite

- [ ] Activities sao persistidas automaticamente
- [ ] Replay nao reexecuta activities completadas
- [ ] Durable sleep sobrevive a restarts
- [ ] Timeouts funcionam por activity
- [ ] Retry policy por activity
- [ ] Dashboard mostra historico de activities
- [ ] Jobs normais (IJob) continuam funcionando

## Dependencias

- Requer #30 (Checkpoints) como base conceitual
- Complementa #24 (Workflows/DAG)

## Checklist Pre-Commit

- [ ] IActivity interface criada
- [ ] IDurableContext interface criada
- [ ] IDurableJob interface criada
- [ ] ActivityExecution entidade criada
- [ ] ReplayEngine implementado
- [ ] DurableJobExecutor implementado
- [ ] ActivityRegistry implementado
- [ ] Durable timers implementados
- [ ] Migration SQL criada
- [ ] IJobStorage estendido
- [ ] Testes de replay
- [ ] CLAUDE.md atualizado
- [ ] Atualizar roadmap (marcar como concluido)
- [ ] Mover este prompt para `roadmap/archive/`

## Referencias

- [Temporal Durable Execution](https://docs.temporal.io/concepts/what-is-durable-execution)
- [Azure Durable Functions](https://learn.microsoft.com/en-us/azure/azure-functions/durable/durable-functions-overview)
- [Restate.dev](https://restate.dev/) - Durable execution for microservices
- [Infinitic](https://infinitic.io/) - Durable workflows for Kotlin
