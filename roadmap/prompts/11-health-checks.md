# Prompt: Implementar Health Checks

## Objetivo

Implementar IHealthCheck para ASP.NET Core, permitindo monitoramento de saúde do ZapJobs via `/health` endpoint.

## Contexto

Health checks são essenciais para:
- Kubernetes liveness/readiness probes
- Load balancer health monitoring
- Alertas quando sistema está degradado

## Requisitos

### Health Checks Necessários

1. **Storage Health** - Conexão com banco de dados
2. **Worker Health** - Workers ativos processando jobs
3. **Queue Health** - Filas não estão sobrecarregadas
4. **Dead Letter Health** - DLQ não está muito cheia

### Implementação

```csharp
// ZapJobs.AspNetCore/HealthChecks/ZapJobsStorageHealthCheck.cs
public class ZapJobsStorageHealthCheck : IHealthCheck
{
    private readonly IJobStorage _storage;

    public ZapJobsStorageHealthCheck(IJobStorage storage)
    {
        _storage = storage;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken ct = default)
    {
        try
        {
            var stats = await _storage.GetStatsAsync(ct);

            return HealthCheckResult.Healthy(
                description: "Storage is accessible",
                data: new Dictionary<string, object>
                {
                    ["total_jobs"] = stats.TotalJobs,
                    ["total_runs"] = stats.TotalRuns,
                    ["pending_runs"] = stats.PendingRuns
                });
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy(
                description: "Storage is not accessible",
                exception: ex);
        }
    }
}

// ZapJobs.AspNetCore/HealthChecks/ZapJobsWorkerHealthCheck.cs
public class ZapJobsWorkerHealthCheck : IHealthCheck
{
    private readonly IJobStorage _storage;
    private readonly ZapJobsHealthOptions _options;

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken ct = default)
    {
        try
        {
            var heartbeats = await _storage.GetHeartbeatsAsync(ct);
            var activeWorkers = heartbeats.Count(h =>
                DateTime.UtcNow - h.Timestamp < TimeSpan.FromMinutes(2));

            if (activeWorkers == 0 && _options.RequireActiveWorker)
            {
                return HealthCheckResult.Unhealthy(
                    description: "No active workers",
                    data: new Dictionary<string, object>
                    {
                        ["total_workers"] = heartbeats.Count,
                        ["active_workers"] = 0
                    });
            }

            if (activeWorkers < _options.MinimumWorkers)
            {
                return HealthCheckResult.Degraded(
                    description: $"Only {activeWorkers} active workers (minimum: {_options.MinimumWorkers})",
                    data: new Dictionary<string, object>
                    {
                        ["active_workers"] = activeWorkers,
                        ["minimum_workers"] = _options.MinimumWorkers
                    });
            }

            return HealthCheckResult.Healthy(
                description: $"{activeWorkers} active workers",
                data: new Dictionary<string, object>
                {
                    ["active_workers"] = activeWorkers
                });
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy(exception: ex);
        }
    }
}

// ZapJobs.AspNetCore/HealthChecks/ZapJobsQueueHealthCheck.cs
public class ZapJobsQueueHealthCheck : IHealthCheck
{
    private readonly IJobStorage _storage;
    private readonly ZapJobsHealthOptions _options;

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken ct = default)
    {
        try
        {
            var stats = await _storage.GetStatsAsync(ct);

            if (stats.PendingRuns > _options.MaxPendingJobsUnhealthy)
            {
                return HealthCheckResult.Unhealthy(
                    description: $"Queue backlog too large: {stats.PendingRuns} pending jobs",
                    data: new Dictionary<string, object>
                    {
                        ["pending_runs"] = stats.PendingRuns,
                        ["threshold"] = _options.MaxPendingJobsUnhealthy
                    });
            }

            if (stats.PendingRuns > _options.MaxPendingJobsDegraded)
            {
                return HealthCheckResult.Degraded(
                    description: $"Queue backlog growing: {stats.PendingRuns} pending jobs",
                    data: new Dictionary<string, object>
                    {
                        ["pending_runs"] = stats.PendingRuns,
                        ["threshold"] = _options.MaxPendingJobsDegraded
                    });
            }

            return HealthCheckResult.Healthy(
                description: $"{stats.PendingRuns} pending jobs",
                data: new Dictionary<string, object>
                {
                    ["pending_runs"] = stats.PendingRuns
                });
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy(exception: ex);
        }
    }
}

// ZapJobs.AspNetCore/HealthChecks/ZapJobsDeadLetterHealthCheck.cs
public class ZapJobsDeadLetterHealthCheck : IHealthCheck
{
    private readonly IJobStorage _storage;
    private readonly ZapJobsHealthOptions _options;

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken ct = default)
    {
        try
        {
            var dlqCount = await _storage.GetDeadLetterCountAsync(ct);

            if (dlqCount > _options.MaxDeadLetterUnhealthy)
            {
                return HealthCheckResult.Unhealthy(
                    description: $"Dead letter queue has {dlqCount} entries",
                    data: new Dictionary<string, object>
                    {
                        ["dead_letter_count"] = dlqCount,
                        ["threshold"] = _options.MaxDeadLetterUnhealthy
                    });
            }

            if (dlqCount > _options.MaxDeadLetterDegraded)
            {
                return HealthCheckResult.Degraded(
                    description: $"Dead letter queue growing: {dlqCount} entries",
                    data: new Dictionary<string, object>
                    {
                        ["dead_letter_count"] = dlqCount
                    });
            }

            return HealthCheckResult.Healthy(
                description: $"Dead letter queue has {dlqCount} entries");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy(exception: ex);
        }
    }
}
```

