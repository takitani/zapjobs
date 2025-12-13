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
    internal RateLimitPolicy? RateLimit { get; private set; }

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
    /// Set a rate limit for this job type
    /// </summary>
    /// <param name="limit">Maximum executions allowed within the window</param>
    /// <param name="window">Time window for the rate limit</param>
    public JobDefinitionBuilder WithRateLimit(int limit, TimeSpan window)
    {
        RateLimit = RateLimitPolicy.Create(limit, window);
        return this;
    }

    /// <summary>
    /// Set a rate limit with behavior for this job type
    /// </summary>
    /// <param name="limit">Maximum executions allowed within the window</param>
    /// <param name="window">Time window for the rate limit</param>
    /// <param name="behavior">What to do when rate limit is exceeded</param>
    public JobDefinitionBuilder WithRateLimit(int limit, TimeSpan window, RateLimitBehavior behavior)
    {
        RateLimit = RateLimitPolicy.Create(limit, window, behavior);
        return this;
    }

    /// <summary>
    /// Set a rate limit with behavior and max delay for this job type
    /// </summary>
    /// <param name="limit">Maximum executions allowed within the window</param>
    /// <param name="window">Time window for the rate limit</param>
    /// <param name="behavior">What to do when rate limit is exceeded</param>
    /// <param name="maxDelay">Maximum delay when using Delay behavior</param>
    public JobDefinitionBuilder WithRateLimit(int limit, TimeSpan window, RateLimitBehavior behavior, TimeSpan maxDelay)
    {
        RateLimit = RateLimitPolicy.Create(limit, window, behavior, maxDelay);
        return this;
    }

    /// <summary>
    /// Set a rate limit policy for this job type
    /// </summary>
    public JobDefinitionBuilder WithRateLimit(RateLimitPolicy policy)
    {
        RateLimit = policy;
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
        if (RateLimit != null) definition.RateLimit = RateLimit;

        return definition;
    }

    /// <summary>
    /// Apply the configured options to an existing JobDefinition
    /// </summary>
    public void ApplyTo(JobDefinition definition)
    {
        if (Queue != null) definition.Queue = Queue;
        if (MaxRetries.HasValue) definition.MaxRetries = MaxRetries.Value;
        if (TimeoutSeconds.HasValue) definition.TimeoutSeconds = TimeoutSeconds.Value;
        if (MaxConcurrency.HasValue) definition.MaxConcurrency = MaxConcurrency.Value;
        if (PreventOverlapping.HasValue) definition.PreventOverlapping = PreventOverlapping.Value;
        if (RateLimit != null) definition.RateLimit = RateLimit;
    }
}
