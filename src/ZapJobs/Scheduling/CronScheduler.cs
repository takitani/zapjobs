using Cronos;
using Microsoft.Extensions.Options;
using ZapJobs.Core;

namespace ZapJobs.Scheduling;

/// <summary>
/// CRON expression parser and scheduler using Cronos
/// </summary>
public class CronScheduler : ICronScheduler
{
    private readonly TimeZoneInfo _defaultTimeZone;

    public CronScheduler(IOptions<ZapJobsOptions> options)
    {
        _defaultTimeZone = options.Value.GetDefaultTimeZone();
    }

    /// <summary>
    /// Get the next occurrence of a CRON expression
    /// </summary>
    public DateTime? GetNextOccurrence(string cronExpression, DateTime fromUtc, TimeZoneInfo? timeZone = null)
    {
        var tz = timeZone ?? _defaultTimeZone;
        var cron = ParseExpression(cronExpression);
        return cron.GetNextOccurrence(fromUtc, tz);
    }

    /// <summary>
    /// Get multiple next occurrences of a CRON expression
    /// </summary>
    public IEnumerable<DateTime> GetNextOccurrences(string cronExpression, DateTime fromUtc, DateTime toUtc, TimeZoneInfo? timeZone = null)
    {
        var tz = timeZone ?? _defaultTimeZone;
        var cron = ParseExpression(cronExpression);
        return cron.GetOccurrences(fromUtc, toUtc, tz);
    }

    /// <summary>
    /// Validate a CRON expression
    /// </summary>
    public bool IsValidExpression(string cronExpression)
    {
        try
        {
            ParseExpression(cronExpression);
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Get a human-readable description of a CRON expression
    /// </summary>
    public string GetDescription(string cronExpression)
    {
        // TODO: Could integrate CronExpressionDescriptor NuGet for better descriptions
        var cron = ParseExpression(cronExpression);
        var next = cron.GetNextOccurrence(DateTime.UtcNow, _defaultTimeZone);
        return next.HasValue
            ? $"Next: {next.Value:yyyy-MM-dd HH:mm:ss} ({_defaultTimeZone.Id})"
            : "No upcoming occurrence";
    }

    private static CronExpression ParseExpression(string cronExpression)
    {
        // Determine format based on number of parts
        var parts = cronExpression.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var format = parts.Length == 6 ? CronFormat.IncludeSeconds : CronFormat.Standard;
        return CronExpression.Parse(cronExpression, format);
    }
}

/// <summary>
/// Interface for CRON scheduling operations
/// </summary>
public interface ICronScheduler
{
    DateTime? GetNextOccurrence(string cronExpression, DateTime fromUtc, TimeZoneInfo? timeZone = null);
    IEnumerable<DateTime> GetNextOccurrences(string cronExpression, DateTime fromUtc, DateTime toUtc, TimeZoneInfo? timeZone = null);
    bool IsValidExpression(string cronExpression);
    string GetDescription(string cronExpression);
}
