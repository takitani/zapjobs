using System.Diagnostics;
using Microsoft.Extensions.Options;
using OpenTelemetry.Trace;
using ZapJobs.Core;
using ZapJobs.Execution;

namespace ZapJobs.OpenTelemetry;

/// <summary>
/// Decorator that adds OpenTelemetry tracing and metrics to job execution
/// </summary>
public class TracingJobExecutor : IJobExecutor
{
    private readonly IJobExecutor _inner;
    private readonly ZapJobsInstrumentationOptions _options;

    /// <summary>
    /// Creates a new tracing job executor
    /// </summary>
    public TracingJobExecutor(IJobExecutor inner, IOptions<ZapJobsInstrumentationOptions> options)
    {
        _inner = inner;
        _options = options.Value;
    }

    /// <inheritdoc />
    public void RegisterJobType<TJob>() where TJob : IJob
        => _inner.RegisterJobType<TJob>();

    /// <inheritdoc />
    public void RegisterJobType(Type jobType)
        => _inner.RegisterJobType(jobType);

    /// <inheritdoc />
    public IReadOnlyCollection<string> GetRegisteredJobTypes()
        => _inner.GetRegisteredJobTypes();

    /// <inheritdoc />
    public async Task<JobRunResult> ExecuteAsync(JobRun run, CancellationToken ct = default)
    {
        // Check filter - if filtered out, skip tracing
        if (_options.Filter != null && !_options.Filter(run))
        {
            return await _inner.ExecuteAsync(run, ct);
        }

        // Create activity name
        var activityName = $"{ZapJobsInstrumentation.Operations.Execute} {run.JobTypeId}";

        // Start activity with Consumer kind (processing a message)
        using var activity = ZapJobsInstrumentation.ActivitySource.StartActivity(
            name: activityName,
            kind: ActivityKind.Consumer);

        // If activity is null (no listeners), just execute
        if (activity == null)
        {
            return await ExecuteWithMetricsAsync(run, ct);
        }

        // Set standard tags
        SetActivityTags(activity, run);

        // Apply custom enrichment
        _options.Enrich?.Invoke(activity, run);

        // Record input if enabled
        if (_options.RecordInput && !string.IsNullOrEmpty(run.InputJson))
        {
            var input = run.InputJson.Length > _options.MaxPayloadLength
                ? run.InputJson[.._options.MaxPayloadLength] + "..."
                : run.InputJson;
            activity.SetTag("zapjobs.job.input", input);
        }

        // Track running jobs metric
        var tags = new[]
        {
            ZapJobsMetrics.JobTypeTag(run.JobTypeId),
            ZapJobsMetrics.QueueTag(run.Queue)
        };

        ZapJobsMetrics.JobsRunning.Add(1, tags);

        try
        {
            var result = await _inner.ExecuteAsync(run, ct);

            // Set result tags
            activity.SetTag(ZapJobsInstrumentation.Tags.JobStatus, result.Success ? "completed" : "failed");
            activity.SetTag(ZapJobsInstrumentation.Tags.JobDurationMs, result.DurationMs);

            // Record output if enabled
            if (_options.RecordOutput && result.Success && !string.IsNullOrEmpty(result.OutputJson))
            {
                var output = result.OutputJson.Length > _options.MaxPayloadLength
                    ? result.OutputJson[.._options.MaxPayloadLength] + "..."
                    : result.OutputJson;
                activity.SetTag("zapjobs.job.output", output);
            }

            if (result.Success)
            {
                activity.SetStatus(ActivityStatusCode.Ok);

                // Record success metrics
                ZapJobsMetrics.JobsProcessed.Add(1, tags);
                ZapJobsMetrics.JobsSucceeded.Add(1, tags);
                ZapJobsMetrics.JobDuration.Record(result.DurationMs, tags);
            }
            else
            {
                activity.SetStatus(ActivityStatusCode.Error, result.ErrorMessage);

                if (_options.RecordException && result.ErrorMessage != null)
                {
                    activity.AddEvent(new ActivityEvent("exception", tags: new ActivityTagsCollection
                    {
                        ["exception.type"] = "JobExecutionException",
                        ["exception.message"] = result.ErrorMessage
                    }));
                }

                // Record failure metrics
                ZapJobsMetrics.JobsProcessed.Add(1, tags);
                ZapJobsMetrics.JobDuration.Record(result.DurationMs, tags);

                if (result.WillRetry)
                {
                    ZapJobsMetrics.JobsRetried.Add(1, tags);
                }
                else
                {
                    ZapJobsMetrics.JobsFailed.Add(1, tags);
                    ZapJobsMetrics.JobsDeadLettered.Add(1, tags);
                }
            }

            return result;
        }
        catch (Exception ex)
        {
            activity.SetStatus(ActivityStatusCode.Error, ex.Message);

            if (_options.RecordException)
            {
                activity.AddException(ex);
            }

            // Record failure metrics
            ZapJobsMetrics.JobsProcessed.Add(1, tags);
            ZapJobsMetrics.JobsFailed.Add(1, tags);

            throw;
        }
        finally
        {
            ZapJobsMetrics.JobsRunning.Add(-1, tags);
        }
    }

