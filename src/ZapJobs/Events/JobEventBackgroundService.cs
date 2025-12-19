using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace ZapJobs.Events;

/// <summary>
/// Background service that processes job events from the dispatcher channel
/// </summary>
public sealed class JobEventBackgroundService : BackgroundService
{
    private readonly JobEventDispatcher _dispatcher;
    private readonly IServiceProvider _services;
    private readonly ILogger<JobEventBackgroundService> _logger;

    /// <summary>
    /// Create a new job event background service
    /// </summary>
    public JobEventBackgroundService(
        JobEventDispatcher dispatcher,
        IServiceProvider services,
        ILogger<JobEventBackgroundService> logger)
    {
        _dispatcher = dispatcher;
        _services = services;
        _logger = logger;
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Job event processor started");

        try
        {
            await foreach (var task in _dispatcher.Reader.ReadAllAsync(stoppingToken))
            {
                try
                {
                    // Create a scope for each event to allow scoped services in handlers
                    using var scope = _services.CreateScope();
                    await task(scope.ServiceProvider, stoppingToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error processing job event");
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Normal shutdown
        }

        _logger.LogInformation("Job event processor stopped");
    }

    /// <inheritdoc />
    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _dispatcher.Complete();
        await base.StopAsync(cancellationToken);
    }
}
