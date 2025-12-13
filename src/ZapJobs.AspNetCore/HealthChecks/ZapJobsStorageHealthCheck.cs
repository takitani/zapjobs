using Microsoft.Extensions.Diagnostics.HealthChecks;
using ZapJobs.Core;

namespace ZapJobs.AspNetCore.HealthChecks;

/// <summary>
/// Health check that verifies storage/database connectivity
/// </summary>
public class ZapJobsStorageHealthCheck : IHealthCheck
{
    private readonly IJobStorage _storage;

    public ZapJobsStorageHealthCheck(IJobStorage storage)
    {
        _storage = storage;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken ct = default)
    {
        try
        {
            var stats = await _storage.GetStatsAsync(ct);

            return HealthCheckResult.Healthy(
                description: "Storage is accessible",
                data: new Dictionary<string, object>
                {
                    ["total_jobs"] = stats.TotalJobs,
                    ["total_runs"] = stats.TotalRuns,
                    ["pending_runs"] = stats.PendingRuns
                });
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy(
                description: "Storage is not accessible",
                exception: ex);
        }
    }
}
