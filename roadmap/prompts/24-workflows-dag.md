# Workflows / DAG (Directed Acyclic Graph)

**Prioridade:** P3
**Esforco:** A (Alto)
**Pilar:** CORE
**Gerado em:** 2025-12-13

## Contexto

Job Continuations ja permitem encadear jobs sequencialmente (A -> B -> C). Porem, workflows complexos precisam de:
- Execucao paralela de jobs
- Convergencia (esperar multiplos jobs antes de continuar)
- Visualizacao grafica do fluxo

BullMQ chama isso de "Flows", Oban Pro de "Workflows", Celery de "Canvas".

## Objetivo

Implementar sistema de workflows que permite definir grafos de jobs com dependencias complexas, incluindo execucao paralela e pontos de convergencia.

## Requisitos

### Funcionais
- [ ] Definir workflows como DAG (sem ciclos)
- [ ] Suportar execucao paralela de jobs independentes
- [ ] Suportar convergencia (join) de multiplos jobs
- [ ] API fluent para definicao de workflows
- [ ] Persistencia do estado do workflow
- [ ] Visualizacao do grafo no dashboard

### Nao-Funcionais
- [ ] Performance: Resolver dependencias em O(n) onde n = numero de jobs
- [ ] Resiliencia: Workflow sobrevive a restart do worker
- [ ] Observabilidade: Status de cada step visivel

## Arquivos Relevantes

```
src/ZapJobs.Core/
├── Workflows/
│   └── IWorkflowBuilder.cs          # NOVO: API de definicao
│   └── IWorkflowService.cs          # NOVO: Interface de servico
│   └── WorkflowStep.cs              # NOVO: Entidade de step
│   └── WorkflowStatus.cs            # NOVO: Status do workflow
├── Entities/
│   └── Workflow.cs                  # NOVO: Entidade principal

src/ZapJobs/
├── Workflows/
│   └── WorkflowBuilder.cs           # NOVO: Implementacao builder
│   └── WorkflowService.cs           # NOVO: Gerenciamento
│   └── WorkflowExecutor.cs          # NOVO: Execucao do grafo

src/ZapJobs.Storage.PostgreSQL/
└── Migrations/AddWorkflows.sql      # NOVO: Schema de workflows
```

## Implementacao Sugerida

### Passo 1: Modelo de Dados

```csharp
// Workflow.cs
public class Workflow
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public WorkflowStatus Status { get; set; }
    public string? CreatedBy { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public List<WorkflowStep> Steps { get; set; } = new();
}

// WorkflowStep.cs
public class WorkflowStep
{
    public Guid Id { get; set; }
    public Guid WorkflowId { get; set; }
    public string StepName { get; set; } = string.Empty;
    public string JobTypeId { get; set; } = string.Empty;
    public string? InputJson { get; set; }
    public Guid? RunId { get; set; }  // Job run quando iniciado
    public StepStatus Status { get; set; }
    public List<Guid> DependsOn { get; set; } = new(); // Steps que precisam completar antes
}

public enum StepStatus { Pending, Running, Completed, Failed, Skipped }
public enum WorkflowStatus { Created, Running, Completed, Failed, Cancelled }
```

### Passo 2: API Fluent

```csharp
// IWorkflowBuilder.cs
public interface IWorkflowBuilder
{
    IWorkflowBuilder AddStep(string name, string jobTypeId, object? input = null);
    IWorkflowBuilder AddStep<TInput>(string name, string jobTypeId, TInput input);
    IWorkflowBuilder DependsOn(params string[] stepNames);
    IWorkflowBuilder OnSuccess(string jobTypeId, object? input = null);
    IWorkflowBuilder OnFailure(string jobTypeId, object? input = null);
}

// Uso
var workflowId = await _workflowService.CreateAsync("import-pipeline", workflow =>
{
    // Paralelo: fetch e validate rodam juntos
    workflow.AddStep("fetch", "fetch-data-job", new { Source = "api" });
    workflow.AddStep("validate", "validate-schema-job");

    // Transform depende de ambos
    workflow.AddStep("transform", "transform-data-job")
        .DependsOn("fetch", "validate");

    // Load depende de transform
    workflow.AddStep("load", "load-database-job")
        .DependsOn("transform");

    // Callbacks
    workflow.OnSuccess("send-notification-job", new { Type = "success" });
    workflow.OnFailure("alert-admin-job", new { Workflow = "import-pipeline" });
});
```

