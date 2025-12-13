# Prompt: Implementar Calendar Exclusions

## Objetivo

Adicionar suporte a exclusão de datas e períodos para jobs agendados. Permitir que jobs CRON não executem em feriados, manutenções programadas ou períodos específicos.

## Contexto

Calendar exclusions são necessários para:
- Não executar jobs em feriados
- Pausar jobs durante janelas de manutenção
- Evitar execuções em períodos de freeze (ex: fim de ano)
- Respeitar horário comercial

## Requisitos

### Novas Entidades

```csharp
// ZapJobs.Core/Calendar/CalendarExclusion.cs
public class CalendarExclusion
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Name of the exclusion</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Optional description</summary>
    public string? Description { get; set; }

    /// <summary>Type of exclusion</summary>
    public ExclusionType Type { get; set; }

    /// <summary>When the exclusion starts (for DateRange type)</summary>
    public DateTime? StartDate { get; set; }

    /// <summary>When the exclusion ends (for DateRange type)</summary>
    public DateTime? EndDate { get; set; }

    /// <summary>Specific date (for SingleDate type)</summary>
    public DateOnly? Date { get; set; }

    /// <summary>CRON expression for recurring exclusions (for Cron type)</summary>
    public string? CronExpression { get; set; }

    /// <summary>Days of week to exclude (for DayOfWeek type)</summary>
    public DayOfWeek[]? ExcludedDays { get; set; }

    /// <summary>Time range start (for time-based exclusions)</summary>
    public TimeOnly? TimeStart { get; set; }

    /// <summary>Time range end (for time-based exclusions)</summary>
    public TimeOnly? TimeEnd { get; set; }

    /// <summary>Timezone for time-based calculations</summary>
    public string? TimeZone { get; set; }

    /// <summary>Whether this exclusion is active</summary>
    public bool IsEnabled { get; set; } = true;

    /// <summary>When this exclusion was created</summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public enum ExclusionType
{
    /// <summary>Single specific date</summary>
    SingleDate,

    /// <summary>Date range (from start to end)</summary>
    DateRange,

    /// <summary>Recurring pattern via CRON</summary>
    Cron,

    /// <summary>Specific days of week</summary>
    DayOfWeek,

    /// <summary>Time window (e.g., only business hours)</summary>
    TimeWindow
}

// Link between exclusions and jobs/calendars
public class JobCalendarLink
{
    public string JobTypeId { get; set; } = string.Empty;
    public Guid CalendarExclusionId { get; set; }
}
```

### Calendar Service Interface

```csharp
// ZapJobs.Core/Calendar/ICalendarService.cs
public interface ICalendarService
{
    /// <summary>Check if a datetime is excluded for a job</summary>
    Task<bool> IsExcludedAsync(string jobTypeId, DateTime dateTime, CancellationToken ct = default);

    /// <summary>Get next valid execution time (skipping exclusions)</summary>
    Task<DateTime?> GetNextValidTimeAsync(
        string jobTypeId,
        DateTime afterTime,
        string cronExpression,
        CancellationToken ct = default);

    /// <summary>Get all exclusions for a job</summary>
    Task<IReadOnlyList<CalendarExclusion>> GetExclusionsForJobAsync(
        string jobTypeId,
        CancellationToken ct = default);

    /// <summary>Create a new calendar exclusion</summary>
    Task<Guid> CreateExclusionAsync(CalendarExclusion exclusion, CancellationToken ct = default);

    /// <summary>Link an exclusion to a job</summary>
    Task LinkExclusionToJobAsync(Guid exclusionId, string jobTypeId, CancellationToken ct = default);

    /// <summary>Unlink an exclusion from a job</summary>
    Task UnlinkExclusionFromJobAsync(Guid exclusionId, string jobTypeId, CancellationToken ct = default);

    /// <summary>Get all calendar exclusions</summary>
    Task<IReadOnlyList<CalendarExclusion>> GetAllExclusionsAsync(CancellationToken ct = default);

    /// <summary>Update an exclusion</summary>
    Task UpdateExclusionAsync(CalendarExclusion exclusion, CancellationToken ct = default);

    /// <summary>Delete an exclusion</summary>
    Task DeleteExclusionAsync(Guid id, CancellationToken ct = default);
}
```

