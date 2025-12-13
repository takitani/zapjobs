using Microsoft.Extensions.Diagnostics.HealthChecks;
using ZapJobs.Core;

namespace ZapJobs.AspNetCore.HealthChecks;

/// <summary>
/// Health check that verifies dead letter queue is within acceptable limits
/// </summary>
public class ZapJobsDeadLetterHealthCheck : IHealthCheck
{
    private readonly IJobStorage _storage;
    private readonly ZapJobsHealthOptions _options;

    public ZapJobsDeadLetterHealthCheck(IJobStorage storage, ZapJobsHealthOptions options)
    {
        _storage = storage;
        _options = options;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken ct = default)
    {
        try
        {
            var dlqCount = await _storage.GetDeadLetterCountAsync(ct);

            if (dlqCount > _options.MaxDeadLetterUnhealthy)
            {
                return HealthCheckResult.Unhealthy(
                    description: $"Dead letter queue has {dlqCount} entries",
                    data: new Dictionary<string, object>
                    {
                        ["dead_letter_count"] = dlqCount,
                        ["threshold"] = _options.MaxDeadLetterUnhealthy
                    });
            }

            if (dlqCount > _options.MaxDeadLetterDegraded)
            {
                return HealthCheckResult.Degraded(
                    description: $"Dead letter queue growing: {dlqCount} entries",
                    data: new Dictionary<string, object>
                    {
                        ["dead_letter_count"] = dlqCount,
                        ["threshold"] = _options.MaxDeadLetterDegraded
                    });
            }

            return HealthCheckResult.Healthy(
                description: $"Dead letter queue has {dlqCount} entries",
                data: new Dictionary<string, object>
                {
                    ["dead_letter_count"] = dlqCount
                });
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy(
                description: "Failed to check dead letter queue health",
                exception: ex);
        }
    }
}
