# Burst Mode

**Prioridade:** P3
**Esforco:** M (Medio)
**Pilar:** CORE
**Gerado em:** 2025-12-13

## Contexto

Rate limiting protege recursos, mas pode subutilizar capacidade quando o sistema esta ocioso. Oban Pro tem "Burst Mode" que permite exceder limites temporariamente quando ha capacidade disponivel.

Exemplo:
- Queue "email" com rate limit de 10/minuto
- Queue "reports" esta vazia
- Burst mode: "email" pode usar 15/minuto temporariamente

## Objetivo

Implementar burst mode que permite exceder rate limits quando ha capacidade ociosa no sistema.

## Requisitos

### Funcionais
- [ ] Configuracao de burst por queue e globalmente
- [ ] Deteccao de capacidade ociosa
- [ ] Limite maximo de burst (ex: 1.5x do normal)
- [ ] Retorno ao normal quando sistema carregado
- [ ] Metricas de burst usage

### Nao-Funcionais
- [ ] Nao afetar fairness entre queues (exceto quando ocioso)
- [ ] Baixo overhead de calculo
- [ ] Configuracao simples

## Arquivos Relevantes

```
src/ZapJobs.Core/
├── Configuration/
│   └── ZapJobsOptions.cs            # Adicionar BurstOptions
│   └── BurstOptions.cs              # NOVO: Configuracao de burst
├── RateLimiting/
│   └── RateLimitPolicy.cs           # Adicionar BurstMultiplier

src/ZapJobs/
├── RateLimiting/
│   └── SlidingWindowRateLimiter.cs  # Integrar burst
│   └── BurstCapacityCalculator.cs   # NOVO: Calcular capacidade
```

## Implementacao Sugerida

### Passo 1: Configuracao

```csharp
// BurstOptions.cs
public class BurstOptions
{
    /// <summary>Enable burst mode globally</summary>
    public bool Enabled { get; set; } = false;

    /// <summary>Maximum burst multiplier (1.5 = 50% acima do limite)</summary>
    public double MaxMultiplier { get; set; } = 1.5;

    /// <summary>Threshold of idle capacity to enable burst (0.5 = 50% ocioso)</summary>
    public double IdleThreshold { get; set; } = 0.5;

    /// <summary>How often to recalculate burst capacity</summary>
    public TimeSpan RecalculationInterval { get; set; } = TimeSpan.FromSeconds(10);
}

// RateLimitPolicy.cs - adicionar
public class RateLimitPolicy
{
    // ... campos existentes ...

    /// <summary>Allow burst when system is idle (null = use global setting)</summary>
    public bool? AllowBurst { get; set; }

    /// <summary>Maximum burst multiplier for this policy</summary>
    public double? BurstMultiplier { get; set; }
}

// ZapJobsOptions.cs - adicionar
public BurstOptions Burst { get; set; } = new();
```

### Passo 2: Uso na Configuracao

```csharp
services.AddZapJobs(options =>
{
    options.WorkerCount = 4;

    // Habilitar burst globalmente
    options.Burst.Enabled = true;
    options.Burst.MaxMultiplier = 1.5;  // Ate 50% acima do limite
    options.Burst.IdleThreshold = 0.4;  // Burst quando 40%+ ocioso

    // Rate limits normais
    options.GlobalRateLimit = RateLimitPolicy.Create(100, TimeSpan.FromMinutes(1));
    options.QueueRateLimits["notifications"] = RateLimitPolicy.Create(20, TimeSpan.FromMinutes(1));
})
.AddJob<EmailJob>(job => job
    .WithRateLimit(10, TimeSpan.FromMinutes(1))
    .WithBurst(multiplier: 2.0))  // Este job pode ir ate 2x
```

### Passo 3: Calculador de Capacidade

