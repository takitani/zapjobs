namespace ZapJobs.AspNetCore.HealthChecks;

/// <summary>
/// Configuration options for ZapJobs health checks
/// </summary>
public class ZapJobsHealthOptions
{
    /// <summary>
    /// Require at least one active worker for healthy status.
    /// Default: true
    /// </summary>
    public bool RequireActiveWorker { get; set; } = true;

    /// <summary>
    /// Minimum number of workers required for healthy status.
    /// Default: 1
    /// </summary>
    public int MinimumWorkers { get; set; } = 1;

    /// <summary>
    /// Pending jobs threshold for degraded status.
    /// Default: 100
    /// </summary>
    public int MaxPendingJobsDegraded { get; set; } = 100;

    /// <summary>
    /// Pending jobs threshold for unhealthy status.
    /// Default: 1000
    /// </summary>
    public int MaxPendingJobsUnhealthy { get; set; } = 1000;

    /// <summary>
    /// Dead letter entries threshold for degraded status.
    /// Default: 10
    /// </summary>
    public int MaxDeadLetterDegraded { get; set; } = 10;

    /// <summary>
    /// Dead letter entries threshold for unhealthy status.
    /// Default: 100
    /// </summary>
    public int MaxDeadLetterUnhealthy { get; set; } = 100;

    /// <summary>
    /// Time threshold for considering a worker active.
    /// Default: 2 minutes
    /// </summary>
    public TimeSpan ActiveWorkerThreshold { get; set; } = TimeSpan.FromMinutes(2);
}
