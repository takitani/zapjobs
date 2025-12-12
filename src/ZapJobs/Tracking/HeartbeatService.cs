using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ZapJobs.Core;

namespace ZapJobs.Tracking;

/// <summary>
/// Background service that sends heartbeats to indicate worker health
/// </summary>
public class HeartbeatService : BackgroundService
{
    private readonly IJobStorage _storage;
    private readonly ZapJobsOptions _options;
    private readonly ILogger<HeartbeatService> _logger;
    private readonly string _workerId;
    private int _jobsProcessed;
    private int _jobsFailed;

    public HeartbeatService(
        IJobStorage storage,
        IOptions<ZapJobsOptions> options,
        ILogger<HeartbeatService> logger)
    {
        _storage = storage;
        _options = options.Value;
        _logger = logger;
        _workerId = _options.WorkerId ?? $"{Environment.MachineName}-{Guid.NewGuid():N}".Substring(0, 32);
    }

    public string WorkerId => _workerId;

    public void IncrementProcessed() => Interlocked.Increment(ref _jobsProcessed);
    public void IncrementFailed() => Interlocked.Increment(ref _jobsFailed);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Heartbeat service started for worker {WorkerId}", _workerId);

        // Send initial heartbeat
        await SendHeartbeatAsync(stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(_options.HeartbeatInterval, stoppingToken);
                await SendHeartbeatAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                // Normal shutdown
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error sending heartbeat");
                // Continue trying
            }
        }

        // Send final heartbeat indicating shutdown
        try
        {
            var heartbeat = new JobHeartbeat
            {
                WorkerId = _workerId,
                Hostname = Environment.MachineName,
                ProcessId = Environment.ProcessId,
                Timestamp = DateTime.UtcNow,
                Queues = _options.Queues,
                JobsProcessed = _jobsProcessed,
                JobsFailed = _jobsFailed,
                IsShuttingDown = true
            };

            await _storage.SendHeartbeatAsync(heartbeat, default);
            _logger.LogInformation("Sent shutdown heartbeat for worker {WorkerId}", _workerId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to send shutdown heartbeat");
        }
    }

    private async Task SendHeartbeatAsync(CancellationToken ct)
    {
        var heartbeat = new JobHeartbeat
        {
            WorkerId = _workerId,
            Hostname = Environment.MachineName,
            ProcessId = Environment.ProcessId,
            Timestamp = DateTime.UtcNow,
            Queues = _options.Queues,
            JobsProcessed = _jobsProcessed,
            JobsFailed = _jobsFailed,
            IsShuttingDown = false
        };

        await _storage.SendHeartbeatAsync(heartbeat, ct);
        _logger.LogDebug("Heartbeat sent for worker {WorkerId}", _workerId);
    }
}
