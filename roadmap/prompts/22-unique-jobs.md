# Unique Jobs / Deduplication

**Prioridade:** P2
**Esforco:** M (Medio)
**Pilar:** CORE
**Gerado em:** 2025-12-13

## Contexto

Atualmente o ZapJobs nao tem protecao nativa contra jobs duplicados. Se o mesmo job for enfileirado multiplas vezes antes de ser processado, todas as instancias serao executadas. Competidores como BullMQ e Sidekiq Pro oferecem deduplicacao nativa.

**Problema:**
- Usuarios podem enfileirar acidentalmente o mesmo job multiplas vezes
- Jobs idempontentes gastam recursos desnecessarios
- Jobs nao-idempotentes podem causar efeitos colaterais duplicados

## Objetivo

Implementar sistema de deduplicacao de jobs com multiplos modos de operacao, permitindo que desenvolvedores configurem comportamento por job type.

## Requisitos

### Funcionais
- [ ] Suporte a 3 modos de deduplicacao:
  - `UntilExecuting`: Lock ate job iniciar execucao
  - `UntilExecuted`: Lock ate job completar (sucesso ou falha)
  - `Throttle`: Ignora duplicatas por TTL configuravel
- [ ] Configuracao via fluent API no registro do job
- [ ] Chave de unicidade customizavel (padrao: hash do input)
- [ ] Suporte a chave parcial (ex: dedupe por tenant_id apenas)
- [ ] Telemetria de jobs ignorados por deduplicacao

### Nao-Funcionais
- [ ] Performance: Verificacao de unicidade < 5ms
- [ ] Concorrencia: Usar SKIP LOCKED ou advisory locks no PostgreSQL
- [ ] Consistencia: Garantir atomicidade na verificacao + enfileiramento

## Arquivos Relevantes

```
src/ZapJobs.Core/
├── Configuration/
│   └── JobDefinitionBuilder.cs      # Adicionar .WithUnique()
│   └── UniqueJobOptions.cs          # NOVO: Opcoes de deduplicacao
├── Entities/
│   └── JobRun.cs                    # Adicionar UniqueKey
├── Abstractions/
│   └── IJobStorage.cs               # Adicionar metodos de unicidade

src/ZapJobs/
├── Scheduling/
│   └── JobSchedulerService.cs       # Verificar unicidade antes de enqueue
├── Services/
│   └── UniqueJobService.cs          # NOVO: Logica de deduplicacao

src/ZapJobs.Storage.PostgreSQL/
└── PostgreSqlJobStorage.cs          # Implementar verificacao atomica
└── Migrations/AddUniqueJobs.sql     # NOVO: Indice e constraints
```

## Implementacao Sugerida

### Passo 1: Modelo de Dados

Adicionar campos em JobRun e criar indice:

```csharp
// JobRun.cs
public string? UniqueKey { get; set; }
public DateTimeOffset? UniqueUntil { get; set; }

// Migration
ALTER TABLE zapjobs.runs ADD COLUMN unique_key VARCHAR(256);
ALTER TABLE zapjobs.runs ADD COLUMN unique_until TIMESTAMPTZ;
CREATE UNIQUE INDEX idx_runs_unique_key
    ON zapjobs.runs (unique_key)
    WHERE unique_key IS NOT NULL
    AND status IN ('Pending', 'Running');
```

### Passo 2: Configuracao Fluent

```csharp
// UniqueJobOptions.cs
public class UniqueJobOptions
{
    public UniqueJobMode Mode { get; set; } = UniqueJobMode.UntilExecuted;
    public TimeSpan? ThrottleDuration { get; set; }
    public Func<object?, string>? KeyGenerator { get; set; }
}

public enum UniqueJobMode
{
    UntilExecuting,  // Lock ate job comecar
    UntilExecuted,   // Lock ate job terminar
    Throttle         // Ignora por TTL
}

// Uso
.AddJob<SendEmailJob>(job => job
    .WithUnique(UniqueJobMode.UntilExecuted))

.AddJob<SyncJob>(job => job
    .WithUnique(UniqueJobMode.Throttle, TimeSpan.FromMinutes(5)))

.AddJob<TenantJob>(job => job
    .WithUnique(opts => {
        opts.Mode = UniqueJobMode.UntilExecuted;
        opts.KeyGenerator = input => ((TenantInput)input!).TenantId;
    }))
```

### Passo 3: Logica de Verificacao

```csharp
// JobSchedulerService.EnqueueAsync
public async Task<Guid> EnqueueAsync(...)
{
    var definition = GetDefinition(jobTypeId);

    if (definition.UniqueOptions != null)
    {
        var uniqueKey = GenerateUniqueKey(jobTypeId, input, definition.UniqueOptions);

        if (await _storage.ExistsUniqueJobAsync(uniqueKey, definition.UniqueOptions))
        {
            _logger.LogDebug("Job {JobTypeId} ignorado: duplicata com key {Key}",
                jobTypeId, uniqueKey);
            return Guid.Empty; // Ou throw? Retornar run existente?
        }

        run.UniqueKey = uniqueKey;
        run.UniqueUntil = CalculateUniqueUntil(definition.UniqueOptions);
    }

    return await _storage.EnqueueAsync(run);
}
```

### Passo 4: Limpeza de Locks

Para modo `UntilExecuting`, limpar o lock quando job inicia:

```csharp
// JobExecutor, ao iniciar execucao
if (run.UniqueKey != null && definition.UniqueOptions?.Mode == UniqueJobMode.UntilExecuting)
{
    await _storage.ClearUniqueLockAsync(run.Id);
}
```

Para modo `Throttle`, usar coluna `unique_until` com timestamp.

## Criterios de Aceite

- [ ] Jobs com mesmo input sao deduplicados no modo UntilExecuted
- [ ] Lock e liberado quando job inicia no modo UntilExecuting
- [ ] Throttle respeita TTL configurado
- [ ] Chave customizada funciona (dedupe por campo especifico)
- [ ] Metricas de jobs deduplicados disponiveis
- [ ] Testes unitarios para cada modo
- [ ] Documentacao no CLAUDE.md atualizada

## Checklist Pre-Commit

- [ ] Codigo implementado e funcionando
- [ ] Migration SQL criada
- [ ] Testes unitarios criados
- [ ] InMemoryStorage implementado
- [ ] PostgreSqlStorage implementado
- [ ] CLAUDE.md atualizado com exemplos de uso
- [ ] Atualizar roadmap (marcar como concluido)
- [ ] Mover este prompt para `roadmap/archive/`

## Referencias

- [BullMQ Deduplication](https://docs.bullmq.io/guide/jobs/deduplication)
- [Sidekiq Unique Jobs](https://github.com/mhenrixon/sidekiq-unique-jobs)
- [Oban Unique Jobs](https://hexdocs.pm/oban/unique-jobs.html)
