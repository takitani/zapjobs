using System.Threading.Channels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ZapJobs.Core.Events;

namespace ZapJobs.Events;

/// <summary>
/// Dispatches job events to registered handlers using a background channel.
/// Events are fire-and-forget to avoid blocking job execution.
/// </summary>
public sealed class JobEventDispatcher : IJobEventDispatcher
{
    private readonly IServiceProvider _services;
    private readonly ILogger<JobEventDispatcher> _logger;
    private readonly Channel<Func<IServiceProvider, CancellationToken, Task>> _channel;

    /// <summary>
    /// Create a new job event dispatcher
    /// </summary>
    public JobEventDispatcher(
        IServiceProvider services,
        ILogger<JobEventDispatcher> logger)
    {
        _services = services;
        _logger = logger;
        _channel = Channel.CreateUnbounded<Func<IServiceProvider, CancellationToken, Task>>(
            new UnboundedChannelOptions
            {
                SingleReader = true,
                SingleWriter = false
            });
    }

    /// <inheritdoc />
    public Task DispatchAsync<TEvent>(TEvent @event) where TEvent : IJobEvent
    {
        // Fire and forget - queue the work and return immediately
        var success = _channel.Writer.TryWrite(async (scopedServices, ct) =>
        {
            var handlers = scopedServices.GetServices<IJobEventHandler<TEvent>>();
            var eventTypeName = typeof(TEvent).Name;

            foreach (var handler in handlers)
            {
                try
                {
                    await handler.HandleAsync(@event, ct);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex,
                        "Error in event handler {Handler} for {EventType} (RunId: {RunId})",
                        handler.GetType().Name, eventTypeName, @event.RunId);
                }
            }
        });

        if (!success)
        {
            _logger.LogWarning(
                "Failed to queue event {EventType} for RunId {RunId} - channel is closed",
                typeof(TEvent).Name, @event.RunId);
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Get the channel reader for the background service
    /// </summary>
    internal ChannelReader<Func<IServiceProvider, CancellationToken, Task>> Reader => _channel.Reader;

    /// <summary>
    /// Complete the channel (called on shutdown)
    /// </summary>
    internal void Complete() => _channel.Writer.Complete();
}
