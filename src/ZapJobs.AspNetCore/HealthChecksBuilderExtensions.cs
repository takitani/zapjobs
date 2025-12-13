using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using ZapJobs.AspNetCore.HealthChecks;

namespace ZapJobs.AspNetCore;

/// <summary>
/// Extension methods for adding ZapJobs health checks to IHealthChecksBuilder
/// </summary>
public static class HealthChecksBuilderExtensions
{
    /// <summary>
    /// Adds ZapJobs health checks to the health checks builder
    /// </summary>
    /// <param name="builder">The health checks builder</param>
    /// <param name="configure">Optional configuration for health check options</param>
    /// <returns>The health checks builder for chaining</returns>
    public static IHealthChecksBuilder AddZapJobsHealthChecks(
        this IHealthChecksBuilder builder,
        Action<ZapJobsHealthOptions>? configure = null)
    {
        var options = new ZapJobsHealthOptions();
        configure?.Invoke(options);

        builder.Services.AddSingleton(options);

        return builder
            .AddCheck<ZapJobsStorageHealthCheck>(
                "zapjobs-storage",
                tags: ["zapjobs", "storage", "db"])
            .AddCheck<ZapJobsWorkerHealthCheck>(
                "zapjobs-workers",
                tags: ["zapjobs", "workers"])
            .AddCheck<ZapJobsQueueHealthCheck>(
                "zapjobs-queue",
                tags: ["zapjobs", "queue"])
            .AddCheck<ZapJobsDeadLetterHealthCheck>(
                "zapjobs-deadletter",
                tags: ["zapjobs", "deadletter"]);
    }

    /// <summary>
    /// Adds only the ZapJobs storage health check (useful for liveness probes)
    /// </summary>
    /// <param name="builder">The health checks builder</param>
    /// <returns>The health checks builder for chaining</returns>
    public static IHealthChecksBuilder AddZapJobsStorageHealthCheck(
        this IHealthChecksBuilder builder)
    {
        return builder
            .AddCheck<ZapJobsStorageHealthCheck>(
                "zapjobs-storage",
                tags: ["zapjobs", "storage", "db"]);
    }
}
