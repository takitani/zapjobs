using ZapJobs.Core;

namespace ZapJobs.AspNetCore.Api.Dtos;

/// <summary>
/// DTO representing a worker
/// </summary>
public record WorkerDto
{
    /// <summary>Worker instance ID</summary>
    public required string WorkerId { get; init; }

    /// <summary>Machine hostname</summary>
    public required string Hostname { get; init; }

    /// <summary>Process ID</summary>
    public required int ProcessId { get; init; }

    /// <summary>Job type currently being processed</summary>
    public string? CurrentJobType { get; init; }

    /// <summary>Run ID currently being processed</summary>
    public Guid? CurrentRunId { get; init; }

    /// <summary>Last heartbeat timestamp</summary>
    public required DateTime LastHeartbeat { get; init; }

    /// <summary>Whether the worker is healthy (heartbeat within threshold)</summary>
    public required bool IsHealthy { get; init; }

    /// <summary>Queues this worker is listening to</summary>
    public required string[] Queues { get; init; }

    /// <summary>Number of jobs processed since worker started</summary>
    public required int JobsProcessed { get; init; }

    /// <summary>Number of jobs that failed since worker started</summary>
    public required int JobsFailed { get; init; }

    /// <summary>Whether the worker is shutting down</summary>
    public required bool IsShuttingDown { get; init; }

    /// <summary>When this worker started</summary>
    public required DateTime StartedAt { get; init; }

    public static WorkerDto FromEntity(JobHeartbeat h, TimeSpan staleThreshold) => new()
    {
        WorkerId = h.WorkerId,
        Hostname = h.Hostname,
        ProcessId = h.ProcessId,
        CurrentJobType = h.CurrentJobType,
        CurrentRunId = h.CurrentRunId,
        LastHeartbeat = h.Timestamp,
        IsHealthy = DateTime.UtcNow - h.Timestamp < staleThreshold && !h.IsShuttingDown,
        Queues = h.Queues,
        JobsProcessed = h.JobsProcessed,
        JobsFailed = h.JobsFailed,
        IsShuttingDown = h.IsShuttingDown,
        StartedAt = h.StartedAt
    };
}
