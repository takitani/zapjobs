namespace ZapJobs.Core.Events;

/// <summary>
/// Dispatches job lifecycle events to registered handlers
/// </summary>
public interface IJobEventDispatcher
{
    /// <summary>
    /// Dispatch an event to all registered handlers.
    /// This is fire-and-forget - handlers run in the background.
    /// </summary>
    /// <typeparam name="TEvent">The type of event to dispatch</typeparam>
    /// <param name="event">The event to dispatch</param>
    /// <returns>A task that completes when the event has been queued (not when handlers complete)</returns>
    Task DispatchAsync<TEvent>(TEvent @event) where TEvent : IJobEvent;
}
