# Event History / Replay

**Prioridade:** P3
**Esforco:** A (Alto)
**Pilar:** TRUST
**Gerado em:** 2025-12-19

## Contexto

Temporal.io guarda um log imutavel de todos os eventos de um workflow (Event History). Isso permite:
1. **Debugging**: Ver exatamente o que aconteceu
2. **Auditoria**: Compliance e rastreabilidade
3. **Replay**: Reexecutar workflow com mesmo estado
4. **Time-travel**: Ver estado do workflow em qualquer momento

ZapJobs ja tem logs (`zapjobs.logs`), mas sao textuais. Event History e estruturado e semantico.

## Objetivo

Implementar sistema de Event History que registra todos os eventos de um job/workflow de forma estruturada, permitindo visualizacao, auditoria e replay.

## Requisitos

### Funcionais
- [ ] Registrar eventos estruturados (nao apenas texto)
- [ ] Tipos de evento: Started, Completed, Failed, ActivityStarted, ActivityCompleted, etc.
- [ ] Payload de evento (dados relevantes)
- [ ] Timeline visual no dashboard
- [ ] Export de historico (JSON)
- [ ] Replay de job baseado em historico

### Nao-Funcionais
- [ ] Performance: Eventos sao append-only (fast write)
- [ ] Storage: Compactacao de eventos antigos
- [ ] Query: Filtrar por tipo de evento, data, job

## Arquivos Relevantes

```
src/ZapJobs.Core/
├── Events/
│   └── IEventStore.cs              # NOVO: Interface de armazenamento
│   └── JobEvent.cs                 # NOVO: Evento base
│   └── EventTypes.cs               # NOVO: Tipos de evento
│   └── IEventPublisher.cs          # NOVO: Publicador de eventos

src/ZapJobs/
├── Events/
│   └── EventStore.cs               # NOVO: Implementacao
│   └── EventPublisher.cs           # NOVO: Publicador
│   └── EventReplayer.cs            # NOVO: Motor de replay

src/ZapJobs.Storage.PostgreSQL/
└── Migrations/AddEventHistory.sql  # NOVO: Schema
```

## Implementacao Sugerida

### Passo 1: Modelo de Eventos

```csharp
// JobEvent.cs - Evento base
public class JobEvent
{
    public Guid Id { get; set; }
    public Guid RunId { get; set; }
    public Guid? WorkflowId { get; set; }      // Opcional: se parte de workflow
    public string EventType { get; set; } = string.Empty;
    public long SequenceNumber { get; set; }   // Ordem global
    public DateTimeOffset Timestamp { get; set; }
    public string PayloadJson { get; set; } = "{}";
    public string? CorrelationId { get; set; }  // Para rastrear eventos relacionados
}

// EventTypes.cs - Tipos de evento estruturados
public static class EventTypes
{
    // Job lifecycle
    public const string JobScheduled = "job.scheduled";
    public const string JobStarted = "job.started";
    public const string JobCompleted = "job.completed";
    public const string JobFailed = "job.failed";
    public const string JobRetrying = "job.retrying";
    public const string JobCancelled = "job.cancelled";

    // Activity lifecycle (para Durable Execution)
    public const string ActivityStarted = "activity.started";
    public const string ActivityCompleted = "activity.completed";
    public const string ActivityFailed = "activity.failed";
    public const string ActivityRetrying = "activity.retrying";

    // Workflow lifecycle
    public const string WorkflowStarted = "workflow.started";
    public const string WorkflowCompleted = "workflow.completed";
    public const string WorkflowFailed = "workflow.failed";
    public const string StepStarted = "workflow.step.started";
    public const string StepCompleted = "workflow.step.completed";

    // Saga lifecycle
    public const string SagaStarted = "saga.started";
    public const string SagaCompleted = "saga.completed";
    public const string SagaCompensating = "saga.compensating";
    public const string SagaCompensated = "saga.compensated";
    public const string CompensationStarted = "saga.compensation.started";
    public const string CompensationCompleted = "saga.compensation.completed";

    // State changes
    public const string CheckpointSaved = "checkpoint.saved";
    public const string ProgressUpdated = "progress.updated";
    public const string TimerScheduled = "timer.scheduled";
    public const string TimerFired = "timer.fired";

    // Custom (user-defined)
    public const string Custom = "custom";
}
```

