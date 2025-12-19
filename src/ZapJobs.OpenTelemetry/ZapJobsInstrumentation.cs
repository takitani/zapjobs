using System.Diagnostics;
using System.Reflection;

namespace ZapJobs.OpenTelemetry;

/// <summary>
/// OpenTelemetry instrumentation for ZapJobs
/// </summary>
public static class ZapJobsInstrumentation
{
    /// <summary>
    /// ActivitySource name for ZapJobs
    /// </summary>
    public const string ActivitySourceName = "ZapJobs";

    /// <summary>
    /// Instrumentation version (from assembly version)
    /// </summary>
    public static readonly string Version = typeof(ZapJobsInstrumentation).Assembly
        .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? typeof(ZapJobsInstrumentation).Assembly.GetName().Version?.ToString()
        ?? "1.0.0";

    /// <summary>
    /// Meter name for ZapJobs metrics
    /// </summary>
    public const string MeterName = "ZapJobs";

    /// <summary>
    /// ActivitySource for creating spans
    /// </summary>
    internal static readonly ActivitySource ActivitySource = new(ActivitySourceName, Version);

    /// <summary>
    /// Semantic convention tags for ZapJobs spans
    /// </summary>
    public static class Tags
    {
        /// <summary>Job type identifier (e.g., "send-email")</summary>
        public const string JobTypeId = "zapjobs.job.type_id";

        /// <summary>Unique job run ID (GUID)</summary>
        public const string JobRunId = "zapjobs.job.run_id";

        /// <summary>Queue name</summary>
        public const string JobQueue = "zapjobs.job.queue";

        /// <summary>How the job was triggered (manual, scheduled, recurring, continuation)</summary>
        public const string JobTriggerType = "zapjobs.job.trigger_type";

        /// <summary>Current attempt number (1-based)</summary>
        public const string JobAttempt = "zapjobs.job.attempt";

        /// <summary>Final job status (completed, failed)</summary>
        public const string JobStatus = "zapjobs.job.status";

        /// <summary>Execution duration in milliseconds</summary>
        public const string JobDurationMs = "zapjobs.job.duration_ms";

        /// <summary>Worker ID that processed the job</summary>
        public const string WorkerId = "zapjobs.worker.id";

        /// <summary>Batch ID if job is part of a batch</summary>
        public const string BatchId = "zapjobs.batch.id";

        /// <summary>Whether this is a continuation job</summary>
        public const string IsContinuation = "zapjobs.job.is_continuation";

        /// <summary>Parent job run ID for continuations</summary>
        public const string ParentRunId = "zapjobs.job.parent_run_id";
    }

    /// <summary>
    /// Standard activity/operation names
    /// </summary>
    public static class Operations
    {
        /// <summary>Job execution operation</summary>
        public const string Execute = "job.execute";

        /// <summary>Job scheduling operation</summary>
        public const string Schedule = "job.schedule";

        /// <summary>Job enqueueing operation</summary>
        public const string Enqueue = "job.enqueue";
    }
}
