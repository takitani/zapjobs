# Prompt: Implementar Multi-tenancy

## Objetivo

Adicionar suporte a multi-tenancy, permitindo que múltiplos tenants (clientes/organizações) usem a mesma instância do ZapJobs com isolamento de dados.

## Contexto

Multi-tenancy é necessário para:
- SaaS applications
- Isolamento de dados por cliente
- Cotas e limites por tenant
- Billing por tenant

## Requisitos

### Estratégias de Isolamento

1. **Column-based** - Coluna `tenant_id` em todas as tabelas (recomendado)
2. **Schema-based** - Schema PostgreSQL separado por tenant
3. **Database-based** - Database separado por tenant

### Tenant Context

```csharp
// ZapJobs.Core/MultiTenancy/ITenantContext.cs
public interface ITenantContext
{
    /// <summary>Current tenant ID</summary>
    string? TenantId { get; }

    /// <summary>Set current tenant</summary>
    void SetTenant(string tenantId);
}

// ZapJobs.Core/MultiTenancy/TenantInfo.cs
public class TenantInfo
{
    public string TenantId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public bool IsEnabled { get; set; } = true;

    // Quotas
    public int? MaxJobs { get; set; }
    public int? MaxRunsPerDay { get; set; }
    public int? MaxWorkers { get; set; }
    public int? MaxRetentionDays { get; set; }

    // Configuration
    public string? ConnectionString { get; set; }  // For database-per-tenant
    public string? Schema { get; set; }            // For schema-per-tenant

    public DateTime CreatedAt { get; set; }
    public DateTime? SuspendedAt { get; set; }
}
```

### Tenant Resolver

```csharp
// ZapJobs.Core/MultiTenancy/ITenantResolver.cs
public interface ITenantResolver
{
    /// <summary>Resolve tenant from current context</summary>
    Task<string?> ResolveTenantAsync(CancellationToken ct = default);
}

// Implementations
public class HeaderTenantResolver : ITenantResolver
{
    private readonly IHttpContextAccessor _accessor;
    private readonly string _headerName;

    public Task<string?> ResolveTenantAsync(CancellationToken ct = default)
    {
        var context = _accessor.HttpContext;
        if (context?.Request.Headers.TryGetValue(_headerName, out var values) == true)
        {
            return Task.FromResult<string?>(values.FirstOrDefault());
        }
        return Task.FromResult<string?>(null);
    }
}

public class ClaimsTenantResolver : ITenantResolver
{
    private readonly IHttpContextAccessor _accessor;

    public Task<string?> ResolveTenantAsync(CancellationToken ct = default)
    {
        var tenantClaim = _accessor.HttpContext?.User.FindFirst("tenant_id");
        return Task.FromResult(tenantClaim?.Value);
    }
}
```

### Atualizar Entidades

```csharp
// Adicionar TenantId a todas as entidades
public class JobDefinition
{
    public string? TenantId { get; set; }
    // ... existing ...
}

public class JobRun
{
    public string? TenantId { get; set; }
    // ... existing ...
}

public class JobLog
{
    public string? TenantId { get; set; }
    // ... existing ...
}
```

### Tenant-aware Storage

```csharp
// ZapJobs.Storage.PostgreSQL/TenantAwarePostgreSqlJobStorage.cs
public class TenantAwarePostgreSqlJobStorage : IJobStorage
{
    private readonly PostgreSqlJobStorage _inner;
    private readonly ITenantContext _tenantContext;

    public async Task<IReadOnlyList<JobDefinition>> GetAllDefinitionsAsync(CancellationToken ct = default)
    {
        var tenantId = _tenantContext.TenantId;

        await using var conn = CreateConnection();
        await conn.OpenAsync(ct);

        await using var cmd = new NpgsqlCommand($"""
            SELECT * FROM {_definitionsTable}
            WHERE tenant_id = @tenantId OR tenant_id IS NULL
            ORDER BY display_name
            """, conn);

        cmd.Parameters.AddWithValue("tenantId", (object?)tenantId ?? DBNull.Value);

        // ... rest of implementation
    }

    public async Task<Guid> EnqueueAsync(JobRun run, CancellationToken ct = default)
    {
        // Automatically set tenant
        run.TenantId = _tenantContext.TenantId;

        // Check quotas
        if (_tenantContext.TenantId != null)
        {
            await CheckQuotasAsync(run, ct);
        }

        return await _inner.EnqueueAsync(run, ct);
    }

    private async Task CheckQuotasAsync(JobRun run, CancellationToken ct)
    {
        var tenant = await _tenantStore.GetTenantAsync(_tenantContext.TenantId!, ct);

        if (tenant?.MaxRunsPerDay.HasValue == true)
        {
            var todayCount = await GetTodayRunCountAsync(_tenantContext.TenantId!, ct);
            if (todayCount >= tenant.MaxRunsPerDay)
            {
                throw new QuotaExceededException($"Daily run limit ({tenant.MaxRunsPerDay}) exceeded");
            }
        }
    }
}
```

### Schema Migration

