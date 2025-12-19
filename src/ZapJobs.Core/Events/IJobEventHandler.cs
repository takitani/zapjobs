namespace ZapJobs.Core.Events;

/// <summary>
/// Interface for handling job lifecycle events.
/// Implement this interface to react to job events (logging, metrics, notifications, etc.)
/// </summary>
/// <typeparam name="TEvent">The type of event to handle</typeparam>
/// <example>
/// <code>
/// public class SlackNotificationHandler : IJobEventHandler&lt;JobFailedEvent&gt;
/// {
///     private readonly ISlackClient _slack;
///
///     public SlackNotificationHandler(ISlackClient slack) => _slack = slack;
///
///     public async Task HandleAsync(JobFailedEvent @event, CancellationToken ct)
///     {
///         if (!@event.WillRetry)
///         {
///             await _slack.SendMessageAsync(
///                 channel: "#alerts",
///                 text: $"Job {@event.JobTypeId} failed permanently: {@event.ErrorMessage}");
///         }
///     }
/// }
/// </code>
/// </example>
public interface IJobEventHandler<in TEvent> where TEvent : IJobEvent
{
    /// <summary>
    /// Handle the event asynchronously
    /// </summary>
    /// <param name="event">The event to handle</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A task representing the asynchronous operation</returns>
    /// <remarks>
    /// Handlers run in the background and do not block job execution.
    /// Exceptions in handlers are logged but do not affect the job.
    /// </remarks>
    Task HandleAsync(TEvent @event, CancellationToken ct = default);
}
