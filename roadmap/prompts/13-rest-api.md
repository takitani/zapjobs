# Prompt: Implementar REST API Standalone

## Objetivo

Criar uma API REST completa e documentada que pode ser usada independentemente do dashboard, permitindo integração programática com o ZapJobs.

## Contexto

O dashboard já tem endpoints internos, mas precisamos de uma API pública bem documentada com:
- Versionamento
- OpenAPI/Swagger
- Autenticação
- Rate limiting

## Requisitos

### Endpoints da API

```
POST   /api/v1/jobs/{jobTypeId}/trigger     # Trigger job manually
POST   /api/v1/jobs/{jobTypeId}/schedule    # Schedule job for later
GET    /api/v1/jobs                          # List all job definitions
GET    /api/v1/jobs/{jobTypeId}              # Get job definition
PUT    /api/v1/jobs/{jobTypeId}              # Update job definition
DELETE /api/v1/jobs/{jobTypeId}              # Delete job definition

GET    /api/v1/runs                          # List runs (with filters)
GET    /api/v1/runs/{runId}                  # Get run details
POST   /api/v1/runs/{runId}/cancel           # Cancel run
GET    /api/v1/runs/{runId}/logs             # Get run logs

GET    /api/v1/workers                       # List active workers
GET    /api/v1/stats                         # Get statistics

GET    /api/v1/deadletter                    # List dead letter entries
POST   /api/v1/deadletter/{id}/requeue       # Requeue dead letter
POST   /api/v1/deadletter/{id}/discard       # Discard dead letter

GET    /api/v1/webhooks                      # List webhooks
POST   /api/v1/webhooks                      # Create webhook
PUT    /api/v1/webhooks/{id}                 # Update webhook
DELETE /api/v1/webhooks/{id}                 # Delete webhook
```

### Controllers