### Implementação do Calendar Service

```csharp
// ZapJobs/Calendar/CalendarService.cs
public class CalendarService : ICalendarService
{
    private readonly IJobStorage _storage;
    private readonly ILogger<CalendarService> _logger;

    public async Task<bool> IsExcludedAsync(
        string jobTypeId,
        DateTime dateTime,
        CancellationToken ct = default)
    {
        var exclusions = await GetExclusionsForJobAsync(jobTypeId, ct);

        foreach (var exclusion in exclusions.Where(e => e.IsEnabled))
        {
            if (IsDateTimeExcluded(exclusion, dateTime))
            {
                _logger.LogDebug(
                    "DateTime {DateTime} excluded for job {JobTypeId} by exclusion {ExclusionName}",
                    dateTime, jobTypeId, exclusion.Name);
                return true;
            }
        }

        return false;
    }

    public async Task<DateTime?> GetNextValidTimeAsync(
        string jobTypeId,
        DateTime afterTime,
        string cronExpression,
        CancellationToken ct = default)
    {
        var cron = Cronos.CronExpression.Parse(cronExpression);
        var exclusions = await GetExclusionsForJobAsync(jobTypeId, ct);
        var enabledExclusions = exclusions.Where(e => e.IsEnabled).ToList();

        var candidate = afterTime;
        var maxAttempts = 1000; // Prevent infinite loops

        for (var i = 0; i < maxAttempts; i++)
        {
            var next = cron.GetNextOccurrence(candidate, TimeZoneInfo.Utc);

            if (!next.HasValue)
                return null;

            var isExcluded = enabledExclusions.Any(e => IsDateTimeExcluded(e, next.Value));

            if (!isExcluded)
                return next.Value;

            candidate = next.Value;
        }

        _logger.LogWarning(
            "Could not find valid execution time for job {JobTypeId} after {MaxAttempts} attempts",
            jobTypeId, maxAttempts);

        return null;
    }

    private bool IsDateTimeExcluded(CalendarExclusion exclusion, DateTime dateTime)
    {
        // Convert to exclusion's timezone if specified
        var localDateTime = dateTime;
        if (!string.IsNullOrEmpty(exclusion.TimeZone))
        {
            var tz = TimeZoneInfo.FindSystemTimeZoneById(exclusion.TimeZone);
            localDateTime = TimeZoneInfo.ConvertTimeFromUtc(dateTime, tz);
        }

        return exclusion.Type switch
        {
            ExclusionType.SingleDate =>
                exclusion.Date.HasValue &&
                DateOnly.FromDateTime(localDateTime) == exclusion.Date.Value,

            ExclusionType.DateRange =>
                exclusion.StartDate.HasValue &&
                exclusion.EndDate.HasValue &&
                localDateTime >= exclusion.StartDate.Value &&
                localDateTime <= exclusion.EndDate.Value,

            ExclusionType.Cron =>
                !string.IsNullOrEmpty(exclusion.CronExpression) &&
                IsCronMatch(exclusion.CronExpression, localDateTime),

            ExclusionType.DayOfWeek =>
                exclusion.ExcludedDays?.Contains(localDateTime.DayOfWeek) == true,

            ExclusionType.TimeWindow =>
                IsOutsideTimeWindow(exclusion, localDateTime),

            _ => false
        };
    }

    private bool IsCronMatch(string cronExpression, DateTime dateTime)
    {
        var cron = Cronos.CronExpression.Parse(cronExpression);
        var start = dateTime.AddMinutes(-1);
        var next = cron.GetNextOccurrence(start, TimeZoneInfo.Utc);

        return next.HasValue &&
               next.Value.Date == dateTime.Date &&
               next.Value.Hour == dateTime.Hour &&
               next.Value.Minute == dateTime.Minute;
    }

    private bool IsOutsideTimeWindow(CalendarExclusion exclusion, DateTime dateTime)
    {
        if (!exclusion.TimeStart.HasValue || !exclusion.TimeEnd.HasValue)
            return false;

        var time = TimeOnly.FromDateTime(dateTime);

        // If window spans midnight (e.g., 22:00 - 06:00)
        if (exclusion.TimeStart.Value > exclusion.TimeEnd.Value)
        {
            return time < exclusion.TimeStart.Value && time >= exclusion.TimeEnd.Value;
        }

        // Normal window (e.g., 09:00 - 18:00 means exclude outside business hours)
        return time < exclusion.TimeStart.Value || time >= exclusion.TimeEnd.Value;
    }
}
```

