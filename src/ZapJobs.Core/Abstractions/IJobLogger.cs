namespace ZapJobs.Core;

/// <summary>
/// Logger for job execution with persistence to storage
/// </summary>
public interface IJobLogger
{
    /// <summary>Log a trace message</summary>
    Task TraceAsync(string message, string? category = null, object? context = null, int? durationMs = null);

    /// <summary>Log a debug message</summary>
    Task DebugAsync(string message, string? category = null, object? context = null, int? durationMs = null);

    /// <summary>Log an info message</summary>
    Task InfoAsync(string message, string? category = null, object? context = null, int? durationMs = null);

    /// <summary>Log a warning message</summary>
    Task WarningAsync(string message, string? category = null, object? context = null, int? durationMs = null);

    /// <summary>Log an error message</summary>
    Task ErrorAsync(string message, string? category = null, object? context = null, Exception? exception = null, int? durationMs = null);

    /// <summary>Log a critical error message</summary>
    Task CriticalAsync(string message, string? category = null, object? context = null, Exception? exception = null, int? durationMs = null);
}

/// <summary>
/// Factory for creating job loggers
/// </summary>
public interface IJobLoggerFactory
{
    /// <summary>Create a logger for a specific run</summary>
    IJobLogger CreateLogger(Guid runId);
}