    private async Task<JobRunResult> ExecuteWithMetricsAsync(JobRun run, CancellationToken ct)
    {
        var tags = new[]
        {
            ZapJobsMetrics.JobTypeTag(run.JobTypeId),
            ZapJobsMetrics.QueueTag(run.Queue)
        };

        ZapJobsMetrics.JobsRunning.Add(1, tags);

        try
        {
            var result = await _inner.ExecuteAsync(run, ct);

            ZapJobsMetrics.JobsProcessed.Add(1, tags);
            ZapJobsMetrics.JobDuration.Record(result.DurationMs, tags);

            if (result.Success)
            {
                ZapJobsMetrics.JobsSucceeded.Add(1, tags);
            }
            else if (result.WillRetry)
            {
                ZapJobsMetrics.JobsRetried.Add(1, tags);
            }
            else
            {
                ZapJobsMetrics.JobsFailed.Add(1, tags);
                ZapJobsMetrics.JobsDeadLettered.Add(1, tags);
            }

            return result;
        }
        catch
        {
            ZapJobsMetrics.JobsProcessed.Add(1, tags);
            ZapJobsMetrics.JobsFailed.Add(1, tags);
            throw;
        }
        finally
        {
            ZapJobsMetrics.JobsRunning.Add(-1, tags);
        }
    }

    private static void SetActivityTags(Activity activity, JobRun run)
    {
        activity.SetTag(ZapJobsInstrumentation.Tags.JobTypeId, run.JobTypeId);
        activity.SetTag(ZapJobsInstrumentation.Tags.JobRunId, run.Id.ToString());
        activity.SetTag(ZapJobsInstrumentation.Tags.JobQueue, run.Queue);
        activity.SetTag(ZapJobsInstrumentation.Tags.JobTriggerType, run.TriggerType.ToString());
        activity.SetTag(ZapJobsInstrumentation.Tags.JobAttempt, run.AttemptNumber);

        if (run.BatchId.HasValue)
        {
            activity.SetTag(ZapJobsInstrumentation.Tags.BatchId, run.BatchId.Value.ToString());
        }

        // Check if this is a continuation
        if (run.TriggerType == JobTriggerType.Continuation && !string.IsNullOrEmpty(run.TriggeredBy))
        {
            activity.SetTag(ZapJobsInstrumentation.Tags.IsContinuation, true);

            // Extract parent run ID from "continuation:{runId}" format
            if (run.TriggeredBy.StartsWith("continuation:"))
            {
                var parentId = run.TriggeredBy["continuation:".Length..];
                activity.SetTag(ZapJobsInstrumentation.Tags.ParentRunId, parentId);
            }
        }
    }
}
