using System.Diagnostics.Metrics;

namespace ZapJobs.OpenTelemetry;

/// <summary>
/// OpenTelemetry metrics for ZapJobs
/// </summary>
public static class ZapJobsMetrics
{
    private static readonly Meter Meter = new(
        ZapJobsInstrumentation.MeterName,
        ZapJobsInstrumentation.Version);

    /// <summary>
    /// Counter for total jobs processed (includes both succeeded and failed)
    /// </summary>
    public static readonly Counter<long> JobsProcessed = Meter.CreateCounter<long>(
        name: "zapjobs.jobs.processed",
        unit: "{job}",
        description: "Number of jobs processed");

    /// <summary>
    /// Counter for jobs that completed successfully
    /// </summary>
    public static readonly Counter<long> JobsSucceeded = Meter.CreateCounter<long>(
        name: "zapjobs.jobs.succeeded",
        unit: "{job}",
        description: "Number of jobs completed successfully");

    /// <summary>
    /// Counter for jobs that failed (final failure, not retries)
    /// </summary>
    public static readonly Counter<long> JobsFailed = Meter.CreateCounter<long>(
        name: "zapjobs.jobs.failed",
        unit: "{job}",
        description: "Number of jobs that failed permanently");

    /// <summary>
    /// Counter for jobs that were retried
    /// </summary>
    public static readonly Counter<long> JobsRetried = Meter.CreateCounter<long>(
        name: "zapjobs.jobs.retried",
        unit: "{job}",
        description: "Number of job retry attempts");

    /// <summary>
    /// Histogram for job execution duration
    /// </summary>
    public static readonly Histogram<double> JobDuration = Meter.CreateHistogram<double>(
        name: "zapjobs.jobs.duration",
        unit: "ms",
        description: "Job execution duration in milliseconds");

    /// <summary>
    /// UpDownCounter for currently running jobs
    /// </summary>
    public static readonly UpDownCounter<int> JobsRunning = Meter.CreateUpDownCounter<int>(
        name: "zapjobs.jobs.running",
        unit: "{job}",
        description: "Number of jobs currently running");

    /// <summary>
    /// Counter for jobs scheduled (enqueued)
    /// </summary>
    public static readonly Counter<long> JobsScheduled = Meter.CreateCounter<long>(
        name: "zapjobs.jobs.scheduled",
        unit: "{job}",
        description: "Number of jobs scheduled/enqueued");

    /// <summary>
    /// Counter for jobs moved to dead letter queue
    /// </summary>
    public static readonly Counter<long> JobsDeadLettered = Meter.CreateCounter<long>(
        name: "zapjobs.jobs.dead_lettered",
        unit: "{job}",
        description: "Number of jobs moved to dead letter queue");

    /// <summary>
    /// Creates a KeyValuePair tag for job type
    /// </summary>
    public static KeyValuePair<string, object?> JobTypeTag(string jobTypeId)
        => new("job.type_id", jobTypeId);

    /// <summary>
    /// Creates a KeyValuePair tag for queue
    /// </summary>
    public static KeyValuePair<string, object?> QueueTag(string queue)
        => new("queue", queue);

    /// <summary>
    /// Creates a KeyValuePair tag for status
    /// </summary>
    public static KeyValuePair<string, object?> StatusTag(string status)
        => new("status", status);
}
