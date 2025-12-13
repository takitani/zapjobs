namespace ZapJobs.Core;

/// <summary>
/// Configuration for rate limiting job executions
/// </summary>
public class RateLimitPolicy
{
    /// <summary>Maximum executions allowed within the time window</summary>
    public int Limit { get; set; }

    /// <summary>Time window for the rate limit</summary>
    public TimeSpan Window { get; set; }

    /// <summary>What to do when rate limit is reached</summary>
    public RateLimitBehavior Behavior { get; set; } = RateLimitBehavior.Delay;

    /// <summary>Maximum delay when using Delay behavior</summary>
    public TimeSpan MaxDelay { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Create a rate limit policy with the specified limit and window
    /// </summary>
    public static RateLimitPolicy Create(int limit, TimeSpan window) => new()
    {
        Limit = limit,
        Window = window
    };

    /// <summary>
    /// Create a rate limit policy with the specified limit, window, and behavior
    /// </summary>
    public static RateLimitPolicy Create(int limit, TimeSpan window, RateLimitBehavior behavior) => new()
    {
        Limit = limit,
        Window = window,
        Behavior = behavior
    };

    /// <summary>
    /// Create a rate limit policy with the specified limit, window, behavior, and max delay
    /// </summary>
    public static RateLimitPolicy Create(int limit, TimeSpan window, RateLimitBehavior behavior, TimeSpan maxDelay) => new()
    {
        Limit = limit,
        Window = window,
        Behavior = behavior,
        MaxDelay = maxDelay
    };
}

/// <summary>
/// Behavior when rate limit is exceeded
/// </summary>
public enum RateLimitBehavior
{
    /// <summary>Delay execution until rate limit allows (reschedule for later)</summary>
    Delay = 0,

    /// <summary>Reject the job and mark as failed</summary>
    Reject = 1,

    /// <summary>Skip silently (useful for recurring jobs)</summary>
    Skip = 2
}

/// <summary>
/// Scope of rate limiting
/// </summary>
public enum RateLimitScope
{
    /// <summary>Rate limit per job type</summary>
    JobType = 0,

    /// <summary>Rate limit per queue</summary>
    Queue = 1,

    /// <summary>Global rate limit across all jobs</summary>
    Global = 2
}
