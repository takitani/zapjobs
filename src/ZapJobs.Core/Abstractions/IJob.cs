namespace ZapJobs.Core;

/// <summary>
/// Base interface for all background jobs
/// </summary>
public interface IJob
{
    /// <summary>Unique identifier for this job type (e.g., "email-sender", "report-generator")</summary>
    string JobTypeId { get; }

    /// <summary>Execute the job</summary>
    Task ExecuteAsync(JobExecutionContext context, CancellationToken ct);
}

/// <summary>
/// Job with typed input parameters
/// </summary>
/// <typeparam name="TInput">Type of input data</typeparam>
public interface IJob<TInput> : IJob where TInput : class
{
    /// <summary>Execute the job with typed input</summary>
    Task ExecuteAsync(JobExecutionContext<TInput> context, CancellationToken ct);

    // Default implementation redirects to typed version
    Task IJob.ExecuteAsync(JobExecutionContext context, CancellationToken ct)
        => ExecuteAsync(context.As<TInput>(), ct);
}

/// <summary>
/// Job with typed input and output
/// </summary>
/// <typeparam name="TInput">Type of input data</typeparam>
/// <typeparam name="TOutput">Type of output data</typeparam>
public interface IJob<TInput, TOutput> : IJob<TInput>
    where TInput : class
    where TOutput : class
{
    /// <summary>Execute and return typed result</summary>
    new Task<TOutput> ExecuteAsync(JobExecutionContext<TInput> context, CancellationToken ct);

    // Redirect to typed version and discard result
    async Task IJob<TInput>.ExecuteAsync(JobExecutionContext<TInput> context, CancellationToken ct)
    {
        var result = await ExecuteAsync(context, ct);
        context.SetOutput(result);
    }
}
