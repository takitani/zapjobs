# Prompt: Implementar SignalR Real-time Dashboard

## Objetivo

Converter o dashboard para usar SignalR, permitindo atualizações em tempo real sem polling.

## Contexto

Atualmente o dashboard usa polling (refresh a cada X segundos). Com SignalR:
- Atualizações instantâneas quando jobs mudam de status
- Logs aparecem em tempo real durante execução
- Menor carga no servidor
- Melhor UX

## Requisitos

### Hub SignalR

```csharp
// ZapJobs.AspNetCore/Hubs/ZapJobsHub.cs
public class ZapJobsHub : Hub
{
    private readonly IJobStorage _storage;
    private readonly ILogger<ZapJobsHub> _logger;

    public ZapJobsHub(IJobStorage storage, ILogger<ZapJobsHub> logger)
    {
        _storage = storage;
        _logger = logger;
    }

    public override async Task OnConnectedAsync()
    {
        _logger.LogDebug("Client connected: {ConnectionId}", Context.ConnectionId);

        // Send initial stats
        var stats = await _storage.GetStatsAsync();
        await Clients.Caller.SendAsync("StatsUpdated", stats);

        await base.OnConnectedAsync();
    }

    // Subscribe to specific job runs
    public async Task SubscribeToRun(Guid runId)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, $"run:{runId}");
        _logger.LogDebug("Client {ConnectionId} subscribed to run {RunId}",
            Context.ConnectionId, runId);
    }

    public async Task UnsubscribeFromRun(Guid runId)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"run:{runId}");
    }

    // Subscribe to job type updates
    public async Task SubscribeToJobType(string jobTypeId)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, $"job:{jobTypeId}");
    }

    public async Task UnsubscribeFromJobType(string jobTypeId)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"job:{jobTypeId}");
    }
}
```

### Notifier Service

```csharp
// ZapJobs.AspNetCore/Services/SignalRNotifier.cs
public interface IDashboardNotifier
{
    Task NotifyStatsUpdatedAsync(JobStorageStats stats);
    Task NotifyJobRunUpdatedAsync(JobRun run);
    Task NotifyLogAddedAsync(Guid runId, JobLog log);
    Task NotifyWorkerUpdatedAsync(JobHeartbeat heartbeat);
}

public class SignalRDashboardNotifier : IDashboardNotifier
{
    private readonly IHubContext<ZapJobsHub> _hubContext;

    public SignalRDashboardNotifier(IHubContext<ZapJobsHub> hubContext)
    {
        _hubContext = hubContext;
    }

    public async Task NotifyStatsUpdatedAsync(JobStorageStats stats)
    {
        await _hubContext.Clients.All.SendAsync("StatsUpdated", stats);
    }

    public async Task NotifyJobRunUpdatedAsync(JobRun run)
    {
        // Notify all clients
        await _hubContext.Clients.All.SendAsync("JobRunUpdated", new
        {
            run.Id,
            run.JobTypeId,
            Status = run.Status.ToString(),
            run.Progress,
            run.DurationMs,
            run.ErrorMessage
        });

        // Notify subscribers of this specific run
        await _hubContext.Clients
            .Group($"run:{run.Id}")
            .SendAsync("RunDetailUpdated", run);

        // Notify subscribers of this job type
        await _hubContext.Clients
            .Group($"job:{run.JobTypeId}")
            .SendAsync("JobTypeRunUpdated", run);
    }

    public async Task NotifyLogAddedAsync(Guid runId, JobLog log)
    {
        await _hubContext.Clients
            .Group($"run:{runId}")
            .SendAsync("LogAdded", new
            {
                log.Id,
                log.RunId,
                Level = log.Level.ToString(),
                log.Message,
                log.Category,
                log.Timestamp
            });
    }

    public async Task NotifyWorkerUpdatedAsync(JobHeartbeat heartbeat)
    {
        await _hubContext.Clients.All.SendAsync("WorkerUpdated", new
        {
            heartbeat.WorkerId,
            heartbeat.Hostname,
            heartbeat.CurrentJobType,
            heartbeat.Timestamp,
            heartbeat.JobsProcessed
        });
    }
}
```

### Integrar no JobExecutor

```csharp
// Injetar IDashboardNotifier e notificar em pontos chave
public class JobExecutor : IJobExecutor
{
    private readonly IDashboardNotifier? _notifier;

    public async Task<JobRunResult> ExecuteAsync(JobRun run, CancellationToken ct)
    {
        // Notify run started
        run.Status = JobRunStatus.Running;
        await _notifier?.NotifyJobRunUpdatedAsync(run);

        // ... execute job ...

        // Notify run completed/failed
        await _notifier?.NotifyJobRunUpdatedAsync(run);

        // Update stats
        var stats = await _storage.GetStatsAsync(ct);
        await _notifier?.NotifyStatsUpdatedAsync(stats);

        return result;
    }
}
```

### JavaScript Client

