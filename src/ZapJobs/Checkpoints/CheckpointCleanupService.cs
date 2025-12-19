using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ZapJobs.Core.Checkpoints;

namespace ZapJobs.Checkpoints;

/// <summary>
/// Background service that periodically cleans up expired checkpoints
/// and checkpoints for completed jobs.
/// </summary>
public class CheckpointCleanupService : BackgroundService
{
    private readonly ICheckpointStore _checkpointStore;
    private readonly CheckpointOptions _options;
    private readonly ILogger<CheckpointCleanupService> _logger;

    public CheckpointCleanupService(
        ICheckpointStore checkpointStore,
        IOptions<CheckpointOptions> options,
        ILogger<CheckpointCleanupService> logger)
    {
        _checkpointStore = checkpointStore;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "Checkpoint cleanup service started. Interval: {Interval}, Completed job cleanup: {CompletedAge}",
            _options.CleanupInterval,
            _options.DeleteAfterJobCompletion);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(_options.CleanupInterval, stoppingToken);
                await RunCleanupAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                // Shutdown requested, exit gracefully
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during checkpoint cleanup");
                // Continue running despite errors
            }
        }

        _logger.LogInformation("Checkpoint cleanup service stopped");
    }

    private async Task RunCleanupAsync(CancellationToken ct)
    {
        var totalDeleted = 0;

        // Clean up expired checkpoints
        var expiredDeleted = await _checkpointStore.CleanupExpiredAsync(ct);
        totalDeleted += expiredDeleted;

        // Clean up checkpoints for completed jobs
        if (_options.DeleteAfterJobCompletion.HasValue)
        {
            var completedDeleted = await _checkpointStore.CleanupCompletedJobsAsync(
                _options.DeleteAfterJobCompletion.Value, ct);
            totalDeleted += completedDeleted;
        }

        if (totalDeleted > 0)
        {
            _logger.LogInformation(
                "Checkpoint cleanup completed: {Expired} expired, {Completed} from completed jobs",
                expiredDeleted,
                totalDeleted - expiredDeleted);
        }
        else
        {
            _logger.LogDebug("Checkpoint cleanup: no checkpoints to clean up");
        }
    }
}
