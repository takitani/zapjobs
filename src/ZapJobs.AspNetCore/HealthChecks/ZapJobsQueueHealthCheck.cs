using Microsoft.Extensions.Diagnostics.HealthChecks;
using ZapJobs.Core;

namespace ZapJobs.AspNetCore.HealthChecks;

/// <summary>
/// Health check that verifies queue backlog is within acceptable limits
/// </summary>
public class ZapJobsQueueHealthCheck : IHealthCheck
{
    private readonly IJobStorage _storage;
    private readonly ZapJobsHealthOptions _options;

    public ZapJobsQueueHealthCheck(IJobStorage storage, ZapJobsHealthOptions options)
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
            var stats = await _storage.GetStatsAsync(ct);

            if (stats.PendingRuns > _options.MaxPendingJobsUnhealthy)
            {
                return HealthCheckResult.Unhealthy(
                    description: $"Queue backlog too large: {stats.PendingRuns} pending jobs",
                    data: new Dictionary<string, object>
                    {
                        ["pending_runs"] = stats.PendingRuns,
                        ["threshold"] = _options.MaxPendingJobsUnhealthy
                    });
            }

            if (stats.PendingRuns > _options.MaxPendingJobsDegraded)
            {
                return HealthCheckResult.Degraded(
                    description: $"Queue backlog growing: {stats.PendingRuns} pending jobs",
                    data: new Dictionary<string, object>
                    {
                        ["pending_runs"] = stats.PendingRuns,
                        ["threshold"] = _options.MaxPendingJobsDegraded
                    });
            }

            return HealthCheckResult.Healthy(
                description: $"{stats.PendingRuns} pending jobs",
                data: new Dictionary<string, object>
                {
                    ["pending_runs"] = stats.PendingRuns
                });
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy(
                description: "Failed to check queue health",
                exception: ex);
        }
    }
}
