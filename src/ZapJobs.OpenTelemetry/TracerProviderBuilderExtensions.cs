using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry.Trace;

namespace ZapJobs.OpenTelemetry;

/// <summary>
/// Extension methods for configuring ZapJobs tracing
/// </summary>
public static class TracerProviderBuilderExtensions
{
    /// <summary>
    /// Adds ZapJobs instrumentation to the TracerProviderBuilder
    /// </summary>
    /// <param name="builder">The TracerProviderBuilder</param>
    /// <param name="configure">Optional configuration action</param>
    /// <returns>The builder for chaining</returns>
    /// <example>
    /// <code>
    /// builder.Services.AddOpenTelemetry()
    ///     .WithTracing(tracing => tracing
    ///         .AddZapJobsInstrumentation(opts =>
    ///         {
    ///             opts.RecordException = true;
    ///             opts.Filter = run => run.JobTypeId != "health-check";
    ///         }));
    /// </code>
    /// </example>
    public static TracerProviderBuilder AddZapJobsInstrumentation(
        this TracerProviderBuilder builder,
        Action<ZapJobsInstrumentationOptions>? configure = null)
    {
        var options = new ZapJobsInstrumentationOptions();
        configure?.Invoke(options);

        // Configure options in DI
        builder.ConfigureServices(services =>
        {
            services.Configure<ZapJobsInstrumentationOptions>(opt =>
            {
                opt.RecordException = options.RecordException;
                opt.Enrich = options.Enrich;
                opt.Filter = options.Filter;
                opt.RecordInput = options.RecordInput;
                opt.RecordOutput = options.RecordOutput;
                opt.MaxPayloadLength = options.MaxPayloadLength;
            });
        });

        // Add the ActivitySource
        return builder.AddSource(ZapJobsInstrumentation.ActivitySourceName);
    }
}
