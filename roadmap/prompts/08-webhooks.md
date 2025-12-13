# Prompt: Implementar Webhooks

## Objetivo

Implementar sistema de webhooks para notificar URLs externas sobre eventos de jobs (completed, failed, etc).

## Contexto

Webhooks permitem integração com sistemas externos sem polling:
- Notificar Slack quando job falha
- Atualizar sistemas externos quando job completa
- Integrar com pipelines de CI/CD
- Alertas em ferramentas de monitoramento

## Requisitos

### Novas Entidades

```csharp
// ZapJobs.Core/Entities/WebhookSubscription.cs
public class WebhookSubscription
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Name for identification</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>URL to POST to</summary>
    public string Url { get; set; } = string.Empty;

    /// <summary>Events to subscribe to</summary>
    public WebhookEvent Events { get; set; }

    /// <summary>Filter by job type (null = all)</summary>
    public string? JobTypeFilter { get; set; }

    /// <summary>Filter by queue (null = all)</summary>
    public string? QueueFilter { get; set; }

    /// <summary>Secret for HMAC signature</summary>
    public string? Secret { get; set; }

    /// <summary>Custom headers (JSON object)</summary>
    public string? HeadersJson { get; set; }

    /// <summary>Is this subscription active?</summary>
    public bool IsEnabled { get; set; } = true;

    /// <summary>Last successful delivery</summary>
    public DateTime? LastSuccessAt { get; set; }

    /// <summary>Last failed delivery</summary>
    public DateTime? LastFailureAt { get; set; }

    /// <summary>Consecutive failures count</summary>
    public int FailureCount { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

[Flags]
public enum WebhookEvent
{
    None = 0,
    JobStarted = 1,
    JobCompleted = 2,
    JobFailed = 4,
    JobRetrying = 8,
    JobCancelled = 16,
    WorkerStarted = 32,
    WorkerStopped = 64,

    // Common combinations
    AllJobEvents = JobStarted | JobCompleted | JobFailed | JobRetrying | JobCancelled,
    AllEvents = AllJobEvents | WorkerStarted | WorkerStopped
}

// Delivery attempt tracking
public class WebhookDelivery
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid SubscriptionId { get; set; }
    public WebhookEvent Event { get; set; }
    public string PayloadJson { get; set; } = string.Empty;
    public int StatusCode { get; set; }
    public string? ResponseBody { get; set; }
    public int AttemptNumber { get; set; }
    public bool Success { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
```

### Payload do Webhook

```csharp
public class WebhookPayload
{
    public string Event { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public string? JobTypeId { get; set; }
    public Guid? RunId { get; set; }
    public string? Queue { get; set; }
    public string? Status { get; set; }
    public string? ErrorMessage { get; set; }
    public int? DurationMs { get; set; }
    public int? AttemptNumber { get; set; }
    public object? Output { get; set; }
    public Dictionary<string, object>? Metadata { get; set; }
}
```

### Nova Tabela PostgreSQL

```sql
CREATE TABLE IF NOT EXISTS {prefix}webhook_subscriptions (
    id UUID PRIMARY KEY,
    name VARCHAR(255) NOT NULL,
    url VARCHAR(2048) NOT NULL,
    events INTEGER NOT NULL,
    job_type_filter VARCHAR(255),
    queue_filter VARCHAR(100),
    secret VARCHAR(255),
    headers_json TEXT,
    is_enabled BOOLEAN NOT NULL DEFAULT TRUE,
    last_success_at TIMESTAMP,
    last_failure_at TIMESTAMP,
    failure_count INTEGER NOT NULL DEFAULT 0,
    created_at TIMESTAMP NOT NULL DEFAULT NOW(),
    updated_at TIMESTAMP NOT NULL DEFAULT NOW()
);

CREATE TABLE IF NOT EXISTS {prefix}webhook_deliveries (
    id UUID PRIMARY KEY,
    subscription_id UUID NOT NULL REFERENCES {prefix}webhook_subscriptions(id),
    event INTEGER NOT NULL,
    payload_json TEXT NOT NULL,
    status_code INTEGER NOT NULL,
    response_body TEXT,
    attempt_number INTEGER NOT NULL,
    success BOOLEAN NOT NULL,
    created_at TIMESTAMP NOT NULL DEFAULT NOW()
);

CREATE INDEX IF NOT EXISTS idx_{prefix}webhook_deliveries_subscription
    ON {prefix}webhook_deliveries(subscription_id, created_at DESC);
```

### Serviço de Webhooks

