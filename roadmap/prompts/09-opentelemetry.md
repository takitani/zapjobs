# Prompt: Implementar OpenTelemetry

## Objetivo

Integrar OpenTelemetry para traces e spans em execuções de jobs, permitindo observabilidade moderna com ferramentas como Jaeger, Zipkin, Azure Monitor, etc.

## Contexto

OpenTelemetry é o padrão moderno para observabilidade. Com esta integração:
- Cada execução de job gera spans
- Traces propagam entre jobs encadeados
- Métricas de latência e erros
- Integração com APM tools

## Requisitos

### Novo Pacote

Criar `ZapJobs.OpenTelemetry`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net9.0</TargetFramework>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="OpenTelemetry" Version="1.10.0" />
    <PackageReference Include="OpenTelemetry.Api" Version="1.10.0" />
    <PackageReference Include="OpenTelemetry.Extensions.Hosting" Version="1.10.0" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\ZapJobs\ZapJobs.csproj" />
  </ItemGroup>
</Project>
```

### Instrumentação

```csharp
// ZapJobs.OpenTelemetry/ZapJobsInstrumentation.cs
public static class ZapJobsInstrumentation
{
    public const string ActivitySourceName = "ZapJobs";
    public const string Version = "1.0.0";

    internal static readonly ActivitySource ActivitySource = new(ActivitySourceName, Version);

    // Semantic conventions
    public static class Tags
    {
        public const string JobTypeId = "zapjobs.job.type_id";
        public const string JobRunId = "zapjobs.job.run_id";
        public const string JobQueue = "zapjobs.job.queue";
        public const string JobTriggerType = "zapjobs.job.trigger_type";
        public const string JobAttempt = "zapjobs.job.attempt";
        public const string JobStatus = "zapjobs.job.status";
        public const string JobDurationMs = "zapjobs.job.duration_ms";
        public const string WorkerId = "zapjobs.worker.id";
    }
}
```

### Extension Methods

```csharp
// ZapJobs.OpenTelemetry/TracerProviderBuilderExtensions.cs
public static class TracerProviderBuilderExtensions
{
    public static TracerProviderBuilder AddZapJobsInstrumentation(
        this TracerProviderBuilder builder,
        Action<ZapJobsInstrumentationOptions>? configure = null)
    {
        var options = new ZapJobsInstrumentationOptions();
        configure?.Invoke(options);

        return builder.AddSource(ZapJobsInstrumentation.ActivitySourceName);
    }
}

public class ZapJobsInstrumentationOptions
{
    /// <summary>Record exception details in span</summary>
    public bool RecordException { get; set; } = true;

    /// <summary>Enrich span with additional tags</summary>
    public Action<Activity, JobRun>? Enrich { get; set; }

    /// <summary>Filter which jobs to trace</summary>
    public Func<JobRun, bool>? Filter { get; set; }
}
```

### Instrumentação do JobExecutor

Criar decorator ou modificar JobExecutor:

```csharp
// ZapJobs.OpenTelemetry/TracingJobExecutor.cs
public class TracingJobExecutor : IJobExecutor
{
    private readonly IJobExecutor _inner;
    private readonly ZapJobsInstrumentationOptions _options;

    public TracingJobExecutor(IJobExecutor inner, IOptions<ZapJobsInstrumentationOptions> options)
    {
        _inner = inner;
        _options = options.Value;
    }

    public async Task<JobRunResult> ExecuteAsync(JobRun run, CancellationToken ct = default)
    {
        // Check filter
        if (_options.Filter != null && !_options.Filter(run))
        {
            return await _inner.ExecuteAsync(run, ct);
        }

        using var activity = ZapJobsInstrumentation.ActivitySource.StartActivity(
            name: $"job.execute {run.JobTypeId}",
            kind: ActivityKind.Consumer);

        if (activity == null)
        {
            return await _inner.ExecuteAsync(run, ct);
        }

        // Set standard tags
        activity.SetTag(ZapJobsInstrumentation.Tags.JobTypeId, run.JobTypeId);
        activity.SetTag(ZapJobsInstrumentation.Tags.JobRunId, run.Id.ToString());
        activity.SetTag(ZapJobsInstrumentation.Tags.JobQueue, run.Queue);
        activity.SetTag(ZapJobsInstrumentation.Tags.JobTriggerType, run.TriggerType.ToString());
        activity.SetTag(ZapJobsInstrumentation.Tags.JobAttempt, run.AttemptNumber);

        // Custom enrichment
        _options.Enrich?.Invoke(activity, run);

        try
        {
            var result = await _inner.ExecuteAsync(run, ct);

            activity.SetTag(ZapJobsInstrumentation.Tags.JobStatus, result.Success ? "completed" : "failed");
            activity.SetTag(ZapJobsInstrumentation.Tags.JobDurationMs, result.DurationMs);

            if (!result.Success)
            {
                activity.SetStatus(ActivityStatusCode.Error, result.ErrorMessage);

                if (_options.RecordException && result.ErrorMessage != null)
                {
                    activity.AddEvent(new ActivityEvent("exception", tags: new ActivityTagsCollection
                    {
                        ["exception.type"] = "JobExecutionException",
                        ["exception.message"] = result.ErrorMessage
                    }));
                }
            }

            return result;
        }
        catch (Exception ex)
        {
            activity.SetStatus(ActivityStatusCode.Error, ex.Message);

            if (_options.RecordException)
            {
                activity.RecordException(ex);
            }

            throw;
        }
    }

