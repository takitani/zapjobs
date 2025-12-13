using Microsoft.Extensions.Logging;
using ZapJobs.Core;

namespace ZapJobs.RateLimiting;

/// <summary>
/// Sliding window rate limiter implementation using storage for tracking
/// </summary>
public class SlidingWindowRateLimiter : IRateLimiter
{
    private readonly IJobStorage _storage;
    private readonly ILogger<SlidingWindowRateLimiter> _logger;

    public SlidingWindowRateLimiter(
        IJobStorage storage,
        ILogger<SlidingWindowRateLimiter> logger)
    {
        _storage = storage;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task<RateLimitResult> AcquireAsync(
        string key,
        RateLimitPolicy policy,
        CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        var windowStart = now - policy.Window;

        // Count executions in the current window
        var count = await _storage.CountRateLimitExecutionsAsync(key, windowStart, ct);

        if (count < policy.Limit)
        {
            // Record this execution
            await _storage.RecordRateLimitExecutionAsync(key, now, ct);

            _logger.LogDebug(
                "Rate limit acquired for {Key}: {Count}/{Limit}",
                key, count + 1, policy.Limit);

            return RateLimitResult.Allow(policy.Limit - count - 1);
        }

        // Calculate when the next permit will be available
        var oldestExecution = await _storage.GetOldestRateLimitExecutionAsync(key, windowStart, ct);
        var retryAfter = oldestExecution.HasValue
            ? oldestExecution.Value.Add(policy.Window) - now
            : policy.Window;

        // Ensure retryAfter is positive
        if (retryAfter < TimeSpan.Zero)
            retryAfter = TimeSpan.FromSeconds(1);

        _logger.LogDebug(
            "Rate limit exceeded for {Key}: {Count}/{Limit}, retry after {RetryAfter}",
            key, count, policy.Limit, retryAfter);

        return RateLimitResult.Deny(retryAfter);
    }

    /// <inheritdoc/>
    public Task ReleaseAsync(string key, CancellationToken ct = default)
    {
        // For sliding window, we don't need to explicitly release permits
        // The permits naturally expire based on the time window
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public async Task<RateLimitStatus> GetStatusAsync(
        string key,
        RateLimitPolicy policy,
        CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        var windowStart = now - policy.Window;

        var count = await _storage.CountRateLimitExecutionsAsync(key, windowStart, ct);
        var oldestExecution = await _storage.GetOldestRateLimitExecutionAsync(key, windowStart, ct);

        var windowRemaining = oldestExecution.HasValue
            ? oldestExecution.Value.Add(policy.Window) - now
            : policy.Window;

        if (windowRemaining < TimeSpan.Zero)
            windowRemaining = TimeSpan.Zero;

        return new RateLimitStatus(key, count, policy.Limit, windowRemaining);
    }

    /// <summary>
    /// Generate a rate limit key for a job type
    /// </summary>
    public static string GetJobTypeKey(string jobTypeId) => $"job:{jobTypeId}";

    /// <summary>
    /// Generate a rate limit key for a queue
    /// </summary>
    public static string GetQueueKey(string queue) => $"queue:{queue}";

    /// <summary>
    /// Generate the global rate limit key
    /// </summary>
    public static string GlobalKey => "global";
}