### Atualizar Job Scheduler

```csharp
// Modificar ScheduledJobProcessor para usar CalendarService
public class ScheduledJobProcessor
{
    private readonly ICalendarService _calendarService;

    protected override async Task ProcessScheduledJobsAsync(CancellationToken ct)
    {
        var definitions = await _storage.GetScheduledJobDefinitionsAsync(ct);
        var now = DateTime.UtcNow;

        foreach (var definition in definitions.Where(d => d.IsEnabled))
        {
            // Check if current time is excluded
            if (await _calendarService.IsExcludedAsync(definition.JobTypeId, now, ct))
            {
                _logger.LogDebug(
                    "Skipping job {JobTypeId} at {Now} due to calendar exclusion",
                    definition.JobTypeId, now);
                continue;
            }

            // Calculate next run with exclusions
            if (!string.IsNullOrEmpty(definition.CronExpression))
            {
                var nextRun = await _calendarService.GetNextValidTimeAsync(
                    definition.JobTypeId,
                    now,
                    definition.CronExpression,
                    ct);

                if (nextRun.HasValue)
                {
                    definition.NextRunAt = nextRun.Value;
                    await _storage.UpdateDefinitionAsync(definition, ct);
                }
            }

            // ... rest of scheduling logic
        }
    }
}
```

### Nova Tabela PostgreSQL

```sql
-- Calendar exclusions table
CREATE TABLE IF NOT EXISTS {prefix}calendar_exclusions (
    id UUID PRIMARY KEY,
    name VARCHAR(255) NOT NULL,
    description TEXT,
    type INTEGER NOT NULL,
    start_date TIMESTAMP,
    end_date TIMESTAMP,
    date DATE,
    cron_expression VARCHAR(100),
    excluded_days INTEGER[], -- PostgreSQL array of day numbers (0=Sunday)
    time_start TIME,
    time_end TIME,
    time_zone VARCHAR(100),
    is_enabled BOOLEAN NOT NULL DEFAULT TRUE,
    created_at TIMESTAMP NOT NULL DEFAULT NOW()
);

-- Link table for jobs and exclusions
CREATE TABLE IF NOT EXISTS {prefix}job_calendar_links (
    job_type_id VARCHAR(255) NOT NULL,
    calendar_exclusion_id UUID NOT NULL REFERENCES {prefix}calendar_exclusions(id) ON DELETE CASCADE,
    PRIMARY KEY (job_type_id, calendar_exclusion_id)
);

-- Index for quick lookups
CREATE INDEX IF NOT EXISTS idx_{prefix}job_calendar_links_job
    ON {prefix}job_calendar_links(job_type_id);
```

### Fluent API para Configuração

