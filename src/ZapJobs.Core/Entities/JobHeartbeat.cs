namespace ZapJobs.Core;

/// <summary>
/// Health check heartbeat from a worker
/// </summary>
public class JobHeartbeat
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Worker instance ID (e.g., "hostname-guid")</summary>
    public string WorkerId { get; set; } = string.Empty;

    /// <summary>Machine hostname</summary>
    public string Hostname { get; set; } = string.Empty;

    /// <summary>Process ID</summary>
    public int ProcessId { get; set; }

    /// <summary>Job type currently being processed (null if idle)</summary>
    public string? CurrentJobType { get; set; }

    /// <summary>Run ID currently being processed (null if idle)</summary>
    public Guid? CurrentRunId { get; set; }

    /// <summary>Last heartbeat timestamp</summary>
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    /// <summary>Queues this worker is listening to</summary>
    public string[] Queues { get; set; } = ["default"];

    /// <summary>Number of jobs processed since worker started</summary>
    public int JobsProcessed { get; set; }

    /// <summary>Number of jobs that failed since worker started</summary>
    public int JobsFailed { get; set; }

    /// <summary>Whether the worker is shutting down</summary>
    public bool IsShuttingDown { get; set; }

    /// <summary>When this worker started</summary>
    public DateTime StartedAt { get; set; } = DateTime.UtcNow;
}
