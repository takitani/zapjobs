using System.Diagnostics;
using ZapJobs.Core;

namespace ZapJobs.OpenTelemetry;

/// <summary>
/// Options for configuring ZapJobs OpenTelemetry instrumentation
/// </summary>
public class ZapJobsInstrumentationOptions
{
    /// <summary>
    /// Whether to record exception details in spans. Default is true.
    /// </summary>
    public bool RecordException { get; set; } = true;

    /// <summary>
    /// Callback to enrich spans with additional tags from job run data.
    /// Called after standard tags are set.
    /// </summary>
    /// <example>
    /// <code>
    /// options.Enrich = (activity, run) =>
    /// {
    ///     activity.SetTag("tenant.id", run.Queue.Split('-')[0]);
    /// };
    /// </code>
    /// </example>
    public Action<Activity, JobRun>? Enrich { get; set; }

    /// <summary>
    /// Filter to exclude jobs from tracing. Return false to skip tracing for a job.
    /// </summary>
    /// <example>
    /// <code>
    /// options.Filter = run => run.JobTypeId != "health-check";
    /// </code>
    /// </example>
    public Func<JobRun, bool>? Filter { get; set; }

    /// <summary>
    /// Whether to record the job input JSON in span attributes. Default is false (for security).
    /// </summary>
    public bool RecordInput { get; set; } = false;

    /// <summary>
    /// Whether to record the job output JSON in span attributes. Default is false (for security).
    /// </summary>
    public bool RecordOutput { get; set; } = false;

    /// <summary>
    /// Maximum length of input/output JSON to record in attributes. Default is 1000 characters.
    /// </summary>
    public int MaxPayloadLength { get; set; } = 1000;
}