### Passo 2: Payloads Tipados

```csharp
// Payloads para cada tipo de evento
public record JobScheduledPayload(
    string JobTypeId,
    string Queue,
    DateTimeOffset ScheduledFor,
    string? InputJson);

public record JobStartedPayload(
    string WorkerId,
    int AttemptNumber);

public record JobCompletedPayload(
    TimeSpan Duration,
    string? OutputJson,
    int? SucceededCount,
    int? FailedCount);

public record JobFailedPayload(
    string ErrorMessage,
    string? StackTrace,
    int AttemptNumber,
    bool WillRetry,
    DateTimeOffset? NextRetryAt);

public record ActivityStartedPayload(
    string ActivityId,
    int SequenceNumber,
    string InputJson);

public record ActivityCompletedPayload(
    string ActivityId,
    int SequenceNumber,
    TimeSpan Duration,
    string OutputJson);

public record ProgressUpdatedPayload(
    int Current,
    int Total,
    string? Message);
```

### Passo 3: Interface de Event Store

```csharp
// IEventStore.cs
public interface IEventStore
{
    // Append evento
    Task AppendAsync(JobEvent @event, CancellationToken ct = default);
    Task AppendAsync(IEnumerable<JobEvent> events, CancellationToken ct = default);

    // Query eventos
    Task<IReadOnlyList<JobEvent>> GetEventsAsync(
        Guid runId,
        string? eventType = null,
        long? afterSequence = null,
        int? limit = null,
        CancellationToken ct = default);

    // Timeline completa de um job
    Task<IReadOnlyList<JobEvent>> GetTimelineAsync(Guid runId, CancellationToken ct = default);

    // Eventos de um workflow (todos os jobs)
    Task<IReadOnlyList<JobEvent>> GetWorkflowEventsAsync(Guid workflowId, CancellationToken ct = default);

    // Ultimo evento de um tipo
    Task<JobEvent?> GetLastEventAsync(Guid runId, string eventType, CancellationToken ct = default);

    // Contagem por tipo
    Task<Dictionary<string, int>> GetEventCountsAsync(Guid runId, CancellationToken ct = default);
}
```

### Passo 4: Event Publisher

```csharp
// IEventPublisher.cs
public interface IEventPublisher
{
    Task PublishAsync<TPayload>(Guid runId, string eventType, TPayload payload, CancellationToken ct = default);
    Task PublishAsync(Guid runId, string eventType, CancellationToken ct = default);
}

// EventPublisher.cs
public class EventPublisher : IEventPublisher
{
    private readonly IEventStore _store;
    private readonly IEventBroadcaster? _broadcaster;  // Opcional: para #26 Event Broadcasting

    public async Task PublishAsync<TPayload>(Guid runId, string eventType, TPayload payload, CancellationToken ct)
    {
        var @event = new JobEvent
        {
            Id = Guid.NewGuid(),
            RunId = runId,
            EventType = eventType,
            Timestamp = DateTimeOffset.UtcNow,
            PayloadJson = JsonSerializer.Serialize(payload)
        };

        await _store.AppendAsync(@event, ct);

        // Broadcast para subscribers (se configurado)
        if (_broadcaster != null)
        {
            await _broadcaster.BroadcastAsync(@event, ct);
        }
    }
}
```

### Passo 5: Integracao com JobExecutor

