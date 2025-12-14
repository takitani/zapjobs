# Event Broadcasting

**Prioridade:** P3
**Esforco:** M (Medio)
**Pilar:** CORE
**Gerado em:** 2025-12-13

## Contexto

Atualmente nao ha como reagir a eventos do ciclo de vida dos jobs de forma desacoplada. Usuarios que querem logging customizado, metricas ou notificacoes precisam modificar cada job individualmente.

Coravel tem um sistema de eventos elegante. ZapJobs deve ter algo similar.

## Objetivo

Implementar sistema de eventos pub/sub para eventos do ciclo de vida dos jobs, permitindo handlers desacoplados.

## Requisitos

### Funcionais
- [ ] Eventos: JobEnqueued, JobStarted, JobCompleted, JobFailed, JobRetrying
- [ ] Handlers registrados via DI
- [ ] Multiplos handlers por evento
- [ ] Handlers sao async
- [ ] Eventos incluem contexto completo (job, run, input, output, error)

### Nao-Funcionais
- [ ] Handlers nao bloqueiam execucao do job
- [ ] Falha em handler nao afeta o job
- [ ] Baixa latencia (fire-and-forget ou background)

## Arquivos Relevantes

```
src/ZapJobs.Core/
├── Events/
│   └── IJobEvent.cs                 # NOVO: Interface base
│   └── JobEnqueuedEvent.cs          # NOVO
│   └── JobStartedEvent.cs           # NOVO
│   └── JobCompletedEvent.cs         # NOVO
│   └── JobFailedEvent.cs            # NOVO
│   └── JobRetryingEvent.cs          # NOVO
│   └── IJobEventHandler.cs          # NOVO: Interface de handler

src/ZapJobs/
├── Events/
│   └── JobEventDispatcher.cs        # NOVO: Dispara eventos
│   └── JobEventBackgroundService.cs # NOVO: Processa em background
```

## Implementacao Sugerida

### Passo 1: Eventos

```csharp
// IJobEvent.cs
public interface IJobEvent
{
    Guid RunId { get; }
    string JobTypeId { get; }
    DateTimeOffset Timestamp { get; }
}

// JobStartedEvent.cs
public class JobStartedEvent : IJobEvent
{
    public Guid RunId { get; init; }
    public string JobTypeId { get; init; } = string.Empty;
    public DateTimeOffset Timestamp { get; init; }
    public int AttemptNumber { get; init; }
    public string? InputJson { get; init; }
    public string Queue { get; init; } = string.Empty;
}

// JobCompletedEvent.cs
public class JobCompletedEvent : IJobEvent
{
    public Guid RunId { get; init; }
    public string JobTypeId { get; init; } = string.Empty;
    public DateTimeOffset Timestamp { get; init; }
    public TimeSpan Duration { get; init; }
    public string? OutputJson { get; init; }
    public int SucceededCount { get; init; }
    public int FailedCount { get; init; }
}

// JobFailedEvent.cs
public class JobFailedEvent : IJobEvent
{
    public Guid RunId { get; init; }
    public string JobTypeId { get; init; } = string.Empty;
    public DateTimeOffset Timestamp { get; init; }
    public string ErrorMessage { get; init; } = string.Empty;
    public string? StackTrace { get; init; }
    public int AttemptNumber { get; init; }
    public bool WillRetry { get; init; }
    public DateTimeOffset? NextRetryAt { get; init; }
}
```

### Passo 2: Interface de Handler

```csharp
// IJobEventHandler.cs
public interface IJobEventHandler<TEvent> where TEvent : IJobEvent
{
    Task HandleAsync(TEvent @event, CancellationToken ct = default);
}

// Exemplo de implementacao
public class SlackNotificationHandler : IJobEventHandler<JobFailedEvent>
{
    private readonly ISlackClient _slack;

    public SlackNotificationHandler(ISlackClient slack) => _slack = slack;

    public async Task HandleAsync(JobFailedEvent @event, CancellationToken ct)
    {
        if (!@event.WillRetry)
        {
            await _slack.SendMessageAsync(
                channel: "#alerts",
                text: $"Job {@event.JobTypeId} falhou permanentemente: {@event.ErrorMessage}");
        }
    }
}

public class MetricsHandler :
    IJobEventHandler<JobStartedEvent>,
    IJobEventHandler<JobCompletedEvent>,
    IJobEventHandler<JobFailedEvent>
{
    private readonly IMetrics _metrics;

    public Task HandleAsync(JobStartedEvent @event, CancellationToken ct)
    {
        _metrics.Increment($"jobs.{@event.JobTypeId}.started");
        return Task.CompletedTask;
    }

    public Task HandleAsync(JobCompletedEvent @event, CancellationToken ct)
    {
        _metrics.Increment($"jobs.{@event.JobTypeId}.completed");
        _metrics.Histogram($"jobs.{@event.JobTypeId}.duration", @event.Duration.TotalMilliseconds);
        return Task.CompletedTask;
    }

    public Task HandleAsync(JobFailedEvent @event, CancellationToken ct)
    {
        _metrics.Increment($"jobs.{@event.JobTypeId}.failed");
        return Task.CompletedTask;
    }
}
```

