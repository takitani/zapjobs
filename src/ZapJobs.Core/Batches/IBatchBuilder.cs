namespace ZapJobs.Core;

/// <summary>
/// Builder for configuring jobs within a batch
/// </summary>
public interface IBatchBuilder
{
    /// <summary>Add a job to the batch</summary>
    IBatchBuilder Enqueue(string jobTypeId, object? input = null, string? queue = null);

    /// <summary>Add a job to the batch using generic type</summary>
    IBatchBuilder Enqueue<TJob>(object? input = null, string? queue = null) where TJob : IJob;

    /// <summary>Add a nested batch</summary>
    IBatchBuilder AddBatch(string name, Action<IBatchBuilder> configure);

    /// <summary>Set continuation to run when all jobs complete successfully</summary>
    IBatchBuilder OnSuccess(string jobTypeId, object? input = null);

    /// <summary>Set continuation to run when any job fails</summary>
    IBatchBuilder OnFailure(string jobTypeId, object? input = null);

    /// <summary>Set continuation to run when batch finishes (success or failure)</summary>
    IBatchBuilder OnComplete(string jobTypeId, object? input = null);
}

/// <summary>
/// Information about a job to be added to a batch
/// </summary>
public record BatchJobInfo(string JobTypeId, object? Input, string? Queue);

/// <summary>
/// Information about a batch continuation
/// </summary>
public record BatchContinuationInfo(string JobTypeId, object? Input);
