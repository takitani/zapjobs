# Prompt: Implementar Prometheus Metrics

## Objetivo

Expor métricas do ZapJobs no formato Prometheus para monitoramento e alertas.

## Contexto

Prometheus é o padrão para métricas em ambientes Kubernetes e cloud-native. Métricas permitem:
- Dashboards no Grafana
- Alertas baseados em thresholds
- SLIs/SLOs para jobs
- Capacity planning

## Requisitos

### Novo Pacote (ou adicionar ao existente)

```xml
<PackageReference Include="prometheus-net" Version="8.2.1" />
<PackageReference Include="prometheus-net.AspNetCore" Version="8.2.1" />
```

### Métricas a Expor

```csharp
// ZapJobs/Metrics/ZapJobsMetrics.cs
public static class ZapJobsMetrics
{
    private static readonly string[] JobLabels = { "job_type", "queue", "status" };
    private static readonly string[] WorkerLabels = { "worker_id", "hostname" };

    // Counters
    public static readonly Counter JobsTotal = Metrics.CreateCounter(
        "zapjobs_jobs_total",
        "Total number of jobs processed",
        new CounterConfiguration { LabelNames = JobLabels });

    public static readonly Counter JobsSucceeded = Metrics.CreateCounter(
        "zapjobs_jobs_succeeded_total",
        "Total number of jobs completed successfully",
        new CounterConfiguration { LabelNames = new[] { "job_type", "queue" } });

    public static readonly Counter JobsFailed = Metrics.CreateCounter(
        "zapjobs_jobs_failed_total",
        "Total number of jobs that failed",
        new CounterConfiguration { LabelNames = new[] { "job_type", "queue", "error_type" } });

    public static readonly Counter JobsRetried = Metrics.CreateCounter(
        "zapjobs_jobs_retried_total",
        "Total number of job retries",
        new CounterConfiguration { LabelNames = new[] { "job_type", "queue" } });

    // Gauges
    public static readonly Gauge JobsPending = Metrics.CreateGauge(
        "zapjobs_jobs_pending",
        "Current number of pending jobs",
        new GaugeConfiguration { LabelNames = new[] { "queue" } });

    public static readonly Gauge JobsRunning = Metrics.CreateGauge(
        "zapjobs_jobs_running",
        "Current number of running jobs",
        new GaugeConfiguration { LabelNames = new[] { "job_type" } });

    public static readonly Gauge WorkersActive = Metrics.CreateGauge(
        "zapjobs_workers_active",
        "Number of active workers",
        new GaugeConfiguration { LabelNames = WorkerLabels });

    public static readonly Gauge DeadLetterQueueSize = Metrics.CreateGauge(
        "zapjobs_dead_letter_queue_size",
        "Number of entries in dead letter queue");

    // Histograms
    public static readonly Histogram JobDuration = Metrics.CreateHistogram(
        "zapjobs_job_duration_seconds",
        "Job execution duration in seconds",
        new HistogramConfiguration
        {
            LabelNames = new[] { "job_type", "queue" },
            Buckets = new[] { 0.1, 0.5, 1, 5, 10, 30, 60, 120, 300, 600 }
        });

    public static readonly Histogram JobWaitTime = Metrics.CreateHistogram(
        "zapjobs_job_wait_time_seconds",
        "Time from job creation to start in seconds",
        new HistogramConfiguration
        {
            LabelNames = new[] { "job_type", "queue" },
            Buckets = new[] { 0.1, 0.5, 1, 5, 10, 30, 60, 120, 300 }
        });

    // Summary (for percentiles)
    public static readonly Summary JobDurationSummary = Metrics.CreateSummary(
        "zapjobs_job_duration_summary_seconds",
        "Job execution duration summary",
        new SummaryConfiguration
        {
            LabelNames = new[] { "job_type" },
            Objectives = new[]
            {
                new QuantileEpsilonPair(0.5, 0.05),
                new QuantileEpsilonPair(0.9, 0.01),
                new QuantileEpsilonPair(0.99, 0.001)
            }
        });
}
```

### Collector para Métricas do Storage

```csharp
// ZapJobs/Metrics/ZapJobsCollector.cs
public class ZapJobsCollector : IDisposable
{
    private readonly IJobStorage _storage;
    private readonly Timer _timer;

    public ZapJobsCollector(IJobStorage storage)
    {
        _storage = storage;
        _timer = new Timer(CollectMetrics, null, TimeSpan.Zero, TimeSpan.FromSeconds(15));
    }

    private async void CollectMetrics(object? state)
    {
        try
        {
            var stats = await _storage.GetStatsAsync();

            ZapJobsMetrics.JobsPending.WithLabels("default").Set(stats.PendingRuns);
            ZapJobsMetrics.DeadLetterQueueSize.Set(stats.DeadLetterCount);

            // Collect per-queue pending counts
            var definitions = await _storage.GetAllDefinitionsAsync();
            foreach (var queue in definitions.Select(d => d.Queue).Distinct())
            {
                var pending = await _storage.GetPendingRunsAsync(new[] { queue }, limit: 1);
                ZapJobsMetrics.JobsPending.WithLabels(queue).Set(pending.Count > 0 ? pending.Count : 0);
            }
        }
        catch
        {
            // Ignore collection errors
        }
    }

    public void Dispose() => _timer.Dispose();
}
```

### Integração no JobExecutor

