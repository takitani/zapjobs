# Prompt: Implementar Rate Limiting

## Objetivo

Implementar rate limiting para controlar a taxa de execução de jobs, evitando sobrecarga de recursos externos ou do próprio sistema.

## Contexto

Rate limiting é necessário quando:
- APIs externas têm limites de requisições
- Jobs consomem muitos recursos
- Precisa distribuir carga ao longo do tempo
- Evitar picos de processamento

## Requisitos

### Tipos de Rate Limiting

1. **Por Job Type** - Limitar execuções de um tipo específico
2. **Por Queue** - Limitar execuções em uma fila
3. **Global** - Limitar todas as execuções
4. **Sliding Window** - X execuções por período

### Configuração

```csharp
// ZapJobs.Core/RateLimiting/RateLimitPolicy.cs
public class RateLimitPolicy
{
    /// <summary>Maximum executions allowed</summary>
    public int Limit { get; set; }

    /// <summary>Time window for the limit</summary>
    public TimeSpan Window { get; set; }

    /// <summary>What to do when limit is reached</summary>
    public RateLimitBehavior Behavior { get; set; } = RateLimitBehavior.Delay;

    /// <summary>Maximum delay when using Delay behavior</summary>
    public TimeSpan MaxDelay { get; set; } = TimeSpan.FromMinutes(5);
}

public enum RateLimitBehavior
{
    /// <summary>Delay execution until rate limit allows</summary>
    Delay,

    /// <summary>Reject and move to failed status</summary>
    Reject,

    /// <summary>Skip silently (for recurring jobs)</summary>
    Skip
}
```

### Atualizar JobDefinition

```csharp
public class JobDefinition
{
    // ... existing ...

    /// <summary>Rate limit policy for this job type</summary>
    public RateLimitPolicy? RateLimit { get; set; }
}
```

### Rate Limiter Interface

```csharp
// ZapJobs.Core/RateLimiting/IRateLimiter.cs
public interface IRateLimiter
{
    /// <summary>
    /// Try to acquire a permit to execute
    /// </summary>
    /// <param name="key">Rate limit key (job type, queue, etc)</param>
    /// <param name="policy">Rate limit policy to apply</param>
    /// <returns>Result with permit or delay information</returns>
    Task<RateLimitResult> AcquireAsync(string key, RateLimitPolicy policy, CancellationToken ct = default);

    /// <summary>
    /// Release a permit (for sliding window with concurrent limit)
    /// </summary>
    Task ReleaseAsync(string key, CancellationToken ct = default);

    /// <summary>
    /// Get current usage for a key
    /// </summary>
    Task<RateLimitStatus> GetStatusAsync(string key, CancellationToken ct = default);
}

public record RateLimitResult(
    bool Allowed,
    TimeSpan? RetryAfter = null,
    int RemainingPermits = 0);

public record RateLimitStatus(
    string Key,
    int CurrentCount,
    int Limit,
    TimeSpan WindowRemaining);
```

### Implementação com Sliding Window

```csharp
// ZapJobs/RateLimiting/SlidingWindowRateLimiter.cs
public class SlidingWindowRateLimiter : IRateLimiter
{
    private readonly IJobStorage _storage;

    public async Task<RateLimitResult> AcquireAsync(
        string key,
        RateLimitPolicy policy,
        CancellationToken ct = default)
    {
        var windowStart = DateTime.UtcNow - policy.Window;

        // Count executions in window
        var count = await _storage.CountExecutionsAsync(key, windowStart, ct);

        if (count < policy.Limit)
        {
            // Record this execution
            await _storage.RecordExecutionAsync(key, DateTime.UtcNow, ct);

            return new RateLimitResult(
                Allowed: true,
                RemainingPermits: policy.Limit - count - 1);
        }

        // Calculate when next permit will be available
        var oldestExecution = await _storage.GetOldestExecutionAsync(key, windowStart, ct);
        var retryAfter = oldestExecution.HasValue
            ? oldestExecution.Value.Add(policy.Window) - DateTime.UtcNow
            : policy.Window;

        return new RateLimitResult(
            Allowed: false,
            RetryAfter: retryAfter,
            RemainingPermits: 0);
    }
}
```

### Tabela para Tracking

```sql
CREATE TABLE IF NOT EXISTS {prefix}rate_limit_executions (
    id UUID PRIMARY KEY,
    key VARCHAR(255) NOT NULL,
    executed_at TIMESTAMP NOT NULL DEFAULT NOW()
);

CREATE INDEX IF NOT EXISTS idx_{prefix}rate_limit_key_time
    ON {prefix}rate_limit_executions(key, executed_at DESC);

-- Cleanup old records periodically
CREATE INDEX IF NOT EXISTS idx_{prefix}rate_limit_cleanup
    ON {prefix}rate_limit_executions(executed_at);
```

### Integração no JobProcessor