### Passo 3: Resolucao de Dependencias

```csharp
// WorkflowExecutor.cs
public class WorkflowExecutor
{
    public async Task ProcessWorkflowAsync(Guid workflowId)
    {
        var workflow = await _storage.GetWorkflowAsync(workflowId);

        // Encontrar steps prontos (dependencias satisfeitas)
        var readySteps = workflow.Steps
            .Where(s => s.Status == StepStatus.Pending)
            .Where(s => s.DependsOn.All(depId =>
                workflow.Steps.First(d => d.Id == depId).Status == StepStatus.Completed))
            .ToList();

        // Enfileirar jobs para steps prontos
        foreach (var step in readySteps)
        {
            var runId = await _scheduler.EnqueueAsync(
                step.JobTypeId,
                step.InputJson,
                metadata: new { WorkflowId = workflowId, StepId = step.Id });

            step.RunId = runId;
            step.Status = StepStatus.Running;
        }

        await _storage.UpdateWorkflowAsync(workflow);
    }
}
```

### Passo 4: Callback ao Completar Job

```csharp
// Quando job completa, verificar se faz parte de workflow
public async Task OnJobCompletedAsync(JobRun run)
{
    if (run.Metadata?.WorkflowId is Guid workflowId)
    {
        var workflow = await _storage.GetWorkflowAsync(workflowId);
        var step = workflow.Steps.First(s => s.RunId == run.Id);

        step.Status = run.Status == JobStatus.Completed
            ? StepStatus.Completed
            : StepStatus.Failed;

        // Verificar se workflow completou ou falhou
        if (workflow.Steps.All(s => s.Status == StepStatus.Completed))
        {
            workflow.Status = WorkflowStatus.Completed;
            await TriggerCallbackAsync(workflow, "success");
        }
        else if (step.Status == StepStatus.Failed)
        {
            workflow.Status = WorkflowStatus.Failed;
            await TriggerCallbackAsync(workflow, "failure");
        }
        else
        {
            // Continuar processando proximos steps
            await ProcessWorkflowAsync(workflowId);
        }
    }
}
```

### Passo 5: Visualizacao no Dashboard

Endpoint que retorna JSON do grafo para renderizacao:

```csharp
// GET /api/workflows/{id}/graph
{
  "nodes": [
    { "id": "fetch", "status": "completed", "jobTypeId": "fetch-data-job" },
    { "id": "validate", "status": "completed" },
    { "id": "transform", "status": "running" },
    { "id": "load", "status": "pending" }
  ],
  "edges": [
    { "from": "fetch", "to": "transform" },
    { "from": "validate", "to": "transform" },
    { "from": "transform", "to": "load" }
  ]
}
```

## Criterios de Aceite

- [ ] Workflows podem ter steps paralelos
- [ ] Steps so iniciam quando dependencias completam
- [ ] Falha em um step falha o workflow (com callback)
- [ ] Grafo e validado (sem ciclos)
- [ ] Dashboard mostra visualizacao do workflow
- [ ] Workflows persistem entre restarts
- [ ] API retorna status detalhado de cada step

## Checklist Pre-Commit

- [ ] Entidades de workflow criadas
- [ ] Migrations SQL criadas
- [ ] WorkflowBuilder implementado
- [ ] WorkflowExecutor implementado
- [ ] Integracao com job completion
- [ ] Endpoint de visualizacao
- [ ] Testes unitarios
- [ ] CLAUDE.md atualizado
- [ ] Atualizar roadmap (marcar como concluido)
- [ ] Mover este prompt para `roadmap/archive/`

## Referencias

- [BullMQ Flows](https://docs.bullmq.io/guide/flows)
- [Oban Workflows](https://oban.pro/features/workflows)
- [Celery Canvas](https://docs.celeryq.dev/en/stable/userguide/canvas.html)
