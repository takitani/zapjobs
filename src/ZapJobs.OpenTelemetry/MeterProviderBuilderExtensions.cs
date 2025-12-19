using OpenTelemetry.Metrics;

namespace ZapJobs.OpenTelemetry;

/// <summary>
/// Extension methods for configuring ZapJobs metrics
/// </summary>
public static class MeterProviderBuilderExtensions
{
    /// <summary>
    /// Adds ZapJobs instrumentation to the MeterProviderBuilder
    /// </summary>
    /// <param name="builder">The MeterProviderBuilder</param>
    /// <returns>The builder for chaining</returns>
    /// <example>
    /// <code>
    /// builder.Services.AddOpenTelemetry()
    ///     .WithMetrics(metrics => metrics
    ///         .AddZapJobsInstrumentation()
    ///         .AddPrometheusExporter());
    /// </code>
    /// </example>
    public static MeterProviderBuilder AddZapJobsInstrumentation(this MeterProviderBuilder builder)
    {
        return builder.AddMeter(ZapJobsInstrumentation.MeterName);
    }
}