```csharp
// Em JobProcessorHostedService, antes de executar
private async Task<bool> TryExecuteWithRateLimitAsync(JobRun run, CancellationToken ct)
{
    var definition = await _storage.GetJobDefinitionAsync(run.JobTypeId, ct);

    if (definition?.RateLimit == null)
    {
        // No rate limit, proceed
        await ExecuteJobAsync(run, ct);
        return true;
    }

    var key = $"job:{run.JobTypeId}";
    var result = await _rateLimiter.AcquireAsync(key, definition.RateLimit, ct);

    if (result.Allowed)
    {
        try
        {
            await ExecuteJobAsync(run, ct);
            return true;
        }
        finally
        {
            await _rateLimiter.ReleaseAsync(key, ct);
        }
    }

    // Handle rate limit exceeded
    switch (definition.RateLimit.Behavior)
    {
        case RateLimitBehavior.Delay:
            // Schedule for later
            var delay = result.RetryAfter ?? TimeSpan.FromSeconds(30);
            if (delay > definition.RateLimit.MaxDelay)
                delay = definition.RateLimit.MaxDelay;

            run.ScheduledAt = DateTime.UtcNow.Add(delay);
            run.Status = JobRunStatus.Scheduled;
            await _storage.UpdateRunAsync(run, ct);

            _logger.LogDebug(
                "Rate limited job {JobTypeId} delayed by {Delay}",
                run.JobTypeId, delay);
            return false;

        case RateLimitBehavior.Reject:
            run.Status = JobRunStatus.Failed;
            run.ErrorMessage = "Rate limit exceeded";
            run.CompletedAt = DateTime.UtcNow;
            await _storage.UpdateRunAsync(run, ct);
            return false;

        case RateLimitBehavior.Skip:
            run.Status = JobRunStatus.Cancelled;
            run.CompletedAt = DateTime.UtcNow;
            await _storage.UpdateRunAsync(run, ct);
            return false;

        default:
            throw new InvalidOperationException();
    }
}
```

### Configuração via Fluent API

```csharp
builder.Services.AddZapJobs()
    .AddJob<EmailSenderJob>(opts => opts
        .WithRateLimit(limit: 100, window: TimeSpan.FromHours(1))
        .OnRateLimitExceeded(RateLimitBehavior.Delay))

    .AddJob<ApiCallerJob>(opts => opts
        .WithRateLimit(limit: 60, window: TimeSpan.FromMinutes(1), maxDelay: TimeSpan.FromMinutes(10)))

    .UseQueueRateLimit("external-api", new RateLimitPolicy
    {
        Limit = 30,
        Window = TimeSpan.FromMinutes(1),
        Behavior = RateLimitBehavior.Delay
    });
```

### Métricas

```csharp
// Adicionar ao Prometheus metrics
public static readonly Counter RateLimitHits = Metrics.CreateCounter(
    "zapjobs_rate_limit_hits_total",
    "Number of times rate limit was hit",
    new CounterConfiguration { LabelNames = new[] { "key", "behavior" } });

public static readonly Gauge RateLimitUsage = Metrics.CreateGauge(
    "zapjobs_rate_limit_usage",
    "Current usage ratio of rate limit",
    new GaugeConfiguration { LabelNames = new[] { "key" } });
```

## Exemplo de Uso

```csharp
// Configure rate limits
builder.Services.AddZapJobs(opts =>
{
    // Global rate limit
    opts.GlobalRateLimit = new RateLimitPolicy
    {
        Limit = 1000,
        Window = TimeSpan.FromMinutes(1)
    };
})
.AddJob<SlackNotifierJob>(opts => opts
    .WithRateLimit(1, TimeSpan.FromSeconds(1))) // 1 per second

.AddJob<ExternalApiJob>(opts => opts
    .WithRateLimit(100, TimeSpan.FromHours(1))
    .OnRateLimitExceeded(RateLimitBehavior.Delay)
    .MaxRateLimitDelay(TimeSpan.FromMinutes(30)));

// Check rate limit status
var status = await rateLimiter.GetStatusAsync("job:external-api");
Console.WriteLine($"API calls: {status.CurrentCount}/{status.Limit}, resets in {status.WindowRemaining}");
```

## Arquivos a Criar/Modificar

1. `ZapJobs.Core/RateLimiting/RateLimitPolicy.cs`
2. `ZapJobs.Core/RateLimiting/IRateLimiter.cs`
3. `ZapJobs.Core/Entities/JobDefinition.cs` - Adicionar RateLimit
4. `ZapJobs/RateLimiting/SlidingWindowRateLimiter.cs`
5. `ZapJobs/RateLimiting/RateLimitCleanupService.cs` - Limpar registros antigos
6. `ZapJobs/HostedServices/JobProcessorHostedService.cs` - Integrar rate limiting
7. `ZapJobs.Storage.PostgreSQL/PostgreSqlJobStorage.cs` - Métodos de rate limit
8. `ZapJobs.Storage.PostgreSQL/MigrationRunner.cs` - Nova tabela

## Critérios de Aceitação

1. [ ] Rate limit por job type funciona
2. [ ] Rate limit por queue funciona
3. [ ] Rate limit global funciona
4. [ ] Behavior Delay adia execução
5. [ ] Behavior Reject falha o job
6. [ ] Behavior Skip cancela silenciosamente
7. [ ] Métricas de rate limit expostas
8. [ ] Dashboard mostra status de rate limits
9. [ ] Cleanup remove registros antigos
10. [ ] Testes para todos os cenários