```csharp
// BurstCapacityCalculator.cs
public class BurstCapacityCalculator
{
    private readonly ZapJobsOptions _options;
    private readonly IJobStorage _storage;
    private double _currentIdleRatio = 1.0;
    private DateTimeOffset _lastCalculation = DateTimeOffset.MinValue;

    public async Task<double> GetBurstMultiplierAsync(RateLimitPolicy policy)
    {
        if (!_options.Burst.Enabled)
            return 1.0;

        if (policy.AllowBurst == false)
            return 1.0;

        await RefreshIdleRatioIfNeededAsync();

        // Se sistema nao esta ocioso suficiente, sem burst
        if (_currentIdleRatio < _options.Burst.IdleThreshold)
            return 1.0;

        // Calcular multiplier proporcional ao idle
        // Idle 50% -> multiplier 1.25 (metade do caminho ate MaxMultiplier)
        // Idle 100% -> multiplier MaxMultiplier
        var idleAboveThreshold = _currentIdleRatio - _options.Burst.IdleThreshold;
        var maxIdleAboveThreshold = 1.0 - _options.Burst.IdleThreshold;
        var ratio = idleAboveThreshold / maxIdleAboveThreshold;

        var maxMultiplier = policy.BurstMultiplier ?? _options.Burst.MaxMultiplier;
        var multiplier = 1.0 + (ratio * (maxMultiplier - 1.0));

        return Math.Min(multiplier, maxMultiplier);
    }

    private async Task RefreshIdleRatioIfNeededAsync()
    {
        if (DateTimeOffset.UtcNow - _lastCalculation < _options.Burst.RecalculationInterval)
            return;

        // Calcular ratio de ociosidade
        // Baseado em: jobs pendentes vs capacidade de workers

        var pendingCount = await _storage.GetPendingJobCountAsync();
        var totalCapacity = _options.WorkerCount;

        // Se tem menos jobs que workers, sistema esta ocioso
        _currentIdleRatio = pendingCount >= totalCapacity
            ? 0.0
            : 1.0 - ((double)pendingCount / totalCapacity);

        _lastCalculation = DateTimeOffset.UtcNow;
    }
}
```

### Passo 4: Integrar no Rate Limiter

```csharp
// SlidingWindowRateLimiter.cs
public class SlidingWindowRateLimiter : IRateLimiter
{
    private readonly BurstCapacityCalculator _burstCalculator;

    public async Task<RateLimitResult> CheckAsync(
        string key,
        RateLimitPolicy policy,
        CancellationToken ct = default)
    {
        // Calcular limite efetivo com burst
        var burstMultiplier = await _burstCalculator.GetBurstMultiplierAsync(policy);
        var effectiveLimit = (int)(policy.Limit * burstMultiplier);

        // Contar execucoes na janela
        var count = await _storage.CountExecutionsAsync(key, policy.Window);

        if (count >= effectiveLimit)
        {
            return new RateLimitResult
            {
                Allowed = false,
                RetryAfter = CalculateRetryAfter(policy),
                CurrentCount = count,
                EffectiveLimit = effectiveLimit,
                BurstActive = burstMultiplier > 1.0
            };
        }

        await _storage.RecordExecutionAsync(key);

        return new RateLimitResult
        {
            Allowed = true,
            CurrentCount = count + 1,
            EffectiveLimit = effectiveLimit,
            BurstActive = burstMultiplier > 1.0
        };
    }
}

// RateLimitResult.cs - adicionar campos
public class RateLimitResult
{
    // ... campos existentes ...

    /// <summary>Effective limit after burst calculation</summary>
    public int EffectiveLimit { get; init; }

    /// <summary>Whether burst mode is currently active</summary>
    public bool BurstActive { get; init; }
}
```

### Passo 5: Metricas

```csharp
// Para Prometheus ou logging
public class BurstMetrics
{
    public int BurstExecutions { get; set; }
    public int NormalExecutions { get; set; }
    public double CurrentIdleRatio { get; set; }
    public double CurrentBurstMultiplier { get; set; }
}

// No rate limiter, apos permitir execucao em burst
if (result.BurstActive)
{
    _metrics.Increment("zapjobs.burst.executions");
}
```

## Criterios de Aceite

- [ ] Burst ativa apenas quando sistema ocioso
- [ ] Multiplier respeita configuracao
- [ ] Retorna ao normal quando sistema carregado
- [ ] Configuravel por job/queue/global
- [ ] Metricas de burst disponiveis
- [ ] Testes para cenarios de carga variavel
- [ ] Nao afeta jobs sem rate limit

## Checklist Pre-Commit

- [ ] BurstOptions criado
- [ ] BurstCapacityCalculator implementado
- [ ] SlidingWindowRateLimiter atualizado
- [ ] Configuracao via fluent API
- [ ] Metricas de burst
- [ ] Testes unitarios
- [ ] CLAUDE.md atualizado
- [ ] Atualizar roadmap (marcar como concluido)
- [ ] Mover este prompt para `roadmap/archive/`

## Referencias

- [Oban Burst Mode](https://oban.pro/features/smart-engine#burst-mode)
- [Token Bucket with Burst](https://en.wikipedia.org/wiki/Token_bucket)