```csharp
public interface IWebhookService
{
    // Subscription management
    Task<Guid> CreateSubscriptionAsync(WebhookSubscription subscription, CancellationToken ct = default);
    Task<WebhookSubscription?> GetSubscriptionAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<WebhookSubscription>> GetSubscriptionsAsync(CancellationToken ct = default);
    Task UpdateSubscriptionAsync(WebhookSubscription subscription, CancellationToken ct = default);
    Task DeleteSubscriptionAsync(Guid id, CancellationToken ct = default);

    // Delivery
    Task PublishEventAsync(WebhookEvent eventType, WebhookPayload payload, CancellationToken ct = default);
    Task<IReadOnlyList<WebhookDelivery>> GetDeliveriesAsync(Guid subscriptionId, int limit = 50, CancellationToken ct = default);

    // Manual retry
    Task RetryDeliveryAsync(Guid deliveryId, CancellationToken ct = default);
}
```

### Integração no JobExecutor

```csharp
// Após job completar/falhar
private async Task PublishWebhookAsync(JobRun run, JobRunStatus previousStatus, CancellationToken ct)
{
    var eventType = run.Status switch
    {
        JobRunStatus.Running => WebhookEvent.JobStarted,
        JobRunStatus.Completed => WebhookEvent.JobCompleted,
        JobRunStatus.Failed => WebhookEvent.JobFailed,
        JobRunStatus.AwaitingRetry => WebhookEvent.JobRetrying,
        JobRunStatus.Cancelled => WebhookEvent.JobCancelled,
        _ => WebhookEvent.None
    };

    if (eventType == WebhookEvent.None) return;

    var payload = new WebhookPayload
    {
        Event = eventType.ToString(),
        JobTypeId = run.JobTypeId,
        RunId = run.Id,
        Queue = run.Queue,
        Status = run.Status.ToString(),
        ErrorMessage = run.ErrorMessage,
        DurationMs = run.DurationMs,
        AttemptNumber = run.AttemptNumber
    };

    await _webhookService.PublishEventAsync(eventType, payload, ct);
}
```

### Assinatura HMAC

```csharp
private string ComputeSignature(string payload, string secret)
{
    using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
    var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(payload));
    return $"sha256={Convert.ToHexString(hash).ToLowerInvariant()}";
}

// Header: X-ZapJobs-Signature: sha256=abc123...
```

### Retry com Backoff

```csharp
public class WebhookDeliveryService : BackgroundService
{
    private readonly int[] _retryDelays = { 10, 30, 60, 300, 900 }; // seconds

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            var pendingDeliveries = await GetPendingDeliveriesAsync(ct);

            foreach (var delivery in pendingDeliveries)
            {
                await DeliverWithRetryAsync(delivery, ct);
            }

            await Task.Delay(TimeSpan.FromSeconds(5), ct);
        }
    }
}
```

## Exemplo de Uso

```csharp
// Setup subscription
await webhookService.CreateSubscriptionAsync(new WebhookSubscription
{
    Name = "Slack Failures",
    Url = "https://hooks.slack.com/services/...",
    Events = WebhookEvent.JobFailed,
    Secret = "my-secret-key",
    HeadersJson = JsonSerializer.Serialize(new { Authorization = "Bearer token" })
});

// Filter by job type
await webhookService.CreateSubscriptionAsync(new WebhookSubscription
{
    Name = "Critical Job Monitor",
    Url = "https://my-monitoring.example.com/webhook",
    Events = WebhookEvent.AllJobEvents,
    JobTypeFilter = "critical-*",  // Supports wildcards
    QueueFilter = "critical"
});
```

## Dashboard

Nova página `/zapjobs/webhooks`:
1. Lista de subscriptions com status
2. Criar/editar subscription
3. Histórico de deliveries
4. Retry manual
5. Test webhook (envia ping)

## Arquivos a Criar/Modificar

1. `ZapJobs.Core/Entities/WebhookSubscription.cs`
2. `ZapJobs.Core/Entities/WebhookDelivery.cs`
3. `ZapJobs.Core/Entities/WebhookPayload.cs`
4. `ZapJobs.Core/Abstractions/IWebhookService.cs`
5. `ZapJobs/Webhooks/WebhookService.cs`
6. `ZapJobs/Webhooks/WebhookDeliveryService.cs` (BackgroundService)
7. `ZapJobs/Execution/JobExecutor.cs` - Publicar eventos
8. `ZapJobs.Storage.PostgreSQL/PostgreSqlJobStorage.cs`
9. `ZapJobs.Storage.PostgreSQL/MigrationRunner.cs`
10. `ZapJobs.AspNetCore/Dashboard/DashboardMiddleware.cs` - Nova página

## Critérios de Aceitação

1. [ ] CRUD de webhook subscriptions funciona
2. [ ] Eventos são disparados corretamente
3. [ ] Filtros por job type e queue funcionam
4. [ ] HMAC signature é enviada quando secret configurado
5. [ ] Retry automático com backoff exponencial
6. [ ] Dashboard para gerenciar webhooks
7. [ ] Histórico de deliveries é mantido
8. [ ] Testes para fluxo completo
