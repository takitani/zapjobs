# Misfire Handling

**Prioridade:** P3
**Esforco:** M (Medio)
**Pilar:** TRUST
**Gerado em:** 2025-12-13

## Contexto

Quando o scheduler fica offline (deploy, crash, manutencao) e volta, jobs CRON agendados podem ter "perdido" suas execucoes. Quartz.NET tem um sistema robusto de misfire handling. ZapJobs precisa de algo similar.

Exemplo: Job agendado para 8:00 AM todo dia
- Scheduler offline das 7:00 as 9:00
- Ao voltar: O que fazer com a execucao das 8:00?

## Objetivo

Implementar estrategias configuraveis de misfire handling para jobs recorrentes.

## Requisitos

### Funcionais
- [ ] Detectar jobs que "misfired" (perderam execucao)
- [ ] Estrategias configuraveis:
  - `FireNow`: Executar imediatamente
  - `SkipToNext`: Pular para proxima execucao
  - `FireAll`: Executar todas as perdidas
  - `FireOnce`: Executar uma vez (consolidar)
- [ ] Threshold de misfire configuravel (janela de tolerancia)
- [ ] Logging detalhado de misfires

### Nao-Funcionais
- [ ] Deteccao na inicializacao do scheduler
- [ ] Configuracao por job type
- [ ] Nao afetar jobs nao-recorrentes

## Arquivos Relevantes

```
src/ZapJobs.Core/
├── Configuration/
│   └── JobDefinitionBuilder.cs      # Adicionar .WithMisfirePolicy()
│   └── MisfirePolicy.cs             # NOVO: Enum de estrategias
├── Scheduling/
│   └── MisfireInfo.cs               # NOVO: Info de misfire

src/ZapJobs/
├── Scheduling/
│   └── CronScheduler.cs             # Detectar misfires
│   └── MisfireHandler.cs            # NOVO: Processar misfires
```

## Implementacao Sugerida

### Passo 1: Modelo

```csharp
// MisfirePolicy.cs
public enum MisfirePolicy
{
    /// <summary>Execute immediately on recovery</summary>
    FireNow,

    /// <summary>Skip missed execution, schedule next</summary>
    SkipToNext,

    /// <summary>Execute all missed executions in order</summary>
    FireAll,

    /// <summary>Execute once regardless of how many were missed</summary>
    FireOnce
}

// MisfireInfo.cs
public class MisfireInfo
{
    public Guid DefinitionId { get; init; }
    public string JobTypeId { get; init; } = string.Empty;
    public DateTimeOffset ScheduledAt { get; init; }
    public DateTimeOffset DetectedAt { get; init; }
    public TimeSpan Delay => DetectedAt - ScheduledAt;
    public MisfirePolicy Policy { get; init; }
}

// JobDefinition - adicionar campos
public class JobDefinition
{
    // ... campos existentes ...

    public MisfirePolicy MisfirePolicy { get; set; } = MisfirePolicy.FireNow;

    /// <summary>Execucoes mais antigas que isso sao consideradas misfired</summary>
    public TimeSpan MisfireThreshold { get; set; } = TimeSpan.FromMinutes(5);
}
```

### Passo 2: Configuracao

```csharp
// JobDefinitionBuilder.cs
public JobDefinitionBuilder WithMisfirePolicy(
    MisfirePolicy policy,
    TimeSpan? threshold = null)
{
    _definition.MisfirePolicy = policy;
    if (threshold.HasValue)
        _definition.MisfireThreshold = threshold.Value;
    return this;
}

// Uso
.AddJob<DailyReportJob>(job => job
    .WithCron("0 8 * * *")
    .WithMisfirePolicy(MisfirePolicy.FireOnce, TimeSpan.FromHours(2)))

.AddJob<CriticalSyncJob>(job => job
    .WithCron("*/5 * * * *")
    .WithMisfirePolicy(MisfirePolicy.FireAll))
```

### Passo 3: Deteccao de Misfires

