# Prompt: Implementar Job Templates

## Objetivo

Criar sistema de templates para jobs, permitindo criar jobs a partir de modelos pré-configurados com parâmetros customizáveis.

## Contexto

Job templates são úteis para:
- Jobs similares com pequenas variações
- Permitir que usuários não-técnicos criem jobs
- Padronização de configurações
- Jobs dinâmicos criados em runtime

## Requisitos

### Nova Entidade

```csharp
// ZapJobs.Core/Templates/JobTemplate.cs
public class JobTemplate
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Unique identifier for the template</summary>
    public string TemplateId { get; set; } = string.Empty;

    /// <summary>Display name</summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>Template description</summary>
    public string? Description { get; set; }

    /// <summary>Category for organization</summary>
    public string? Category { get; set; }

    /// <summary>The job type this template creates</summary>
    public string JobTypeId { get; set; } = string.Empty;

    /// <summary>Default queue</summary>
    public string Queue { get; set; } = "default";

    /// <summary>Default schedule type</summary>
    public ScheduleType DefaultScheduleType { get; set; } = ScheduleType.Manual;

    /// <summary>Default CRON expression</summary>
    public string? DefaultCronExpression { get; set; }

    /// <summary>Default interval in minutes</summary>
    public int? DefaultIntervalMinutes { get; set; }

    /// <summary>Default retry policy</summary>
    public RetryPolicy? DefaultRetryPolicy { get; set; }

    /// <summary>Default timeout</summary>
    public TimeSpan? DefaultTimeout { get; set; }

    /// <summary>Parameter definitions</summary>
    public List<TemplateParameter> Parameters { get; set; } = new();

    /// <summary>Default input values (JSON)</summary>
    public string? DefaultInputJson { get; set; }

    /// <summary>Icon for UI</summary>
    public string? Icon { get; set; }

    /// <summary>Tags for searching</summary>
    public List<string> Tags { get; set; } = new();

    /// <summary>Is this template visible in UI</summary>
    public bool IsVisible { get; set; } = true;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }
}

public class TemplateParameter
{
    /// <summary>Parameter name (used in input JSON)</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Display label</summary>
    public string Label { get; set; } = string.Empty;

    /// <summary>Help text</summary>
    public string? Description { get; set; }

    /// <summary>Parameter type</summary>
    public ParameterType Type { get; set; } = ParameterType.String;

    /// <summary>Is this parameter required</summary>
    public bool Required { get; set; }

    /// <summary>Default value</summary>
    public object? DefaultValue { get; set; }

    /// <summary>Validation pattern (regex)</summary>
    public string? ValidationPattern { get; set; }

    /// <summary>Validation error message</summary>
    public string? ValidationMessage { get; set; }

    /// <summary>Allowed values (for Select type)</summary>
    public List<SelectOption>? Options { get; set; }

    /// <summary>Min value (for Number type)</summary>
    public decimal? Min { get; set; }

    /// <summary>Max value (for Number type)</summary>
    public decimal? Max { get; set; }

    /// <summary>Display order</summary>
    public int Order { get; set; }
}

public enum ParameterType
{
    String,
    Number,
    Boolean,
    Date,
    DateTime,
    Select,
    MultiSelect,
    Email,
    Url,
    Json,
    Cron,
    Secret
}

public class SelectOption
{
    public string Label { get; set; } = string.Empty;
    public object Value { get; set; } = string.Empty;
}
```

### Template Service Interface

