# Saga Pattern

**Prioridade:** P3
**Esforco:** A (Alto)
**Pilar:** CORE
**Gerado em:** 2025-12-13

## Contexto

Transacoes distribuidas sao complexas. Quando um workflow de multiplos steps falha no meio, os steps anteriores ja completados podem precisar ser revertidos. O Saga Pattern resolve isso definindo compensating transactions para cada step.

Exemplo: Reserva de viagem
1. Reservar voo -> Compensacao: Cancelar voo
2. Reservar hotel -> Compensacao: Cancelar hotel
3. Cobrar cartao -> Compensacao: Estornar

Se step 3 falhar, executa compensacoes na ordem reversa (2, 1).

## Objetivo

Implementar suporte ao Saga Pattern com compensating transactions automaticas quando workflows falham.

## Requisitos

### Funcionais
- [ ] Definir compensacao para cada step do workflow
- [ ] Executar compensacoes em ordem reversa na falha
- [ ] Suportar compensacoes parciais (apenas steps ja executados)
- [ ] Registrar resultado de cada compensacao
- [ ] Timeout para compensacoes (com retry)

### Nao-Funcionais
- [ ] Garantir que compensacoes executam mesmo com crashes
- [ ] Compensacoes sao idempotentes por design
- [ ] Log detalhado de todas as acoes

## Arquivos Relevantes

```
src/ZapJobs.Core/
├── Sagas/
│   └── ISagaBuilder.cs              # NOVO: API de definicao
│   └── ISagaService.cs              # NOVO: Interface de servico
│   └── SagaStep.cs                  # NOVO: Step com compensacao
│   └── CompensationResult.cs        # NOVO: Resultado
├── Entities/
│   └── Saga.cs                      # NOVO: Entidade principal
│   └── SagaCompensation.cs          # NOVO: Registro de compensacao

src/ZapJobs/
├── Sagas/
│   └── SagaBuilder.cs               # NOVO: Implementacao builder
│   └── SagaService.cs               # NOVO: Gerenciamento
│   └── SagaCompensator.cs           # NOVO: Executa compensacoes

src/ZapJobs.Storage.PostgreSQL/
└── Migrations/AddSagas.sql          # NOVO: Schema de sagas
```

## Implementacao Sugerida

### Passo 1: Modelo de Dados

```csharp
// SagaStep.cs
public class SagaStep
{
    public Guid Id { get; set; }
    public string StepName { get; set; } = string.Empty;

    // Job de acao
    public string ActionJobTypeId { get; set; } = string.Empty;
    public string? ActionInputJson { get; set; }
    public Guid? ActionRunId { get; set; }

    // Job de compensacao
    public string? CompensationJobTypeId { get; set; }
    public string? CompensationInputJson { get; set; }
    public Guid? CompensationRunId { get; set; }

    public SagaStepStatus Status { get; set; }
    public SagaCompensationStatus CompensationStatus { get; set; }
}

public enum SagaStepStatus { Pending, Running, Completed, Failed }
public enum SagaCompensationStatus { NotNeeded, Pending, Running, Completed, Failed }

// Saga.cs
public class Saga
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public SagaStatus Status { get; set; }
    public List<SagaStep> Steps { get; set; } = new();
    public int CurrentStepIndex { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
}

public enum SagaStatus { Running, Completed, Compensating, Compensated, Failed }
```

### Passo 2: API Fluent

```csharp
// ISagaBuilder.cs
public interface ISagaBuilder
{
    ISagaBuilder Step(string name, Action<ISagaStepBuilder> configure);
}

public interface ISagaStepBuilder
{
    ISagaStepBuilder Action(string jobTypeId, object? input = null);
    ISagaStepBuilder Compensation(string jobTypeId, object? input = null);
    ISagaStepBuilder Compensation(string jobTypeId, Func<object?, object?> inputFromAction);
}

// Uso
var sagaId = await _sagaService.CreateAsync("book-trip", saga =>
{
    saga.Step("book-flight", step =>
    {
        step.Action("book-flight-job", new { FlightId = "UA123" });
        step.Compensation("cancel-flight-job", result => new { BookingId = result });
    });

    saga.Step("book-hotel", step =>
    {
        step.Action("book-hotel-job", new { HotelId = "H456" });
        step.Compensation("cancel-hotel-job", result => new { ReservationId = result });
    });

    saga.Step("charge-payment", step =>
    {
        step.Action("charge-card-job", new { Amount = 1500 });
        step.Compensation("refund-card-job", result => new { TransactionId = result });
    });
});

await _sagaService.ExecuteAsync(sagaId);
```

