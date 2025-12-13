namespace ZapJobs.Core;

/// <summary>
/// Rate limiter interface for controlling job execution rates
/// </summary>
public interface IRateLimiter
{
    /// <summary>
    /// Try to acquire a permit to execute
    /// </summary>
    /// <param name="key">Rate limit key (job type, queue, etc)</param>
    /// <param name="policy">Rate limit policy to apply</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>Result with permit status or delay information</returns>
    Task<RateLimitResult> AcquireAsync(string key, RateLimitPolicy policy, CancellationToken ct = default);

    /// <summary>
    /// Release a permit (for sliding window with concurrent limit)
    /// </summary>
    /// <param name="key">Rate limit key</param>
    /// <param name="ct">Cancellation token</param>
    Task ReleaseAsync(string key, CancellationToken ct = default);

    /// <summary>
    /// Get current usage status for a key
    /// </summary>
    /// <param name="key">Rate limit key</param>
    /// <param name="policy">Rate limit policy to check against</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>Current rate limit status</returns>
    Task<RateLimitStatus> GetStatusAsync(string key, RateLimitPolicy policy, CancellationToken ct = default);
}

/// <summary>
/// Result of a rate limit acquire attempt
/// </summary>
public record RateLimitResult(
    bool Allowed,
    TimeSpan? RetryAfter = null,
    int RemainingPermits = 0)
{
    /// <summary>
    /// Create an allowed result
    /// </summary>
    public static RateLimitResult Allow(int remainingPermits = 0) => new(true, null, remainingPermits);

    /// <summary>
    /// Create a denied result with retry after information
    /// </summary>
    public static RateLimitResult Deny(TimeSpan retryAfter) => new(false, retryAfter, 0);
}

/// <summary>
/// Current status of a rate limit key
/// </summary>
public record RateLimitStatus(
    string Key,
    int CurrentCount,
    int Limit,
    TimeSpan WindowRemaining)
{
    /// <summary>
    /// Usage ratio (0.0 to 1.0+)
    /// </summary>
    public double UsageRatio => Limit > 0 ? (double)CurrentCount / Limit : 0;

    /// <summary>
    /// Whether the limit is exceeded
    /// </summary>
    public bool IsExceeded => CurrentCount >= Limit;
}