```javascript
// dashboard.js - adicionar SignalR
class ZapJobsDashboard {
    constructor() {
        this.connection = null;
        this.setupSignalR();
    }

    async setupSignalR() {
        this.connection = new signalR.HubConnectionBuilder()
            .withUrl('/zapjobs/hub')
            .withAutomaticReconnect()
            .build();

        // Handle events
        this.connection.on('StatsUpdated', (stats) => {
            this.updateStats(stats);
        });

        this.connection.on('JobRunUpdated', (run) => {
            this.updateRunInList(run);
        });

        this.connection.on('LogAdded', (log) => {
            this.appendLog(log);
        });

        this.connection.on('WorkerUpdated', (worker) => {
            this.updateWorker(worker);
        });

        // Connection state
        this.connection.onreconnecting(() => {
            this.showConnectionStatus('Reconnecting...');
        });

        this.connection.onreconnected(() => {
            this.showConnectionStatus('Connected');
        });

        this.connection.onclose(() => {
            this.showConnectionStatus('Disconnected');
        });

        try {
            await this.connection.start();
            this.showConnectionStatus('Connected');
        } catch (err) {
            console.error('SignalR connection error:', err);
            this.showConnectionStatus('Failed to connect');
        }
    }

    async subscribeToRun(runId) {
        await this.connection.invoke('SubscribeToRun', runId);
    }

    updateStats(stats) {
        document.getElementById('stat-pending').textContent = stats.pendingRuns;
        document.getElementById('stat-running').textContent = stats.runningRuns;
        document.getElementById('stat-completed').textContent = stats.completedToday;
        document.getElementById('stat-failed').textContent = stats.failedToday;
    }

    updateRunInList(run) {
        const row = document.querySelector(`tr[data-run-id="${run.id}"]`);
        if (row) {
            row.querySelector('.status').textContent = run.status;
            row.querySelector('.status').className = `badge status ${run.status.toLowerCase()}`;
            if (run.durationMs) {
                row.querySelector('.duration').textContent = this.formatDuration(run.durationMs);
            }
        }
    }

    appendLog(log) {
        const logsContainer = document.getElementById('logs-container');
        if (!logsContainer) return;

        const logEntry = document.createElement('div');
        logEntry.className = `log-entry log-${log.level.toLowerCase()}`;
        logEntry.innerHTML = `
            <span class="log-time">${new Date(log.timestamp).toLocaleTimeString()}</span>
            <span class="log-level">${log.level}</span>
            <span class="log-message">${log.message}</span>
        `;
        logsContainer.appendChild(logEntry);
        logsContainer.scrollTop = logsContainer.scrollHeight;
    }
}
```

### Configuração

```csharp
// ZapJobs.AspNetCore/ServiceCollectionExtensions.cs
public static IServiceCollection AddZapJobsDashboard(
    this IServiceCollection services,
    Action<ZapJobsDashboardOptions>? configure = null)
{
    var options = new ZapJobsDashboardOptions();
    configure?.Invoke(options);

    services.AddSingleton(options);
    services.AddSignalR();
    services.AddSingleton<IDashboardNotifier, SignalRDashboardNotifier>();

    return services;
}

// Endpoint mapping
public static IEndpointConventionBuilder MapZapJobsDashboard(
    this IEndpointRouteBuilder endpoints,
    string pattern = "/zapjobs")
{
    // Map SignalR hub
    endpoints.MapHub<ZapJobsHub>($"{pattern}/hub");

    // Map dashboard middleware
    var pipeline = endpoints.CreateApplicationBuilder()
        .UseMiddleware<DashboardMiddleware>()
        .Build();

    return endpoints.Map($"{pattern}/{{**path}}", pipeline);
}
```

## Arquivos a Criar/Modificar

1. `ZapJobs.AspNetCore/Hubs/ZapJobsHub.cs` - Hub SignalR
2. `ZapJobs.AspNetCore/Services/IDashboardNotifier.cs` - Interface
3. `ZapJobs.AspNetCore/Services/SignalRDashboardNotifier.cs` - Implementação
4. `ZapJobs/Execution/JobExecutor.cs` - Injetar notifier
5. `ZapJobs/Tracking/JobLoggerService.cs` - Notificar logs
6. `ZapJobs.AspNetCore/Dashboard/DashboardMiddleware.cs` - Incluir SignalR JS
7. `ZapJobs.AspNetCore/ServiceCollectionExtensions.cs` - Configurar SignalR

## Critérios de Aceitação

1. [ ] Hub SignalR funciona com autenticação do dashboard
2. [ ] Stats atualizam em tempo real
3. [ ] Lista de runs atualiza quando status muda
4. [ ] Logs aparecem em tempo real na página de detalhes
5. [ ] Workers atualizam em tempo real
6. [ ] Reconexão automática funciona
7. [ ] Indicador visual de status da conexão
8. [ ] Fallback para polling se SignalR falhar
9. [ ] Não quebra dashboard existente
