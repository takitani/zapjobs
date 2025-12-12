using System.Text.Json;

namespace ZapJobs.Core;

/// <summary>
/// Context passed to a job during execution
/// </summary>
public class JobExecutionContext
{
    /// <summary>Unique ID of this run</summary>
    public Guid RunId { get; }

    /// <summary>Job type identifier</summary>
    public string JobTypeId { get; }

    /// <summary>How this job was triggered</summary>
    public JobTriggerType TriggerType { get; }

    /// <summary>Who/what triggered this job</summary>
    public string? TriggeredBy { get; }

    /// <summary>Service provider for dependency injection</summary>
    public IServiceProvider Services { get; }

    /// <summary>Logger for this execution</summary>
    public IJobLogger Logger { get; }

    /// <summary>Raw input as JSON document</summary>
    public JsonDocument? InputDocument { get; }

    private int _itemsProcessed;
    private int _itemsSucceeded;
    private int _itemsFailed;
    private object? _output;

    public JobExecutionContext(
        Guid runId,
        string jobTypeId,
        JobTriggerType triggerType,
        string? triggeredBy,
        IServiceProvider services,
        IJobLogger logger,
        JsonDocument? inputDocument = null)
    {
        RunId = runId;
        JobTypeId = jobTypeId;
        TriggerType = triggerType;
        TriggeredBy = triggeredBy;
        Services = services;
        Logger = logger;
        InputDocument = inputDocument;
    }

    /// <summary>Get typed input from JSON</summary>
    public T? GetInput<T>() where T : class
    {
        if (InputDocument == null) return null;
        return InputDocument.Deserialize<T>();
    }

    /// <summary>Convert to typed context</summary>
    public JobExecutionContext<T> As<T>() where T : class
    {
        return new JobExecutionContext<T>(this);
    }

    /// <summary>Set output data</summary>
    public void SetOutput(object? output)
    {
        _output = output;
    }

    /// <summary>Get output data</summary>
    public object? GetOutput() => _output;

    /// <summary>Increment items processed counter</summary>
    public void IncrementProcessed(int count = 1)
    {
        Interlocked.Add(ref _itemsProcessed, count);
    }

    /// <summary>Increment items succeeded counter</summary>
    public void IncrementSucceeded(int count = 1)
    {
        Interlocked.Add(ref _itemsSucceeded, count);
    }

    /// <summary>Increment items failed counter</summary>
    public void IncrementFailed(int count = 1)
    {
        Interlocked.Add(ref _itemsFailed, count);
    }

    /// <summary>Get current metrics</summary>
    public JobMetrics GetMetrics() => new(_itemsProcessed, _itemsSucceeded, _itemsFailed);

    /// <summary>Update all metrics at once</summary>
    public void UpdateMetrics(int processed, int succeeded, int failed)
    {
        Interlocked.Exchange(ref _itemsProcessed, processed);
        Interlocked.Exchange(ref _itemsSucceeded, succeeded);
        Interlocked.Exchange(ref _itemsFailed, failed);
    }
}

/// <summary>
/// Typed context with strongly-typed input
/// </summary>
public class JobExecutionContext<TInput> where TInput : class
{
    private readonly JobExecutionContext _inner;

    public JobExecutionContext(JobExecutionContext inner)
    {
        _inner = inner;
        Input = inner.GetInput<TInput>();
    }

    /// <summary>Typed input data</summary>
    public TInput? Input { get; }

    /// <summary>Unique ID of this run</summary>
    public Guid RunId => _inner.RunId;

    /// <summary>Job type identifier</summary>
    public string JobTypeId => _inner.JobTypeId;

    /// <summary>How this job was triggered</summary>
    public JobTriggerType TriggerType => _inner.TriggerType;

    /// <summary>Who/what triggered this job</summary>
    public string? TriggeredBy => _inner.TriggeredBy;

    /// <summary>Service provider for dependency injection</summary>
    public IServiceProvider Services => _inner.Services;

    /// <summary>Logger for this execution</summary>
    public IJobLogger Logger => _inner.Logger;

    /// <summary>Set output data</summary>
    public void SetOutput(object? output) => _inner.SetOutput(output);

    /// <summary>Increment items processed counter</summary>
    public void IncrementProcessed(int count = 1) => _inner.IncrementProcessed(count);

    /// <summary>Increment items succeeded counter</summary>
    public void IncrementSucceeded(int count = 1) => _inner.IncrementSucceeded(count);

    /// <summary>Increment items failed counter</summary>
    public void IncrementFailed(int count = 1) => _inner.IncrementFailed(count);

    /// <summary>Get current metrics</summary>
    public JobMetrics GetMetrics() => _inner.GetMetrics();

    /// <summary>Update all metrics at once</summary>
    public void UpdateMetrics(int processed, int succeeded, int failed) => _inner.UpdateMetrics(processed, succeeded, failed);
}
