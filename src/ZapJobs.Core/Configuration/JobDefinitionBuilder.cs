namespace ZapJobs.Core;

/// <summary>
/// Builder for configuring job definition options via fluent API
/// </summary>
public class JobDefinitionBuilder
{
    internal string? Queue { get; private set; }
    internal int? MaxRetries { get; private set; }
    internal int? TimeoutSeconds { get; private set; }
    internal int? MaxConcurrency { get; private set; }
    internal bool? PreventOverlapping { get; private set; }

    /// <summary>
    /// Prevent a new run from starting while another instance is running or pending.
    /// Useful for long-running jobs where overlapping executions are undesirable.
    /// </summary>
    public JobDefinitionBuilder WithPreventOverlapping(bool prevent = true)
    {
        PreventOverlapping = prevent;
        return this;
    }

    /// <summary>
    /// Set the queue for this job type
    /// </summary>
    public JobDefinitionBuilder WithQueue(string queue)
    {
        Queue = queue;
        return this;
    }

    /// <summary>
    /// Set the maximum number of retry attempts
    /// </summary>
    public JobDefinitionBuilder WithMaxRetries(int maxRetries)
    {
        MaxRetries = maxRetries;
        return this;
    }

    /// <summary>
    /// Set the timeout for job execution
    /// </summary>
    public JobDefinitionBuilder WithTimeout(TimeSpan timeout)
    {
        TimeoutSeconds = (int)timeout.TotalSeconds;
        return this;
    }

    /// <summary>
    /// Set the maximum concurrent executions allowed
    /// </summary>
    public JobDefinitionBuilder WithMaxConcurrency(int maxConcurrency)
    {
        MaxConcurrency = maxConcurrency;
        return this;
    }

    /// <summary>
    /// Build a JobDefinition with the configured options
    /// </summary>
    internal JobDefinition Build(string jobTypeId)
    {
        var definition = new JobDefinition
        {
            JobTypeId = jobTypeId,
            DisplayName = jobTypeId
        };

        if (Queue != null) definition.Queue = Queue;
        if (MaxRetries.HasValue) definition.MaxRetries = MaxRetries.Value;
        if (TimeoutSeconds.HasValue) definition.TimeoutSeconds = TimeoutSeconds.Value;
        if (MaxConcurrency.HasValue) definition.MaxConcurrency = MaxConcurrency.Value;
        if (PreventOverlapping.HasValue) definition.PreventOverlapping = PreventOverlapping.Value;

        return definition;
    }

    /// <summary>
    /// Apply the configured options to an existing JobDefinition
    /// </summary>
    internal void ApplyTo(JobDefinition definition)
    {
        if (Queue != null) definition.Queue = Queue;
        if (MaxRetries.HasValue) definition.MaxRetries = MaxRetries.Value;
        if (TimeoutSeconds.HasValue) definition.TimeoutSeconds = TimeoutSeconds.Value;
        if (MaxConcurrency.HasValue) definition.MaxConcurrency = MaxConcurrency.Value;
        if (PreventOverlapping.HasValue) definition.PreventOverlapping = PreventOverlapping.Value;
    }
}