```sql
-- Add tenant_id to all tables
ALTER TABLE {prefix}definitions ADD COLUMN IF NOT EXISTS tenant_id VARCHAR(100);
ALTER TABLE {prefix}runs ADD COLUMN IF NOT EXISTS tenant_id VARCHAR(100);
ALTER TABLE {prefix}logs ADD COLUMN IF NOT EXISTS tenant_id VARCHAR(100);
ALTER TABLE {prefix}heartbeats ADD COLUMN IF NOT EXISTS tenant_id VARCHAR(100);

-- Indexes for tenant queries
CREATE INDEX IF NOT EXISTS idx_{prefix}definitions_tenant ON {prefix}definitions(tenant_id);
CREATE INDEX IF NOT EXISTS idx_{prefix}runs_tenant ON {prefix}runs(tenant_id);

-- Tenant registry table
CREATE TABLE IF NOT EXISTS {prefix}tenants (
    tenant_id VARCHAR(100) PRIMARY KEY,
    name VARCHAR(255) NOT NULL,
    is_enabled BOOLEAN NOT NULL DEFAULT TRUE,
    max_jobs INTEGER,
    max_runs_per_day INTEGER,
    max_workers INTEGER,
    max_retention_days INTEGER,
    connection_string TEXT,
    schema_name VARCHAR(100),
    created_at TIMESTAMP NOT NULL DEFAULT NOW(),
    suspended_at TIMESTAMP
);
```

### Middleware para Web Context

```csharp
// ZapJobs.AspNetCore/MultiTenancy/TenantMiddleware.cs
public class TenantMiddleware
{
    private readonly RequestDelegate _next;

    public async Task InvokeAsync(
        HttpContext context,
        ITenantResolver resolver,
        ITenantContext tenantContext)
    {
        var tenantId = await resolver.ResolveTenantAsync();

        if (tenantId != null)
        {
            tenantContext.SetTenant(tenantId);
        }

        await _next(context);
    }
}
```

### Worker Tenant Handling

```csharp
// Jobs processados precisam saber o tenant
// Opção 1: Armazenar tenant no JobRun e restaurar no worker
public async Task<JobRunResult> ExecuteAsync(JobRun run, CancellationToken ct)
{
    // Set tenant context from run
    if (!string.IsNullOrEmpty(run.TenantId))
    {
        _tenantContext.SetTenant(run.TenantId);
    }

    try
    {
        // Execute job with tenant context
        return await ExecuteJobInternalAsync(run, ct);
    }
    finally
    {
        _tenantContext.Clear();
    }
}
```

### Configuração

```csharp
builder.Services.AddZapJobs()
    .UseMultiTenancy(opts =>
    {
        opts.IsolationStrategy = TenantIsolation.ColumnBased;
        opts.TenantIdColumnName = "tenant_id";
        opts.DefaultTenantId = null; // null = shared/system jobs
    })
    .UseTenantResolver<ClaimsTenantResolver>()
    .UsePostgreSqlStorage(...);

// Or for schema-based
builder.Services.AddZapJobs()
    .UseMultiTenancy(opts =>
    {
        opts.IsolationStrategy = TenantIsolation.SchemaBased;
    })
    .UseTenantResolver<HeaderTenantResolver>("X-Tenant-Id")
    .UsePostgreSqlStorage(...);
```

## Exemplo de Uso

```csharp
// API with tenant from JWT
[ApiController]
[Route("api/v1/jobs")]
public class JobsController : ControllerBase
{
    private readonly IJobScheduler _scheduler;
    private readonly ITenantContext _tenant;

    [HttpPost("trigger/{jobTypeId}")]
    public async Task<IActionResult> Trigger(string jobTypeId)
    {
        // Tenant is automatically resolved from JWT claims
        // All operations are tenant-scoped

        var runId = await _scheduler.EnqueueAsync(jobTypeId);

        return Ok(new { runId, tenant = _tenant.TenantId });
    }
}

// Admin endpoint to manage tenants
[Authorize(Roles = "Admin")]
[Route("api/admin/tenants")]
public class TenantsController : ControllerBase
{
    private readonly ITenantStore _tenants;

    [HttpPost]
    public async Task<IActionResult> CreateTenant(CreateTenantRequest request)
    {
        var tenant = new TenantInfo
        {
            TenantId = request.TenantId,
            Name = request.Name,
            MaxRunsPerDay = 10000,
            MaxRetentionDays = 30
        };

        await _tenants.CreateTenantAsync(tenant);

        // Run migrations for new tenant (if schema-based)
        await _tenants.InitializeTenantAsync(tenant.TenantId);

        return Created($"/api/admin/tenants/{tenant.TenantId}", tenant);
    }

    [HttpPost("{tenantId}/suspend")]
    public async Task<IActionResult> SuspendTenant(string tenantId)
    {
        await _tenants.SuspendTenantAsync(tenantId);
        return NoContent();
    }
}
```

## Arquivos a Criar

```
src/ZapJobs.Core/MultiTenancy/
├── ITenantContext.cs
├── ITenantResolver.cs
├── ITenantStore.cs
├── TenantInfo.cs
├── TenantIsolation.cs
└── QuotaExceededException.cs

src/ZapJobs/MultiTenancy/
├── TenantContext.cs
├── TenantStore.cs
└── TenantAwareJobStorage.cs

src/ZapJobs.AspNetCore/MultiTenancy/
├── TenantMiddleware.cs
├── HeaderTenantResolver.cs
├── ClaimsTenantResolver.cs
└── MultiTenancyServiceCollectionExtensions.cs
```

## Critérios de Aceitação

1. [ ] Tenant é resolvido automaticamente de headers/claims
2. [ ] Todas as queries são filtradas por tenant
3. [ ] Jobs são criados com tenant_id
4. [ ] Quotas são enforced por tenant
5. [ ] Dashboard filtra por tenant (quando aplicável)
6. [ ] Admin pode gerenciar tenants
7. [ ] Tenant suspenso não pode criar jobs
8. [ ] Workers processam jobs preservando tenant context
9. [ ] Métricas separadas por tenant
10. [ ] Testes para isolamento de dados
