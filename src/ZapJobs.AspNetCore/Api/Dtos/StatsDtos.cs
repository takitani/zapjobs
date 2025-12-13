namespace ZapJobs.AspNetCore.Api.Dtos;

/// <summary>
/// DTO representing job statistics
/// </summary>
public record StatsDto
{
    /// <summary>Total number of job definitions</summary>
    public required int TotalJobs { get; init; }

    /// <summary>Number of enabled job definitions</summary>
    public required int EnabledJobs { get; init; }

    /// <summary>Number of pending runs</summary>
    public required int PendingRuns { get; init; }

    /// <summary>Number of currently running jobs</summary>
    public required int RunningRuns { get; init; }

    /// <summary>Number of jobs completed today</summary>
    public required int CompletedToday { get; init; }

    /// <summary>Number of jobs that failed today</summary>
    public required int FailedToday { get; init; }

    /// <summary>Number of active workers</summary>
    public required int ActiveWorkers { get; init; }

    /// <summary>Total number of job runs</summary>
    public required int TotalRuns { get; init; }

    /// <summary>Total number of log entries</summary>
    public required long TotalLogEntries { get; init; }
}