```csharp
// JobExecutor.cs - Emitir eventos
public class JobExecutor
{
    private readonly IEventPublisher _events;

    public async Task<JobRun> ExecuteAsync(JobRun run, CancellationToken ct)
    {
        await _events.PublishAsync(run.Id, EventTypes.JobStarted, new JobStartedPayload(
            WorkerId: _workerId,
            AttemptNumber: run.AttemptNumber
        ), ct);

        try
        {
            var result = await ExecuteJobAsync(run, ct);

            await _events.PublishAsync(run.Id, EventTypes.JobCompleted, new JobCompletedPayload(
                Duration: DateTimeOffset.UtcNow - run.StartedAt!.Value,
                OutputJson: result.OutputJson,
                SucceededCount: result.SucceededCount,
                FailedCount: result.FailedCount
            ), ct);

            return run;
        }
        catch (Exception ex)
        {
            var willRetry = run.AttemptNumber < run.MaxRetries;

            await _events.PublishAsync(run.Id, EventTypes.JobFailed, new JobFailedPayload(
                ErrorMessage: ex.Message,
                StackTrace: ex.StackTrace,
                AttemptNumber: run.AttemptNumber,
                WillRetry: willRetry,
                NextRetryAt: willRetry ? CalculateNextRetry(run) : null
            ), ct);

            throw;
        }
    }
}
```

### Passo 6: Context com Eventos

```csharp
// JobExecutionContext.cs - Expor eventos para o job
public class JobExecutionContext<TInput>
{
    private readonly IEventPublisher _events;

    // Emitir evento customizado
    public async Task EmitEventAsync<TPayload>(string eventType, TPayload payload)
    {
        await _events.PublishAsync(RunId, eventType, payload);
    }

    // Shortcut para eventos comuns
    public async Task EmitProgressAsync(int current, int total, string? message = null)
    {
        await _events.PublishAsync(RunId, EventTypes.ProgressUpdated, new ProgressUpdatedPayload(
            Current: current,
            Total: total,
            Message: message
        ));
    }
}
```

### Passo 7: Event Replay

```csharp
// EventReplayer.cs - Replay de eventos para debugging/auditoria
public class EventReplayer
{
    private readonly IEventStore _store;

    // Reconstruir estado do job em qualquer ponto
    public async Task<JobState> ReplayToPointAsync(Guid runId, DateTimeOffset pointInTime, CancellationToken ct)
    {
        var events = await _store.GetEventsAsync(runId, ct: ct);
        var state = new JobState();

        foreach (var @event in events.Where(e => e.Timestamp <= pointInTime))
        {
            state = ApplyEvent(state, @event);
        }

        return state;
    }

    // Gerar diff entre dois pontos
    public async Task<IReadOnlyList<JobEvent>> GetEventsBetweenAsync(
        Guid runId,
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken ct)
    {
        var events = await _store.GetEventsAsync(runId, ct: ct);
        return events.Where(e => e.Timestamp >= from && e.Timestamp <= to).ToList();
    }

    private JobState ApplyEvent(JobState state, JobEvent @event)
    {
        return @event.EventType switch
        {
            EventTypes.JobStarted => state with { Status = JobStatus.Running },
            EventTypes.JobCompleted => state with { Status = JobStatus.Completed },
            EventTypes.JobFailed => state with { Status = JobStatus.Failed },
            EventTypes.ProgressUpdated => ApplyProgress(state, @event),
            _ => state
        };
    }
}
```

### Passo 8: API de Timeline

```csharp
// GET /api/jobs/{id}/timeline
public class TimelineController
{
    [HttpGet("{runId}/timeline")]
    public async Task<ActionResult<TimelineResponse>> GetTimeline(Guid runId)
    {
        var events = await _eventStore.GetTimelineAsync(runId);

        return new TimelineResponse
        {
            RunId = runId,
            Events = events.Select(e => new TimelineEvent
            {
                Id = e.Id,
                Type = e.EventType,
                Timestamp = e.Timestamp,
                Payload = JsonDocument.Parse(e.PayloadJson)
            }).ToList(),
            Summary = new TimelineSummary
            {
                TotalEvents = events.Count,
                Duration = events.Last().Timestamp - events.First().Timestamp,
                EventCounts = events.GroupBy(e => e.EventType)
                    .ToDictionary(g => g.Key, g => g.Count())
            }
        };
    }
}
```

### Passo 9: Migration SQL