```csharp
// ZapJobs/Configuration/JobOptionsBuilder.cs
public class JobOptionsBuilder
{
    // ... existing ...

    /// <summary>Exclude specific dates</summary>
    public JobOptionsBuilder ExcludeDates(params DateOnly[] dates)
    {
        foreach (var date in dates)
        {
            _exclusions.Add(new CalendarExclusion
            {
                Name = $"Exclude {date}",
                Type = ExclusionType.SingleDate,
                Date = date
            });
        }
        return this;
    }

    /// <summary>Exclude a date range</summary>
    public JobOptionsBuilder ExcludeDateRange(DateTime start, DateTime end, string? name = null)
    {
        _exclusions.Add(new CalendarExclusion
        {
            Name = name ?? $"Exclude {start:d} to {end:d}",
            Type = ExclusionType.DateRange,
            StartDate = start,
            EndDate = end
        });
        return this;
    }

    /// <summary>Exclude specific days of week</summary>
    public JobOptionsBuilder ExcludeDaysOfWeek(params DayOfWeek[] days)
    {
        _exclusions.Add(new CalendarExclusion
        {
            Name = $"Exclude {string.Join(", ", days)}",
            Type = ExclusionType.DayOfWeek,
            ExcludedDays = days
        });
        return this;
    }

    /// <summary>Only run during business hours</summary>
    public JobOptionsBuilder OnlyDuringBusinessHours(
        TimeOnly start = default,
        TimeOnly end = default,
        string? timeZone = null)
    {
        _exclusions.Add(new CalendarExclusion
        {
            Name = "Business hours only",
            Type = ExclusionType.TimeWindow,
            TimeStart = start == default ? new TimeOnly(9, 0) : start,
            TimeEnd = end == default ? new TimeOnly(18, 0) : end,
            TimeZone = timeZone
        });
        return this;
    }

    /// <summary>Use a named calendar</summary>
    public JobOptionsBuilder UseCalendar(string calendarName)
    {
        _calendarNames.Add(calendarName);
        return this;
    }
}

// Uso
builder.Services.AddZapJobs()
    .AddJob<ReportJob>(opts => opts
        .WithCron("0 9 * * *")
        .ExcludeDaysOfWeek(DayOfWeek.Saturday, DayOfWeek.Sunday)
        .ExcludeDates(
            new DateOnly(2024, 12, 25), // Christmas
            new DateOnly(2024, 1, 1)    // New Year
        )
        .OnlyDuringBusinessHours(
            new TimeOnly(8, 0),
            new TimeOnly(20, 0),
            "America/Sao_Paulo"
        ));
```

### Calendários Pré-definidos

```csharp
// ZapJobs/Calendar/Calendars.cs
public static class Calendars
{
    /// <summary>Brazilian holidays</summary>
    public static IEnumerable<CalendarExclusion> BrazilHolidays(int year)
    {
        yield return new CalendarExclusion
        {
            Name = "Confraternização Universal",
            Type = ExclusionType.SingleDate,
            Date = new DateOnly(year, 1, 1)
        };
        yield return new CalendarExclusion
        {
            Name = "Tiradentes",
            Type = ExclusionType.SingleDate,
            Date = new DateOnly(year, 4, 21)
        };
        yield return new CalendarExclusion
        {
            Name = "Dia do Trabalho",
            Type = ExclusionType.SingleDate,
            Date = new DateOnly(year, 5, 1)
        };
        yield return new CalendarExclusion
        {
            Name = "Independência",
            Type = ExclusionType.SingleDate,
            Date = new DateOnly(year, 9, 7)
        };
        yield return new CalendarExclusion
        {
            Name = "Nossa Senhora Aparecida",
            Type = ExclusionType.SingleDate,
            Date = new DateOnly(year, 10, 12)
        };
        yield return new CalendarExclusion
        {
            Name = "Finados",
            Type = ExclusionType.SingleDate,
            Date = new DateOnly(year, 11, 2)
        };
        yield return new CalendarExclusion
        {
            Name = "Proclamação da República",
            Type = ExclusionType.SingleDate,
            Date = new DateOnly(year, 11, 15)
        };
        yield return new CalendarExclusion
        {
            Name = "Natal",
            Type = ExclusionType.SingleDate,
            Date = new DateOnly(year, 12, 25)
        };
        // Easter-based holidays would need calculation
    }

    /// <summary>Weekends only</summary>
    public static CalendarExclusion Weekends => new()
    {
        Name = "Weekends",
        Type = ExclusionType.DayOfWeek,
        ExcludedDays = new[] { DayOfWeek.Saturday, DayOfWeek.Sunday }
    };

    /// <summary>Business hours (9-18)</summary>
    public static CalendarExclusion BusinessHoursOnly => new()
    {
        Name = "Business Hours Only",
        Type = ExclusionType.TimeWindow,
        TimeStart = new TimeOnly(9, 0),
        TimeEnd = new TimeOnly(18, 0)
    };
}

// Uso
builder.Services.AddZapJobs()
    .AddCalendar("brazil-holidays", Calendars.BrazilHolidays(2024))
    .AddCalendar("weekends", new[] { Calendars.Weekends })
    .AddJob<ReportJob>(opts => opts
        .UseCalendar("brazil-holidays")
        .UseCalendar("weekends"));
```

