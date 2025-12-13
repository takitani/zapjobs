using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ZapJobs.Core;

namespace ZapJobs.RateLimiting;

/// <summary>
/// Background service that cleans up old rate limit execution records
/// </summary>
public class RateLimitCleanupService : BackgroundService
{
    private readonly IJobStorage _storage;
    private readonly ZapJobsOptions _options;
    private readonly ILogger<RateLimitCleanupService> _logger;

    public RateLimitCleanupService(
        IJobStorage storage,
        IOptions<ZapJobsOptions> options,
        ILogger<RateLimitCleanupService> logger)
    {
        _storage = storage;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Rate limit cleanup service started");

        // Run cleanup every hour
        var cleanupInterval = TimeSpan.FromHours(1);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(cleanupInterval, stoppingToken);
                await CleanupAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in rate limit cleanup loop");
                await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken);
            }
        }

        _logger.LogInformation("Rate limit cleanup service stopped");
    }

    private async Task CleanupAsync(CancellationToken ct)
    {
        // Keep records for the largest window we might encounter (default: 1 hour buffer)
        var olderThan = DateTime.UtcNow.AddHours(-2);

        var deleted = await _storage.CleanupRateLimitExecutionsAsync(olderThan, ct);

        if (deleted > 0)
        {
            _logger.LogDebug("Cleaned up {Count} old rate limit execution records", deleted);
        }
    }
}