```sql
-- AddEventHistory.sql
CREATE TABLE IF NOT EXISTS zapjobs.events (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    run_id UUID NOT NULL,  -- Sem FK para permitir eventos orphans
    workflow_id UUID,
    event_type VARCHAR(100) NOT NULL,
    sequence_number BIGSERIAL NOT NULL,  -- Auto-incremento global
    timestamp TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    payload_json JSONB NOT NULL DEFAULT '{}',
    correlation_id VARCHAR(255)
);

-- Index para queries comuns
CREATE INDEX idx_events_run_id ON zapjobs.events(run_id);
CREATE INDEX idx_events_workflow_id ON zapjobs.events(workflow_id) WHERE workflow_id IS NOT NULL;
CREATE INDEX idx_events_type ON zapjobs.events(event_type);
CREATE INDEX idx_events_timestamp ON zapjobs.events(timestamp);
CREATE INDEX idx_events_correlation ON zapjobs.events(correlation_id) WHERE correlation_id IS NOT NULL;

-- Index composto para timeline
CREATE INDEX idx_events_run_seq ON zapjobs.events(run_id, sequence_number);

-- Partitioning por data (para grandes volumes)
-- CREATE TABLE zapjobs.events_2025_01 PARTITION OF zapjobs.events
--     FOR VALUES FROM ('2025-01-01') TO ('2025-02-01');
```

## Dashboard Integration

```
Timeline Visual:

    ┌─ 10:00:00 ─────────────────────────────────────────┐
    │ ● job.scheduled                                     │
    │   Queue: default, Type: send-email                 │
    ├─ 10:00:05 ─────────────────────────────────────────┤
    │ ● job.started                                       │
    │   Worker: worker-1, Attempt: 1                     │
    ├─ 10:00:06 ─────────────────────────────────────────┤
    │ ○ activity.started                                  │
    │   Activity: fetch-template                         │
    ├─ 10:00:07 ─────────────────────────────────────────┤
    │ ○ activity.completed                                │
    │   Duration: 1.2s                                   │
    ├─ 10:00:08 ─────────────────────────────────────────┤
    │ ○ progress.updated                                  │
    │   50/100 - Sending emails...                       │
    ├─ 10:00:15 ─────────────────────────────────────────┤
    │ ● job.completed                                     │
    │   Duration: 10s, Success: 100                      │
    └────────────────────────────────────────────────────┘
```

## Criterios de Aceite

- [ ] Eventos sao registrados automaticamente no lifecycle do job
- [ ] Jobs podem emitir eventos customizados
- [ ] API retorna timeline completa de um job
- [ ] Dashboard mostra timeline visual
- [ ] Export de eventos em JSON
- [ ] Filtros por tipo, data, correlationId
- [ ] Replay reconstroi estado do job
- [ ] Performance: < 5ms para append de evento

## Dependencias

- Complementa #26 (Event Broadcasting) - mesmos eventos
- Complementa #31 (Durable Execution) - registra activities

## Checklist Pre-Commit

- [ ] JobEvent entidade criada
- [ ] EventTypes definidos
- [ ] Payloads tipados criados
- [ ] IEventStore interface criada
- [ ] IEventPublisher interface criada
- [ ] EventStore implementado (PostgreSQL)
- [ ] EventPublisher implementado
- [ ] JobExecutor emite eventos
- [ ] Context expoe EmitEventAsync
- [ ] EventReplayer implementado
- [ ] API de timeline
- [ ] Migration SQL criada
- [ ] Testes unitarios
- [ ] CLAUDE.md atualizado
- [ ] Atualizar roadmap (marcar como concluido)
- [ ] Mover este prompt para `roadmap/archive/`

## Referencias

- [Temporal Event History](https://docs.temporal.io/concepts/what-is-an-event-history)
- [Event Sourcing Pattern](https://learn.microsoft.com/en-us/azure/architecture/patterns/event-sourcing)
- [EventStoreDB](https://www.eventstore.com/) - Database for event sourcing
- [Marten Events](https://martendb.io/events/) - Event sourcing for .NET
