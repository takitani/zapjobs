using System.Text.Json;
using ZapJobs.Core;

namespace ZapJobs.Tracking;

/// <summary>
/// Job logger that persists logs to storage
/// </summary>
public class JobLoggerService : IJobLogger
{
    private readonly IJobStorage _storage;
    private readonly Guid _runId;

    public JobLoggerService(IJobStorage storage, Guid runId)
    {
        _storage = storage;
        _runId = runId;
    }

    public Task TraceAsync(string message, string? category = null, object? context = null, int? durationMs = null)
        => LogAsync(JobLogLevel.Trace, message, category, context, null, durationMs);

    public Task DebugAsync(string message, string? category = null, object? context = null, int? durationMs = null)
        => LogAsync(JobLogLevel.Debug, message, category, context, null, durationMs);

    public Task InfoAsync(string message, string? category = null, object? context = null, int? durationMs = null)
        => LogAsync(JobLogLevel.Info, message, category, context, null, durationMs);

    public Task WarningAsync(string message, string? category = null, object? context = null, int? durationMs = null)
        => LogAsync(JobLogLevel.Warning, message, category, context, null, durationMs);

    public Task ErrorAsync(string message, string? category = null, object? context = null, Exception? exception = null, int? durationMs = null)
        => LogAsync(JobLogLevel.Error, message, category, context, exception, durationMs);

    public Task CriticalAsync(string message, string? category = null, object? context = null, Exception? exception = null, int? durationMs = null)
        => LogAsync(JobLogLevel.Critical, message, category, context, exception, durationMs);

    private async Task LogAsync(JobLogLevel level, string message, string? category, object? context, Exception? exception, int? durationMs)
    {
        var log = new JobLog
        {
            RunId = _runId,
            Level = level,
            Message = message,
            Category = category,
            ContextJson = context != null ? JsonSerializer.Serialize(context) : null,
            Exception = exception?.ToString(),
            DurationMs = durationMs,
            Timestamp = DateTime.UtcNow
        };

        await _storage.AddLogAsync(log, default);
    }
}

/// <summary>
/// Factory for creating job loggers
/// </summary>
public class JobLoggerFactory : IJobLoggerFactory
{
    private readonly IJobStorage _storage;

    public JobLoggerFactory(IJobStorage storage)
    {
        _storage = storage;
    }

    public IJobLogger CreateLogger(Guid runId)
    {
        return new JobLoggerService(_storage, runId);
    }
}