### Passo 3: Dispatcher

```csharp
// JobEventDispatcher.cs
public interface IJobEventDispatcher
{
    Task DispatchAsync<TEvent>(TEvent @event) where TEvent : IJobEvent;
}

public class JobEventDispatcher : IJobEventDispatcher
{
    private readonly IServiceProvider _services;
    private readonly ILogger<JobEventDispatcher> _logger;
    private readonly Channel<Func<Task>> _channel;

    public JobEventDispatcher(IServiceProvider services, ILogger<JobEventDispatcher> logger)
    {
        _services = services;
        _logger = logger;
        _channel = Channel.CreateUnbounded<Func<Task>>();
    }

    public Task DispatchAsync<TEvent>(TEvent @event) where TEvent : IJobEvent
    {
        // Fire and forget - nao bloqueia
        _channel.Writer.TryWrite(async () =>
        {
            using var scope = _services.CreateScope();
            var handlers = scope.ServiceProvider
                .GetServices<IJobEventHandler<TEvent>>();

            foreach (var handler in handlers)
            {
                try
                {
                    await handler.HandleAsync(@event);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex,
                        "Error in event handler {Handler} for {Event}",
                        handler.GetType().Name, typeof(TEvent).Name);
                }
            }
        });

        return Task.CompletedTask;
    }

    // Usado pelo background service
    public ChannelReader<Func<Task>> Reader => _channel.Reader;
}
```

### Passo 4: Background Service

```csharp
// JobEventBackgroundService.cs
public class JobEventBackgroundService : BackgroundService
{
    private readonly JobEventDispatcher _dispatcher;
    private readonly ILogger<JobEventBackgroundService> _logger;

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        await foreach (var task in _dispatcher.Reader.ReadAllAsync(ct))
        {
            try
            {
                await task();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing event");
            }
        }
    }
}
```

### Passo 5: Registro

```csharp
// ServiceCollectionExtensions.cs
public static IZapJobsBuilder AddEventHandler<THandler>(this IZapJobsBuilder builder)
    where THandler : class
{
    // Registra para cada interface IJobEventHandler<T> que implementa
    var handlerType = typeof(THandler);
    var interfaces = handlerType.GetInterfaces()
        .Where(i => i.IsGenericType &&
                    i.GetGenericTypeDefinition() == typeof(IJobEventHandler<>));

    foreach (var @interface in interfaces)
    {
        builder.Services.AddScoped(@interface, handlerType);
    }

    return builder;
}

// Uso
services.AddZapJobs(options => { ... })
    .AddEventHandler<SlackNotificationHandler>()
    .AddEventHandler<MetricsHandler>();
```

### Passo 6: Disparar Eventos

```csharp
// JobExecutor.cs - antes de executar
await _dispatcher.DispatchAsync(new JobStartedEvent
{
    RunId = run.Id,
    JobTypeId = run.JobTypeId,
    Timestamp = DateTimeOffset.UtcNow,
    AttemptNumber = run.AttemptNumber,
    InputJson = run.InputJson,
    Queue = run.Queue
});

// Apos execucao com sucesso
await _dispatcher.DispatchAsync(new JobCompletedEvent
{
    RunId = run.Id,
    JobTypeId = run.JobTypeId,
    Timestamp = DateTimeOffset.UtcNow,
    Duration = stopwatch.Elapsed,
    OutputJson = outputJson
});
```

## Criterios de Aceite

- [ ] Eventos sao disparados em todos os pontos do ciclo de vida
- [ ] Handlers sao chamados para cada evento
- [ ] Falha em handler nao afeta o job
- [ ] Handlers rodam em background (fire-and-forget)
- [ ] Multiplos handlers podem existir para mesmo evento
- [ ] Handlers podem injetar dependencias via DI
- [ ] Documentacao de todos os eventos disponíveis

## Checklist Pre-Commit

- [ ] Interfaces de eventos criadas
- [ ] Dispatcher implementado
- [ ] Background service implementado
- [ ] Integracao no JobExecutor
- [ ] Exemplo de handler na docs
- [ ] Testes unitarios
- [ ] CLAUDE.md atualizado
- [ ] Atualizar roadmap (marcar como concluido)
- [ ] Mover este prompt para `roadmap/archive/`

## Referencias

- [Coravel Events](https://docs.coravel.net/Events/)
- [MediatR Notifications](https://github.com/jbogard/MediatR/wiki#notifications)
- [System.Threading.Channels](https://docs.microsoft.com/en-us/dotnet/core/extensions/channels)