    // Delegate other methods
    public void RegisterJobType<TJob>() where TJob : IJob => _inner.RegisterJobType<TJob>();
    public void RegisterJobType(Type jobType) => _inner.RegisterJobType(jobType);
    public IReadOnlyCollection<string> GetRegisteredJobTypes() => _inner.GetRegisteredJobTypes();
}
```

### Propagação de Contexto

Para continuations, propagar trace context:

```csharp
// Ao criar continuation, salvar trace context
public async Task<Guid> ContinueWithAsync(Guid parentRunId, ...)
{
    var continuation = new JobContinuation { ... };

    // Capture current trace context
    if (Activity.Current != null)
    {
        continuation.TraceParent = Activity.Current.Id;
        continuation.TraceState = Activity.Current.TraceStateString;
    }

    await _storage.AddContinuationAsync(continuation, ct);
}

// Ao executar continuation, restaurar contexto
if (!string.IsNullOrEmpty(run.TraceParent))
{
    var context = new ActivityContext(
        ActivityTraceId.CreateFromString(traceId),
        ActivitySpanId.CreateFromString(spanId),
        ActivityTraceFlags.Recorded);

    using var activity = ActivitySource.StartActivity(
        name: $"job.execute {run.JobTypeId}",
        kind: ActivityKind.Consumer,
        parentContext: context);
}
```

### Métricas

```csharp
public static class ZapJobsMetrics
{
    private static readonly Meter Meter = new("ZapJobs", "1.0.0");

    public static readonly Counter<long> JobsProcessed = Meter.CreateCounter<long>(
        "zapjobs.jobs.processed",
        description: "Number of jobs processed");

    public static readonly Counter<long> JobsFailed = Meter.CreateCounter<long>(
        "zapjobs.jobs.failed",
        description: "Number of jobs failed");

    public static readonly Histogram<double> JobDuration = Meter.CreateHistogram<double>(
        "zapjobs.jobs.duration",
        unit: "ms",
        description: "Job execution duration");

    public static readonly UpDownCounter<int> JobsRunning = Meter.CreateUpDownCounter<int>(
        "zapjobs.jobs.running",
        description: "Number of jobs currently running");
}
```

## Exemplo de Uso

```csharp
var builder = WebApplication.CreateBuilder(args);

// Configure OpenTelemetry
builder.Services.AddOpenTelemetry()
    .WithTracing(tracing => tracing
        .AddAspNetCoreInstrumentation()
        .AddHttpClientInstrumentation()
        .AddZapJobsInstrumentation(opts =>
        {
            opts.RecordException = true;
            opts.Enrich = (activity, run) =>
            {
                activity.SetTag("custom.tag", "value");
            };
        })
        .AddOtlpExporter())
    .WithMetrics(metrics => metrics
        .AddZapJobsInstrumentation()
        .AddPrometheusExporter());

// Add ZapJobs
builder.Services.AddZapJobs()
    .UsePostgreSqlStorage(...)
    .AddOpenTelemetry(); // Enables tracing
```

## Arquivos a Criar

```
src/ZapJobs.OpenTelemetry/
├── ZapJobs.OpenTelemetry.csproj
├── ZapJobsInstrumentation.cs
├── ZapJobsInstrumentationOptions.cs
├── ZapJobsMetrics.cs
├── TracingJobExecutor.cs
├── TracerProviderBuilderExtensions.cs
└── ServiceCollectionExtensions.cs
```

## Critérios de Aceitação

1. [ ] Traces são gerados para cada job execution
2. [ ] Spans contêm tags semânticas corretas
3. [ ] Exceptions são registradas no span
4. [ ] Métricas de jobs_processed, jobs_failed, duration
5. [ ] Context propagation funciona entre continuations
6. [ ] Exporters (OTLP, Jaeger, Zipkin) funcionam
7. [ ] Filter permite excluir jobs do tracing
8. [ ] Enrich permite adicionar tags customizadas
9. [ ] Documentação com exemplo de setup