```csharp
// ZapJobs.Core/Templates/ITemplateService.cs
public interface ITemplateService
{
    /// <summary>Get all templates</summary>
    Task<IReadOnlyList<JobTemplate>> GetAllTemplatesAsync(CancellationToken ct = default);

    /// <summary>Get templates by category</summary>
    Task<IReadOnlyList<JobTemplate>> GetTemplatesByCategoryAsync(
        string category,
        CancellationToken ct = default);

    /// <summary>Get a specific template</summary>
    Task<JobTemplate?> GetTemplateAsync(string templateId, CancellationToken ct = default);

    /// <summary>Create a new template</summary>
    Task<Guid> CreateTemplateAsync(JobTemplate template, CancellationToken ct = default);

    /// <summary>Update a template</summary>
    Task UpdateTemplateAsync(JobTemplate template, CancellationToken ct = default);

    /// <summary>Delete a template</summary>
    Task DeleteTemplateAsync(string templateId, CancellationToken ct = default);

    /// <summary>Create a job from template</summary>
    Task<JobDefinition> CreateJobFromTemplateAsync(
        string templateId,
        string jobTypeId,
        Dictionary<string, object>? parameters = null,
        CancellationToken ct = default);

    /// <summary>Trigger a job from template (one-time execution)</summary>
    Task<Guid> TriggerFromTemplateAsync(
        string templateId,
        Dictionary<string, object>? parameters = null,
        CancellationToken ct = default);

    /// <summary>Validate parameters against template</summary>
    Task<ValidationResult> ValidateParametersAsync(
        string templateId,
        Dictionary<string, object> parameters,
        CancellationToken ct = default);
}

public class ValidationResult
{
    public bool IsValid { get; set; }
    public List<ValidationError> Errors { get; set; } = new();
}

public class ValidationError
{
    public string ParameterName { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
}
```

### Implementação do Template Service

