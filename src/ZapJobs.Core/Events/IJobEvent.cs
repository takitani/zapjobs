namespace ZapJobs.Core.Events;

/// <summary>
/// Base interface for all job lifecycle events
/// </summary>
public interface IJobEvent
{
    /// <summary>
    /// The unique identifier of the job run
    /// </summary>
    Guid RunId { get; }

    /// <summary>
    /// The job type identifier
    /// </summary>
    string JobTypeId { get; }

    /// <summary>
    /// When the event occurred
    /// </summary>
    DateTimeOffset Timestamp { get; }
}