### Passo 3: Execucao da Saga

```csharp
// SagaService.cs
public async Task ExecuteStepAsync(Guid sagaId)
{
    var saga = await _storage.GetSagaAsync(sagaId);

    if (saga.Status != SagaStatus.Running)
        return;

    var currentStep = saga.Steps[saga.CurrentStepIndex];

    if (currentStep.Status == SagaStepStatus.Pending)
    {
        // Enfileirar job de acao
        var runId = await _scheduler.EnqueueAsync(
            currentStep.ActionJobTypeId,
            currentStep.ActionInputJson,
            metadata: new { SagaId = sagaId, StepIndex = saga.CurrentStepIndex });

        currentStep.ActionRunId = runId;
        currentStep.Status = SagaStepStatus.Running;
    }

    await _storage.UpdateSagaAsync(saga);
}
```

### Passo 4: Callback de Sucesso/Falha

```csharp
// Quando job de acao completa
public async Task OnActionCompletedAsync(Guid sagaId, int stepIndex, JobStatus status, string? output)
{
    var saga = await _storage.GetSagaAsync(sagaId);
    var step = saga.Steps[stepIndex];

    if (status == JobStatus.Completed)
    {
        step.Status = SagaStepStatus.Completed;

        // Guardar output para usar na compensacao
        if (step.CompensationInputJson?.Contains("{{output}}") == true)
        {
            step.CompensationInputJson = step.CompensationInputJson
                .Replace("{{output}}", output);
        }

        saga.CurrentStepIndex++;

        if (saga.CurrentStepIndex >= saga.Steps.Count)
        {
            saga.Status = SagaStatus.Completed;
        }
        else
        {
            // Executar proximo step
            await ExecuteStepAsync(sagaId);
        }
    }
    else
    {
        // Falha: iniciar compensacao
        step.Status = SagaStepStatus.Failed;
        saga.Status = SagaStatus.Compensating;
        await StartCompensationAsync(saga);
    }

    await _storage.UpdateSagaAsync(saga);
}
```

### Passo 5: Compensacao

```csharp
// SagaCompensator.cs
public async Task StartCompensationAsync(Saga saga)
{
    // Marcar steps completados como precisando compensacao
    for (int i = saga.CurrentStepIndex - 1; i >= 0; i--)
    {
        var step = saga.Steps[i];
        if (step.Status == SagaStepStatus.Completed && step.CompensationJobTypeId != null)
        {
            step.CompensationStatus = SagaCompensationStatus.Pending;
        }
    }

    await ExecuteNextCompensationAsync(saga);
}

public async Task ExecuteNextCompensationAsync(Saga saga)
{
    // Encontrar proxima compensacao pendente (ordem reversa)
    var nextCompensation = saga.Steps
        .Where(s => s.CompensationStatus == SagaCompensationStatus.Pending)
        .LastOrDefault();

    if (nextCompensation == null)
    {
        saga.Status = SagaStatus.Compensated;
        return;
    }

    var runId = await _scheduler.EnqueueAsync(
        nextCompensation.CompensationJobTypeId!,
        nextCompensation.CompensationInputJson,
        metadata: new { SagaId = saga.Id, StepIndex = saga.Steps.IndexOf(nextCompensation), IsCompensation = true });

    nextCompensation.CompensationRunId = runId;
    nextCompensation.CompensationStatus = SagaCompensationStatus.Running;
}
```

## Criterios de Aceite

- [ ] Sagas executam steps em ordem
- [ ] Falha em step inicia compensacao em ordem reversa
- [ ] Compensacoes recebem output do step original
- [ ] Saga sobrevive a restart (persistente)
- [ ] Dashboard mostra status da saga e compensacoes
- [ ] Compensacoes falhas sao retentadas
- [ ] Logs detalhados de cada passo

## Checklist Pre-Commit

- [ ] Entidades de saga criadas
- [ ] Migrations SQL criadas
- [ ] SagaBuilder implementado
- [ ] SagaService implementado
- [ ] SagaCompensator implementado
- [ ] Integracao com job completion
- [ ] Testes unitarios (sucesso e falha)
- [ ] CLAUDE.md atualizado
- [ ] Atualizar roadmap (marcar como concluido)
- [ ] Mover este prompt para `roadmap/archive/`

## Referencias

- [Saga Pattern - Microsoft](https://learn.microsoft.com/en-us/azure/architecture/reference-architectures/saga/saga)
- [Temporal Saga](https://docs.temporal.io/encyclopedia/application-lifecycle#sagas)
- [MassTransit Sagas](https://masstransit.io/documentation/patterns/saga)