```csharp
// ZapJobs/Templates/TemplateService.cs
public class TemplateService : ITemplateService
{
    private readonly IJobStorage _storage;
    private readonly IJobScheduler _scheduler;
    private readonly ILogger<TemplateService> _logger;

    public async Task<JobDefinition> CreateJobFromTemplateAsync(
        string templateId,
        string jobTypeId,
        Dictionary<string, object>? parameters = null,
        CancellationToken ct = default)
    {
        var template = await GetTemplateAsync(templateId, ct);
        if (template == null)
            throw new ArgumentException($"Template '{templateId}' not found");

        // Validate parameters
        var validation = await ValidateParametersAsync(templateId, parameters ?? new(), ct);
        if (!validation.IsValid)
            throw new ValidationException(validation.Errors);

        // Merge default values with provided parameters
        var mergedInput = MergeParameters(template, parameters);

        var definition = new JobDefinition
        {
            JobTypeId = jobTypeId,
            DisplayName = ResolveDisplayName(template.DisplayName, parameters),
            Description = template.Description,
            Queue = template.Queue,
            ScheduleType = template.DefaultScheduleType,
            CronExpression = template.DefaultCronExpression,
            IntervalMinutes = template.DefaultIntervalMinutes,
            TimeoutSeconds = (int?)template.DefaultTimeout?.TotalSeconds,
            MaxRetries = template.DefaultRetryPolicy?.MaxRetries ?? 3,
            RetryDelaySeconds = template.DefaultRetryPolicy?.BaseDelaySeconds ?? 30,
            DefaultInputJson = JsonSerializer.Serialize(mergedInput),
            IsEnabled = true
        };

        await _storage.UpsertDefinitionAsync(definition, ct);

        _logger.LogInformation(
            "Created job {JobTypeId} from template {TemplateId}",
            jobTypeId, templateId);

        return definition;
    }

    public async Task<Guid> TriggerFromTemplateAsync(
        string templateId,
        Dictionary<string, object>? parameters = null,
        CancellationToken ct = default)
    {
        var template = await GetTemplateAsync(templateId, ct);
        if (template == null)
            throw new ArgumentException($"Template '{templateId}' not found");

        // Validate
        var validation = await ValidateParametersAsync(templateId, parameters ?? new(), ct);
        if (!validation.IsValid)
            throw new ValidationException(validation.Errors);

        // Merge parameters
        var mergedInput = MergeParameters(template, parameters);

        // Trigger job
        return await _scheduler.TriggerAsync(template.JobTypeId, mergedInput, ct);
    }

    public async Task<ValidationResult> ValidateParametersAsync(
        string templateId,
        Dictionary<string, object> parameters,
        CancellationToken ct = default)
    {
        var template = await GetTemplateAsync(templateId, ct);
        if (template == null)
        {
            return new ValidationResult
            {
                IsValid = false,
                Errors = new() { new() { Message = $"Template '{templateId}' not found" } }
            };
        }

        var errors = new List<ValidationError>();

        foreach (var param in template.Parameters)
        {
            var hasValue = parameters.TryGetValue(param.Name, out var value);

            // Required check
            if (param.Required && (!hasValue || value == null || string.IsNullOrEmpty(value.ToString())))
            {
                errors.Add(new ValidationError
                {
                    ParameterName = param.Name,
                    Message = $"{param.Label} is required"
                });
                continue;
            }

            if (!hasValue || value == null) continue;

            // Type validation
            var typeError = ValidateType(param, value);
            if (typeError != null)
            {
                errors.Add(typeError);
                continue;
            }

            // Pattern validation
            if (!string.IsNullOrEmpty(param.ValidationPattern))
            {
                var regex = new Regex(param.ValidationPattern);
                if (!regex.IsMatch(value.ToString()!))
                {
                    errors.Add(new ValidationError
                    {
                        ParameterName = param.Name,
                        Message = param.ValidationMessage ?? $"Invalid format for {param.Label}"
                    });
                }
            }

            // Range validation for numbers
            if (param.Type == ParameterType.Number && decimal.TryParse(value.ToString(), out var numValue))
            {
                if (param.Min.HasValue && numValue < param.Min.Value)
                {
                    errors.Add(new ValidationError
                    {
                        ParameterName = param.Name,
                        Message = $"{param.Label} must be at least {param.Min}"
                    });
                }
                if (param.Max.HasValue && numValue > param.Max.Value)
                {
                    errors.Add(new ValidationError
                    {
                        ParameterName = param.Name,
                        Message = $"{param.Label} must be at most {param.Max}"
                    });
                }
            }
        }

        return new ValidationResult
        {
            IsValid = errors.Count == 0,
            Errors = errors
        };
    }

    private Dictionary<string, object> MergeParameters(
        JobTemplate template,
        Dictionary<string, object>? provided)
    {
        var result = new Dictionary<string, object>();

        // Start with default input
        if (!string.IsNullOrEmpty(template.DefaultInputJson))
        {
            var defaults = JsonSerializer.Deserialize<Dictionary<string, object>>(template.DefaultInputJson);
            if (defaults != null)
            {
                foreach (var kvp in defaults)
                    result[kvp.Key] = kvp.Value;
            }
        }

        // Apply parameter defaults
        foreach (var param in template.Parameters)
        {
            if (param.DefaultValue != null && !result.ContainsKey(param.Name))
            {
                result[param.Name] = param.DefaultValue;
            }
        }

        // Override with provided values
        if (provided != null)
        {
            foreach (var kvp in provided)
                result[kvp.Key] = kvp.Value;
        }

        return result;
    }

    private string ResolveDisplayName(string displayName, Dictionary<string, object>? parameters)
    {
        if (parameters == null) return displayName;

        var result = displayName;
        foreach (var kvp in parameters)
        {
            result = result.Replace($"{{{{{kvp.Key}}}}}", kvp.Value?.ToString() ?? "");
        }
        return result;
    }
}
```

### Nova Tabela PostgreSQL

```sql
CREATE TABLE IF NOT EXISTS {prefix}templates (
    id UUID PRIMARY KEY,
    template_id VARCHAR(255) UNIQUE NOT NULL,
    display_name VARCHAR(255) NOT NULL,
    description TEXT,
    category VARCHAR(100),
    job_type_id VARCHAR(255) NOT NULL,
    queue VARCHAR(100) NOT NULL DEFAULT 'default',
    default_schedule_type INTEGER NOT NULL DEFAULT 0,
    default_cron_expression VARCHAR(100),
    default_interval_minutes INTEGER,
    default_retry_policy JSONB,
    default_timeout_seconds INTEGER,
    parameters JSONB NOT NULL DEFAULT '[]',
    default_input_json TEXT,
    icon VARCHAR(100),
    tags TEXT[] DEFAULT '{}',
    is_visible BOOLEAN NOT NULL DEFAULT TRUE,
    created_at TIMESTAMP NOT NULL DEFAULT NOW(),
    updated_at TIMESTAMP
);

CREATE INDEX IF NOT EXISTS idx_{prefix}templates_category ON {prefix}templates(category);
CREATE INDEX IF NOT EXISTS idx_{prefix}templates_job_type ON {prefix}templates(job_type_id);
```

