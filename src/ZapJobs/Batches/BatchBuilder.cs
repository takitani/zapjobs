using ZapJobs.Core;

namespace ZapJobs.Batches;

/// <summary>
/// Builder for configuring jobs within a batch
/// </summary>
internal class BatchBuilder : IBatchBuilder
{
    private readonly List<BatchJobInfo> _jobs = new();
    private readonly List<(string Name, Action<IBatchBuilder> Configure)> _nestedBatches = new();
    private BatchContinuationInfo? _onSuccess;
    private BatchContinuationInfo? _onFailure;
    private BatchContinuationInfo? _onComplete;

    /// <summary>Jobs to be created in this batch</summary>
    public IReadOnlyList<BatchJobInfo> Jobs => _jobs;

    /// <summary>Nested batches to be created</summary>
    public IReadOnlyList<(string Name, Action<IBatchBuilder> Configure)> NestedBatches => _nestedBatches;

    /// <summary>Continuation to run on success</summary>
    public BatchContinuationInfo? OnSuccessContinuation => _onSuccess;

    /// <summary>Continuation to run on failure</summary>
    public BatchContinuationInfo? OnFailureContinuation => _onFailure;

    /// <summary>Continuation to run on completion (success or failure)</summary>
    public BatchContinuationInfo? OnCompleteContinuation => _onComplete;

    public IBatchBuilder Enqueue(string jobTypeId, object? input = null, string? queue = null)
    {
        _jobs.Add(new BatchJobInfo(jobTypeId, input, queue));
        return this;
    }

    public IBatchBuilder Enqueue<TJob>(object? input = null, string? queue = null) where TJob : IJob
    {
        var job = Activator.CreateInstance<TJob>();
        return Enqueue(job.JobTypeId, input, queue);
    }

    public IBatchBuilder AddBatch(string name, Action<IBatchBuilder> configure)
    {
        _nestedBatches.Add((name, configure));
        return this;
    }

    public IBatchBuilder OnSuccess(string jobTypeId, object? input = null)
    {
        _onSuccess = new BatchContinuationInfo(jobTypeId, input);
        return this;
    }

    public IBatchBuilder OnFailure(string jobTypeId, object? input = null)
    {
        _onFailure = new BatchContinuationInfo(jobTypeId, input);
        return this;
    }

    public IBatchBuilder OnComplete(string jobTypeId, object? input = null)
    {
        _onComplete = new BatchContinuationInfo(jobTypeId, input);
        return this;
    }
}
