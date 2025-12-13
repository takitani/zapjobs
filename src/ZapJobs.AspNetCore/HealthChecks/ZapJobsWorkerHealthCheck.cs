using Microsoft.Extensions.Diagnostics.HealthChecks;
using ZapJobs.Core;

namespace ZapJobs.AspNetCore.HealthChecks;

/// <summary>
/// Health check that verifies active workers are processing jobs
/// </summary>
public class ZapJobsWorkerHealthCheck : IHealthCheck
{
    private readonly IJobStorage _storage;
    private readonly ZapJobsHealthOptions _options;

    public ZapJobsWorkerHealthCheck(IJobStorage storage, ZapJobsHealthOptions options)
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
            var heartbeats = await _storage.GetHeartbeatsAsync(ct);
            var activeWorkers = heartbeats.Count(h =>
                DateTime.UtcNow - h.Timestamp < _options.ActiveWorkerThreshold);

            if (activeWorkers == 0 && _options.RequireActiveWorker)
            {
                return HealthCheckResult.Unhealthy(
                    description: "No active workers",
                    data: new Dictionary<string, object>
                    {
                        ["total_workers"] = heartbeats.Count,
                        ["active_workers"] = 0
                    });
            }

            if (activeWorkers < _options.MinimumWorkers)
            {
                return HealthCheckResult.Degraded(
                    description: $"Only {activeWorkers} active workers (minimum: {_options.MinimumWorkers})",
                    data: new Dictionary<string, object>
                    {
                        ["active_workers"] = activeWorkers,
                        ["minimum_workers"] = _options.MinimumWorkers
                    });
            }

            return HealthCheckResult.Healthy(
                description: $"{activeWorkers} active workers",
                data: new Dictionary<string, object>
                {
                    ["active_workers"] = activeWorkers
                });
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy(
                description: "Failed to check worker health",
                exception: ex);
        }
    }
}