```csharp
// ZapJobs.AspNetCore/Api/JobsController.cs
[ApiController]
[Route("api/v1/jobs")]
[Produces("application/json")]
public class JobsController : ControllerBase
{
    private readonly IJobScheduler _scheduler;
    private readonly IJobStorage _storage;

    /// <summary>
    /// Trigger a job for immediate execution
    /// </summary>
    /// <param name="jobTypeId">The job type identifier</param>
    /// <param name="request">Optional input parameters</param>
    /// <returns>The created run ID</returns>
    [HttpPost("{jobTypeId}/trigger")]
    [ProducesResponseType(typeof(TriggerResponse), StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Trigger(
        string jobTypeId,
        [FromBody] TriggerRequest? request = null,
        CancellationToken ct = default)
    {
        var definition = await _storage.GetJobDefinitionAsync(jobTypeId, ct);
        if (definition == null)
            return NotFound(new { error = $"Job type '{jobTypeId}' not found" });

        var runId = await _scheduler.TriggerAsync(jobTypeId, request?.Input, ct);

        return Accepted(new TriggerResponse
        {
            RunId = runId,
            JobTypeId = jobTypeId,
            Status = "pending"
        });
    }

    /// <summary>
    /// Schedule a job for later execution
    /// </summary>
    [HttpPost("{jobTypeId}/schedule")]
    [ProducesResponseType(typeof(ScheduleResponse), StatusCodes.Status202Accepted)]
    public async Task<IActionResult> Schedule(
        string jobTypeId,
        [FromBody] ScheduleRequest request,
        CancellationToken ct = default)
    {
        Guid runId;

        if (request.RunAt.HasValue)
        {
            runId = await _scheduler.ScheduleAsync(
                jobTypeId, request.RunAt.Value, request.Input, request.Queue, ct);
        }
        else if (request.Delay.HasValue)
        {
            runId = await _scheduler.ScheduleAsync(
                jobTypeId, request.Delay.Value, request.Input, request.Queue, ct);
        }
        else
        {
            return BadRequest(new { error = "Either 'runAt' or 'delay' must be specified" });
        }

        return Accepted(new ScheduleResponse
        {
            RunId = runId,
            JobTypeId = jobTypeId,
            ScheduledFor = request.RunAt ?? DateTimeOffset.UtcNow.Add(request.Delay!.Value)
        });
    }

    /// <summary>
    /// List all job definitions
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(IEnumerable<JobDefinitionDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> List(CancellationToken ct = default)
    {
        var definitions = await _storage.GetAllDefinitionsAsync(ct);
        return Ok(definitions.Select(d => new JobDefinitionDto(d)));
    }

    /// <summary>
    /// Get a specific job definition
    /// </summary>
    [HttpGet("{jobTypeId}")]
    [ProducesResponseType(typeof(JobDefinitionDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get(string jobTypeId, CancellationToken ct = default)
    {
        var definition = await _storage.GetJobDefinitionAsync(jobTypeId, ct);
        if (definition == null)
            return NotFound();

        return Ok(new JobDefinitionDto(definition));
    }
}

// ZapJobs.AspNetCore/Api/RunsController.cs
[ApiController]
[Route("api/v1/runs")]
public class RunsController : ControllerBase
{
    /// <summary>
    /// List job runs with optional filters
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(PagedResult<JobRunDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> List(
        [FromQuery] string? jobTypeId = null,
        [FromQuery] JobRunStatus? status = null,
        [FromQuery] int limit = 50,
        [FromQuery] int offset = 0,
        CancellationToken ct = default)
    {
        IReadOnlyList<JobRun> runs;

        if (!string.IsNullOrEmpty(jobTypeId))
        {
            runs = await _storage.GetRunsByJobTypeAsync(jobTypeId, limit, offset, ct);
        }
        else if (status.HasValue)
        {
            runs = await _storage.GetRunsByStatusAsync(status.Value, limit, offset, ct);
        }
        else
        {
            runs = await _storage.GetRunsByStatusAsync(JobRunStatus.Pending, limit, offset, ct);
        }

        return Ok(new PagedResult<JobRunDto>
        {
            Items = runs.Select(r => new JobRunDto(r)).ToList(),
            Limit = limit,
            Offset = offset
        });
    }

    /// <summary>
    /// Get detailed information about a specific run
    /// </summary>
    [HttpGet("{runId:guid}")]
    [ProducesResponseType(typeof(JobRunDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get(Guid runId, CancellationToken ct = default)
    {
        var run = await _storage.GetRunAsync(runId, ct);
        if (run == null)
            return NotFound();

        var logs = await _storage.GetLogsAsync(runId, limit: 100, ct);

        return Ok(new JobRunDetailDto(run, logs));
    }

    /// <summary>
    /// Cancel a pending or running job
    /// </summary>
    [HttpPost("{runId:guid}/cancel")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Cancel(Guid runId, CancellationToken ct = default)
    {
        var cancelled = await _scheduler.CancelAsync(runId, ct);

        if (!cancelled)
        {
            var run = await _storage.GetRunAsync(runId, ct);
            if (run == null)
                return NotFound();

            return Conflict(new { error = $"Cannot cancel run in status '{run.Status}'" });
        }

        return NoContent();
    }
}
```

### DTOs

```csharp
// ZapJobs.AspNetCore/Api/Dtos/
public record TriggerRequest(object? Input = null, string? Queue = null);
public record TriggerResponse(Guid RunId, string JobTypeId, string Status);

public record ScheduleRequest(
    object? Input = null,
    string? Queue = null,
    DateTimeOffset? RunAt = null,
    TimeSpan? Delay = null);

public record ScheduleResponse(Guid RunId, string JobTypeId, DateTimeOffset ScheduledFor);

public record JobDefinitionDto(
    string JobTypeId,
    string DisplayName,
    string? Description,
    string Queue,
    string ScheduleType,
    string? CronExpression,
    int? IntervalMinutes,
    bool IsEnabled,
    DateTime? LastRunAt,
    DateTime? NextRunAt,
    string? LastRunStatus)
{
    public JobDefinitionDto(JobDefinition d) : this(
        d.JobTypeId, d.DisplayName, d.Description, d.Queue,
        d.ScheduleType.ToString(), d.CronExpression, d.IntervalMinutes,
        d.IsEnabled, d.LastRunAt, d.NextRunAt, d.LastRunStatus?.ToString()) { }
}

public record JobRunDto(
    Guid Id,
    string JobTypeId,
    string Status,
    string TriggerType,
    string Queue,
    DateTime CreatedAt,
    DateTime? StartedAt,
    DateTime? CompletedAt,
    int? DurationMs,
    int AttemptNumber,
    string? ErrorMessage)
{
    public JobRunDto(JobRun r) : this(
        r.Id, r.JobTypeId, r.Status.ToString(), r.TriggerType.ToString(),
        r.Queue, r.CreatedAt, r.StartedAt, r.CompletedAt,
        r.DurationMs, r.AttemptNumber, r.ErrorMessage) { }
}

public record PagedResult<T>(
    IReadOnlyList<T> Items,
    int Limit,
    int Offset);
```