### Opções de Configuração

```csharp
public class ZapJobsHealthOptions
{
    /// <summary>Require at least one active worker</summary>
    public bool RequireActiveWorker { get; set; } = true;

    /// <summary>Minimum workers for healthy status</summary>
    public int MinimumWorkers { get; set; } = 1;

    /// <summary>Pending jobs threshold for degraded</summary>
    public int MaxPendingJobsDegraded { get; set; } = 100;

    /// <summary>Pending jobs threshold for unhealthy</summary>
    public int MaxPendingJobsUnhealthy { get; set; } = 1000;

    /// <summary>Dead letter entries threshold for degraded</summary>
    public int MaxDeadLetterDegraded { get; set; } = 10;

    /// <summary>Dead letter entries threshold for unhealthy</summary>
    public int MaxDeadLetterUnhealthy { get; set; } = 100;
}
```

### Extension Method

```csharp
public static IHealthChecksBuilder AddZapJobsHealthChecks(
    this IHealthChecksBuilder builder,
    Action<ZapJobsHealthOptions>? configure = null)
{
    var options = new ZapJobsHealthOptions();
    configure?.Invoke(options);

    builder.Services.AddSingleton(options);

    return builder
        .AddCheck<ZapJobsStorageHealthCheck>(
            "zapjobs-storage",
            tags: new[] { "zapjobs", "storage", "db" })
        .AddCheck<ZapJobsWorkerHealthCheck>(
            "zapjobs-workers",
            tags: new[] { "zapjobs", "workers" })
        .AddCheck<ZapJobsQueueHealthCheck>(
            "zapjobs-queue",
            tags: new[] { "zapjobs", "queue" })
        .AddCheck<ZapJobsDeadLetterHealthCheck>(
            "zapjobs-deadletter",
            tags: new[] { "zapjobs", "deadletter" });
}
```

## Exemplo de Uso

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddZapJobs()
    .UsePostgreSqlStorage(...);

// Add health checks
builder.Services.AddHealthChecks()
    .AddZapJobsHealthChecks(opts =>
    {
        opts.MinimumWorkers = 2;
        opts.MaxPendingJobsDegraded = 50;
        opts.MaxPendingJobsUnhealthy = 500;
    });

var app = builder.Build();

// Map health endpoints
app.MapHealthChecks("/health", new HealthCheckOptions
{
    ResponseWriter = UIResponseWriter.WriteHealthCheckUIResponse
});

// Separate liveness/readiness for Kubernetes
app.MapHealthChecks("/health/live", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("zapjobs") && check.Tags.Contains("storage")
});

app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("zapjobs")
});

app.Run();
```

### Kubernetes Deployment

```yaml
apiVersion: apps/v1
kind: Deployment
spec:
  template:
    spec:
      containers:
      - name: app
        livenessProbe:
          httpGet:
            path: /health/live
            port: 8080
          initialDelaySeconds: 10
          periodSeconds: 30
        readinessProbe:
          httpGet:
            path: /health/ready
            port: 8080
          initialDelaySeconds: 5
          periodSeconds: 10
```

## Arquivos a Criar

```
src/ZapJobs.AspNetCore/
├── HealthChecks/
│   ├── ZapJobsStorageHealthCheck.cs
│   ├── ZapJobsWorkerHealthCheck.cs
│   ├── ZapJobsQueueHealthCheck.cs
│   ├── ZapJobsDeadLetterHealthCheck.cs
│   └── ZapJobsHealthOptions.cs
└── HealthChecksBuilderExtensions.cs
```

## Critérios de Aceitação

1. [ ] Storage health check verifica conexão com banco
2. [ ] Worker health check verifica workers ativos
3. [ ] Queue health check verifica backlog
4. [ ] Dead letter health check verifica DLQ
5. [ ] Thresholds são configuráveis
6. [ ] Tags permitem filtrar checks
7. [ ] Response inclui dados detalhados
8. [ ] Funciona com Kubernetes probes
9. [ ] Documentação com exemplos K8s