```csharp
// MisfireHandler.cs
public class MisfireHandler
{
    private readonly IJobStorage _storage;
    private readonly IJobScheduler _scheduler;
    private readonly ILogger<MisfireHandler> _logger;

    public async Task<IReadOnlyList<MisfireInfo>> DetectMisfiresAsync()
    {
        var now = DateTimeOffset.UtcNow;
        var misfires = new List<MisfireInfo>();

        // Buscar todas as definitions com CRON
        var definitions = await _storage.GetRecurringDefinitionsAsync();

        foreach (var def in definitions)
        {
            if (string.IsNullOrEmpty(def.CronExpression))
                continue;

            // Calcular quando deveria ter executado
            var lastRun = await _storage.GetLastRunAsync(def.Id);
            var lastRunTime = lastRun?.ScheduledAt ?? def.CreatedAt;

            // Usar Cronos para calcular proximas execucoes desde ultima run
            var cron = CronExpression.Parse(def.CronExpression);
            var expectedExecutions = GetExpectedExecutions(cron, lastRunTime, now);

            foreach (var expected in expectedExecutions)
            {
                var delay = now - expected;

                if (delay > def.MisfireThreshold)
                {
                    misfires.Add(new MisfireInfo
                    {
                        DefinitionId = def.Id,
                        JobTypeId = def.JobTypeId,
                        ScheduledAt = expected,
                        DetectedAt = now,
                        Policy = def.MisfirePolicy
                    });
                }
            }
        }

        return misfires;
    }

    private IEnumerable<DateTimeOffset> GetExpectedExecutions(
        CronExpression cron,
        DateTimeOffset from,
        DateTimeOffset to)
    {
        var current = from;
        while (true)
        {
            var next = cron.GetNextOccurrence(current, TimeZoneInfo.Utc);
            if (next == null || next > to)
                break;

            yield return next.Value;
            current = next.Value;
        }
    }
}
```

### Passo 4: Processar Misfires

```csharp
// MisfireHandler.cs (continuacao)
public async Task ProcessMisfiresAsync(IReadOnlyList<MisfireInfo> misfires)
{
    // Agrupar por job type
    var byJob = misfires.GroupBy(m => m.JobTypeId);

    foreach (var group in byJob)
    {
        var policy = group.First().Policy;
        var missedExecutions = group.OrderBy(m => m.ScheduledAt).ToList();

        _logger.LogWarning(
            "Job {JobTypeId} misfired {Count} times, policy: {Policy}",
            group.Key, missedExecutions.Count, policy);

        switch (policy)
        {
            case MisfirePolicy.FireNow:
                // Executar uma vez agora
                await _scheduler.EnqueueAsync(group.Key);
                break;

            case MisfirePolicy.SkipToNext:
                // Nao fazer nada, proximo CRON vai triggar
                _logger.LogInformation("Skipping {Count} missed executions for {Job}",
                    missedExecutions.Count, group.Key);
                break;

            case MisfirePolicy.FireAll:
                // Executar todas as perdidas em ordem
                foreach (var misfire in missedExecutions)
                {
                    await _scheduler.EnqueueAsync(group.Key,
                        metadata: new { MisfiredFrom = misfire.ScheduledAt });
                }
                break;

            case MisfirePolicy.FireOnce:
                // Executar uma vez (mais recente)
                var latest = missedExecutions.Last();
                await _scheduler.EnqueueAsync(group.Key,
                    metadata: new { MisfiredFrom = latest.ScheduledAt, MissedCount = missedExecutions.Count });
                break;
        }
    }
}
```

### Passo 5: Integrar na Inicializacao

```csharp
// JobProcessorHostedService.cs - StartAsync
public override async Task StartAsync(CancellationToken ct)
{
    // Verificar misfires antes de iniciar processamento
    var misfireHandler = _services.GetRequiredService<MisfireHandler>();
    var misfires = await misfireHandler.DetectMisfiresAsync();

    if (misfires.Any())
    {
        _logger.LogWarning("Detected {Count} misfired jobs", misfires.Count);
        await misfireHandler.ProcessMisfiresAsync(misfires);
    }

    await base.StartAsync(ct);
}
```

## Criterios de Aceite

- [ ] Misfires sao detectados na inicializacao
- [ ] Cada politica funciona corretamente
- [ ] Threshold respeita configuracao por job
- [ ] Logs claros de misfires detectados
- [ ] Jobs nao-recorrentes nao sao afetados
- [ ] FireAll executa na ordem correta
- [ ] Metadata inclui info de misfire

## Checklist Pre-Commit

- [ ] MisfirePolicy enum criado
- [ ] MisfireHandler implementado
- [ ] Integracao na inicializacao
- [ ] Configuracao via fluent API
- [ ] Testes para cada politica
- [ ] Documentacao no CLAUDE.md
- [ ] Atualizar roadmap (marcar como concluido)
- [ ] Mover este prompt para `roadmap/archive/`

## Referencias

- [Quartz.NET Misfire](https://www.quartz-scheduler.net/documentation/quartz-3.x/tutorial/more-about-triggers.html#misfire-instructions)
- [Hangfire Missed Jobs](https://discuss.hangfire.io/t/handling-missed-recurring-jobs/5654)