## Exemplo de Uso

```csharp
// Configure calendars
builder.Services.AddZapJobs()
    // Define global calendars
    .AddCalendar("maintenance-window", new[]
    {
        new CalendarExclusion
        {
            Name = "Weekly Maintenance",
            Type = ExclusionType.Cron,
            CronExpression = "0 2 * * 0" // Sundays at 2 AM
        }
    })

    // Job-specific exclusions
    .AddJob<DailyReportJob>(opts => opts
        .WithCron("0 8 * * 1-5") // Weekdays at 8 AM
        .ExcludeDates(
            new DateOnly(2024, 12, 25),
            new DateOnly(2024, 12, 31)
        ))

    .AddJob<HourlySync>(opts => opts
        .WithCron("0 * * * *")
        .UseCalendar("maintenance-window")
        .OnlyDuringBusinessHours());

// Runtime calendar management
app.MapPost("/api/admin/calendars/freeze", async (
    ICalendarService calendar,
    FreezeRequest request) =>
{
    var exclusion = new CalendarExclusion
    {
        Name = request.Reason,
        Type = ExclusionType.DateRange,
        StartDate = request.Start,
        EndDate = request.End
    };

    var id = await calendar.CreateExclusionAsync(exclusion);

    // Link to all jobs or specific ones
    foreach (var jobTypeId in request.JobTypeIds)
    {
        await calendar.LinkExclusionToJobAsync(id, jobTypeId);
    }

    return Results.Ok(new { id });
});
```

## Arquivos a Criar/Modificar

1. `ZapJobs.Core/Calendar/CalendarExclusion.cs`
2. `ZapJobs.Core/Calendar/ICalendarService.cs`
3. `ZapJobs.Core/Calendar/ExclusionType.cs`
4. `ZapJobs/Calendar/CalendarService.cs`
5. `ZapJobs/Calendar/Calendars.cs` - Calendários pré-definidos
6. `ZapJobs.Storage.PostgreSQL/PostgreSqlJobStorage.cs` - Métodos de calendar
7. `ZapJobs.Storage.PostgreSQL/MigrationRunner.cs` - Nova tabela
8. `ZapJobs/ScheduledJobProcessor.cs` - Integrar calendar
9. `ZapJobs/Configuration/JobOptionsBuilder.cs` - Fluent API

## Critérios de Aceitação

1. [ ] Exclusão por data única funciona
2. [ ] Exclusão por range de datas funciona
3. [ ] Exclusão por dia da semana funciona
4. [ ] Exclusão por janela de tempo funciona
5. [ ] Exclusões CRON funcionam
6. [ ] GetNextValidTime pula datas excluídas
7. [ ] Dashboard mostra exclusões do job
8. [ ] API para gerenciar exclusões
9. [ ] Calendários pré-definidos disponíveis
10. [ ] Testes para todos os tipos de exclusão