### OpenAPI/Swagger

```csharp
// Program.cs ou ServiceCollectionExtensions
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "ZapJobs API",
        Version = "v1",
        Description = "REST API for ZapJobs job scheduler"
    });

    // Include XML comments
    var xmlFile = $"{Assembly.GetExecutingAssembly().GetName().Name}.xml";
    var xmlPath = Path.Combine(AppContext.BaseDirectory, xmlFile);
    if (File.Exists(xmlPath))
        c.IncludeXmlComments(xmlPath);

    // API Key auth
    c.AddSecurityDefinition("ApiKey", new OpenApiSecurityScheme
    {
        Type = SecuritySchemeType.ApiKey,
        In = ParameterLocation.Header,
        Name = "X-Api-Key",
        Description = "API Key authentication"
    });
});

// Endpoint
app.MapSwagger();
app.UseSwaggerUI(c => c.SwaggerEndpoint("/swagger/v1/swagger.json", "ZapJobs API v1"));
```

### Autenticação API Key

```csharp
// ZapJobs.AspNetCore/Auth/ApiKeyAuthHandler.cs
public class ApiKeyAuthHandler : AuthenticationHandler<ApiKeyAuthOptions>
{
    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue("X-Api-Key", out var apiKeyHeader))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var apiKey = apiKeyHeader.ToString();

        if (!Options.ValidApiKeys.Contains(apiKey))
        {
            return Task.FromResult(AuthenticateResult.Fail("Invalid API key"));
        }

        var claims = new[] { new Claim(ClaimTypes.Name, "api-client") };
        var identity = new ClaimsIdentity(claims, Scheme.Name);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, Scheme.Name);

        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
```

## Exemplo de Uso

```bash
# Trigger a job
curl -X POST "http://localhost:5000/api/v1/jobs/email-sender/trigger" \
  -H "X-Api-Key: my-api-key" \
  -H "Content-Type: application/json" \
  -d '{"input": {"to": "user@example.com", "subject": "Hello"}}'

# Schedule a job
curl -X POST "http://localhost:5000/api/v1/jobs/report-generator/schedule" \
  -H "X-Api-Key: my-api-key" \
  -H "Content-Type: application/json" \
  -d '{"delay": "01:00:00"}'

# List runs
curl "http://localhost:5000/api/v1/runs?status=failed&limit=10" \
  -H "X-Api-Key: my-api-key"

# Get run details
curl "http://localhost:5000/api/v1/runs/550e8400-e29b-41d4-a716-446655440000" \
  -H "X-Api-Key: my-api-key"
```

## Arquivos a Criar

```
src/ZapJobs.AspNetCore/
├── Api/
│   ├── Controllers/
│   │   ├── JobsController.cs
│   │   ├── RunsController.cs
│   │   ├── WorkersController.cs
│   │   ├── StatsController.cs
│   │   ├── DeadLetterController.cs
│   │   └── WebhooksController.cs
│   ├── Dtos/
│   │   ├── JobDefinitionDto.cs
│   │   ├── JobRunDto.cs
│   │   ├── TriggerRequest.cs
│   │   └── PagedResult.cs
│   └── Auth/
│       ├── ApiKeyAuthHandler.cs
│       └── ApiKeyAuthOptions.cs
└── ApiServiceCollectionExtensions.cs
```

## Critérios de Aceitação

1. [ ] Todos os endpoints funcionam corretamente
2. [ ] Swagger UI disponível em /swagger
3. [ ] Autenticação por API Key funciona
4. [ ] Respostas seguem formato consistente
5. [ ] Erros retornam JSON com mensagem clara
6. [ ] Paginação funciona em listas
7. [ ] Filtros funcionam corretamente
8. [ ] XML comments geram documentação
9. [ ] Versionamento via URL (/api/v1/)