```csharp
// Em ExecuteAsync, após execução:
private void RecordMetrics(JobRun run, JobRunResult result)
{
    var labels = new[] { run.JobTypeId, run.Queue };

    if (result.Success)
    {
        ZapJobsMetrics.JobsSucceeded.WithLabels(labels).Inc();
        ZapJobsMetrics.JobsTotal.WithLabels(run.JobTypeId, run.Queue, "completed").Inc();
    }
    else
    {
        var errorType = run.ErrorType ?? "unknown";
        ZapJobsMetrics.JobsFailed.WithLabels(run.JobTypeId, run.Queue, errorType).Inc();
        ZapJobsMetrics.JobsTotal.WithLabels(run.JobTypeId, run.Queue, "failed").Inc();

        if (result.WillRetry)
        {
            ZapJobsMetrics.JobsRetried.WithLabels(labels).Inc();
        }
    }

    // Duration
    var durationSeconds = result.DurationMs / 1000.0;
    ZapJobsMetrics.JobDuration.WithLabels(labels).Observe(durationSeconds);
    ZapJobsMetrics.JobDurationSummary.WithLabels(run.JobTypeId).Observe(durationSeconds);

    // Wait time
    if (run.StartedAt.HasValue)
    {
        var waitSeconds = (run.StartedAt.Value - run.CreatedAt).TotalSeconds;
        ZapJobsMetrics.JobWaitTime.WithLabels(labels).Observe(waitSeconds);
    }
}
```

### Endpoint de Métricas

```csharp
// ZapJobs.AspNetCore/ServiceCollectionExtensions.cs
public static IServiceCollection AddZapJobsMetrics(this IServiceCollection services)
{
    services.AddSingleton<ZapJobsCollector>();
    return services;
}

// ZapJobs.AspNetCore/EndpointRouteBuilderExtensions.cs
public static IEndpointConventionBuilder MapZapJobsMetrics(
    this IEndpointRouteBuilder endpoints,
    string pattern = "/zapjobs/metrics")
{
    return endpoints.MapMetrics(pattern);
}
```

## Exemplo de Uso

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddZapJobs()
    .UsePostgreSqlStorage(...)
    .AddJob<MyJob>();

// Enable Prometheus metrics
builder.Services.AddZapJobsMetrics();

var app = builder.Build();

// Expose metrics endpoint
app.MapZapJobsMetrics("/zapjobs/metrics");
// Or use global prometheus endpoint
app.MapMetrics("/metrics");

app.Run();
```

### Grafana Dashboard JSON

Criar dashboard exemplo em `examples/grafana-dashboard.json`:

```json
{
  "title": "ZapJobs Dashboard",
  "panels": [
    {
      "title": "Jobs Processed",
      "type": "stat",
      "targets": [
        { "expr": "sum(rate(zapjobs_jobs_total[5m]))" }
      ]
    },
    {
      "title": "Success Rate",
      "type": "gauge",
      "targets": [
        { "expr": "sum(rate(zapjobs_jobs_succeeded_total[5m])) / sum(rate(zapjobs_jobs_total[5m])) * 100" }
      ]
    },
    {
      "title": "Job Duration (p99)",
      "type": "timeseries",
      "targets": [
        { "expr": "histogram_quantile(0.99, rate(zapjobs_job_duration_seconds_bucket[5m]))" }
      ]
    }
  ]
}
```

## Arquivos a Criar/Modificar

1. `ZapJobs/Metrics/ZapJobsMetrics.cs` - Definição das métricas
2. `ZapJobs/Metrics/ZapJobsCollector.cs` - Collector background
3. `ZapJobs/Execution/JobExecutor.cs` - Recording de métricas
4. `ZapJobs.AspNetCore/ServiceCollectionExtensions.cs` - Registro
5. `ZapJobs.AspNetCore/EndpointRouteBuilderExtensions.cs` - Endpoint
6. `examples/grafana-dashboard.json` - Dashboard exemplo

## Métricas Exportadas

| Métrica | Tipo | Labels | Descrição |
|---------|------|--------|-----------|
| `zapjobs_jobs_total` | Counter | job_type, queue, status | Total de jobs |
| `zapjobs_jobs_succeeded_total` | Counter | job_type, queue | Jobs com sucesso |
| `zapjobs_jobs_failed_total` | Counter | job_type, queue, error_type | Jobs que falharam |
| `zapjobs_jobs_retried_total` | Counter | job_type, queue | Retries |
| `zapjobs_jobs_pending` | Gauge | queue | Jobs pendentes |
| `zapjobs_jobs_running` | Gauge | job_type | Jobs em execução |
| `zapjobs_workers_active` | Gauge | worker_id, hostname | Workers ativos |
| `zapjobs_dead_letter_queue_size` | Gauge | - | Tamanho da DLQ |
| `zapjobs_job_duration_seconds` | Histogram | job_type, queue | Duração |
| `zapjobs_job_wait_time_seconds` | Histogram | job_type, queue | Tempo de espera |

## Critérios de Aceitação

1. [ ] Endpoint `/zapjobs/metrics` retorna formato Prometheus
2. [ ] Counters incrementam corretamente
3. [ ] Gauges refletem estado atual
4. [ ] Histograms têm buckets apropriados
5. [ ] Labels são consistentes
6. [ ] Dashboard Grafana funciona
7. [ ] Documentação de métricas
8. [ ] Testes para recording de métricas