### Registrar Templates via Fluent API

```csharp
// ZapJobs/Configuration/TemplateBuilder.cs
public class TemplateBuilder
{
    private readonly JobTemplate _template = new();

    public TemplateBuilder WithId(string templateId)
    {
        _template.TemplateId = templateId;
        return this;
    }

    public TemplateBuilder WithName(string displayName)
    {
        _template.DisplayName = displayName;
        return this;
    }

    public TemplateBuilder WithDescription(string description)
    {
        _template.Description = description;
        return this;
    }

    public TemplateBuilder InCategory(string category)
    {
        _template.Category = category;
        return this;
    }

    public TemplateBuilder ForJobType(string jobTypeId)
    {
        _template.JobTypeId = jobTypeId;
        return this;
    }

    public TemplateBuilder ForJobType<TJob>() where TJob : IJob, new()
    {
        _template.JobTypeId = new TJob().JobTypeId;
        return this;
    }

    public TemplateBuilder WithQueue(string queue)
    {
        _template.Queue = queue;
        return this;
    }

    public TemplateBuilder WithCron(string cronExpression)
    {
        _template.DefaultScheduleType = ScheduleType.Cron;
        _template.DefaultCronExpression = cronExpression;
        return this;
    }

    public TemplateBuilder WithParameter(Action<ParameterBuilder> configure)
    {
        var builder = new ParameterBuilder();
        configure(builder);
        _template.Parameters.Add(builder.Build());
        return this;
    }

    public TemplateBuilder WithIcon(string icon)
    {
        _template.Icon = icon;
        return this;
    }

    public TemplateBuilder WithTags(params string[] tags)
    {
        _template.Tags.AddRange(tags);
        return this;
    }

    internal JobTemplate Build() => _template;
}

public class ParameterBuilder
{
    private readonly TemplateParameter _param = new();

    public ParameterBuilder Name(string name)
    {
        _param.Name = name;
        return this;
    }

    public ParameterBuilder Label(string label)
    {
        _param.Label = label;
        return this;
    }

    public ParameterBuilder Description(string description)
    {
        _param.Description = description;
        return this;
    }

    public ParameterBuilder Type(ParameterType type)
    {
        _param.Type = type;
        return this;
    }

    public ParameterBuilder Required(bool required = true)
    {
        _param.Required = required;
        return this;
    }

    public ParameterBuilder Default(object value)
    {
        _param.DefaultValue = value;
        return this;
    }

    public ParameterBuilder Pattern(string pattern, string? message = null)
    {
        _param.ValidationPattern = pattern;
        _param.ValidationMessage = message;
        return this;
    }

    public ParameterBuilder Options(params (string Label, object Value)[] options)
    {
        _param.Type = ParameterType.Select;
        _param.Options = options.Select(o => new SelectOption { Label = o.Label, Value = o.Value }).ToList();
        return this;
    }

    public ParameterBuilder Range(decimal? min = null, decimal? max = null)
    {
        _param.Min = min;
        _param.Max = max;
        return this;
    }

    internal TemplateParameter Build() => _param;
}

// ServiceCollectionExtensions
public static class ZapJobsTemplateExtensions
{
    public static ZapJobsBuilder AddTemplate(
        this ZapJobsBuilder builder,
        string templateId,
        Action<TemplateBuilder> configure)
    {
        var templateBuilder = new TemplateBuilder().WithId(templateId);
        configure(templateBuilder);
        builder.Templates.Add(templateBuilder.Build());
        return builder;
    }
}
```

## Exemplo de Uso

```csharp
// Configure templates
builder.Services.AddZapJobs()
    .AddTemplate("email-notification", t => t
        .WithName("Email Notification")
        .WithDescription("Send an email notification")
        .InCategory("Notifications")
        .ForJobType<SendEmailJob>()
        .WithParameter(p => p
            .Name("recipient")
            .Label("Recipient Email")
            .Type(ParameterType.Email)
            .Required())
        .WithParameter(p => p
            .Name("subject")
            .Label("Subject")
            .Type(ParameterType.String)
            .Required())
        .WithParameter(p => p
            .Name("template")
            .Label("Email Template")
            .Type(ParameterType.Select)
            .Options(
                ("Welcome", "welcome"),
                ("Password Reset", "password-reset"),
                ("Order Confirmation", "order-confirm")))
        .WithParameter(p => p
            .Name("priority")
            .Label("Priority")
            .Type(ParameterType.Select)
            .Default("normal")
            .Options(
                ("Low", "low"),
                ("Normal", "normal"),
                ("High", "high")))
        .WithIcon("envelope")
        .WithTags("email", "notification"))

    .AddTemplate("scheduled-report", t => t
        .WithName("Scheduled Report - {{reportName}}")
        .WithDescription("Generate and send a scheduled report")
        .InCategory("Reports")
        .ForJobType<GenerateReportJob>()
        .WithCron("0 8 * * 1-5") // Weekdays at 8 AM
        .WithParameter(p => p
            .Name("reportName")
            .Label("Report Name")
            .Required())
        .WithParameter(p => p
            .Name("format")
            .Label("Output Format")
            .Type(ParameterType.Select)
            .Default("pdf")
            .Options(
                ("PDF", "pdf"),
                ("Excel", "xlsx"),
                ("CSV", "csv")))
        .WithParameter(p => p
            .Name("recipients")
            .Label("Email Recipients")
            .Type(ParameterType.String)
            .Description("Comma-separated email addresses"))
        .WithIcon("chart-bar")
        .WithTags("report", "scheduled"));

// API to create job from template
app.MapPost("/api/templates/{templateId}/create-job", async (
    string templateId,
    CreateJobFromTemplateRequest request,
    ITemplateService templates) =>
{
    var job = await templates.CreateJobFromTemplateAsync(
        templateId,
        request.JobTypeId,
        request.Parameters);

    return Results.Created($"/api/jobs/{job.JobTypeId}", job);
});

// API to trigger job from template
app.MapPost("/api/templates/{templateId}/trigger", async (
    string templateId,
    TriggerFromTemplateRequest request,
    ITemplateService templates) =>
{
    var runId = await templates.TriggerFromTemplateAsync(
        templateId,
        request.Parameters);

    return Results.Accepted(new { runId });
});

// Dashboard UI would render form based on template parameters
```

## Arquivos a Criar/Modificar

1. `ZapJobs.Core/Templates/JobTemplate.cs`
2. `ZapJobs.Core/Templates/TemplateParameter.cs`
3. `ZapJobs.Core/Templates/ITemplateService.cs`
4. `ZapJobs/Templates/TemplateService.cs`
5. `ZapJobs/Configuration/TemplateBuilder.cs`
6. `ZapJobs/Configuration/ZapJobsTemplateExtensions.cs`
7. `ZapJobs.Storage.PostgreSQL/PostgreSqlJobStorage.cs` - Métodos de template
8. `ZapJobs.Storage.PostgreSQL/MigrationRunner.cs` - Nova tabela
9. `ZapJobs.AspNetCore/Api/TemplatesController.cs`
10. `ZapJobs.AspNetCore/Dashboard/` - UI para templates

## Critérios de Aceitação

1. [ ] Templates podem ser criados via Fluent API
2. [ ] Templates podem ser criados via API/storage
3. [ ] Validação de parâmetros funciona
4. [ ] Jobs criados a partir de templates herdam configurações
5. [ ] Trigger de template executa com parâmetros mesclados
6. [ ] Dashboard lista templates disponíveis
7. [ ] Dashboard permite criar job a partir de template
8. [ ] Dashboard renderiza form dinâmico para parâmetros
9. [ ] Categorias organizam templates
10. [ ] Testes para validação e criação
