using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using ZapJobs.Execution;

namespace ZapJobs.OpenTelemetry;

/// <summary>
/// Extension methods for registering ZapJobs OpenTelemetry instrumentation
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Enables OpenTelemetry instrumentation for ZapJobs.
    /// This decorates the IJobExecutor with tracing and metrics.
    /// </summary>
    /// <param name="builder">The ZapJobsBuilder</param>
    /// <param name="configure">Optional configuration action</param>
    /// <returns>The builder for chaining</returns>
    /// <example>
    /// <code>
    /// services.AddZapJobs()
    ///     .AddJob&lt;MyJob&gt;()
    ///     .AddOpenTelemetry(opts =>
    ///     {
    ///         opts.RecordException = true;
    ///         opts.Filter = run => run.JobTypeId != "health-check";
    ///     });
    /// </code>
    /// </example>
    public static ZapJobsBuilder AddOpenTelemetry(
        this ZapJobsBuilder builder,
        Action<ZapJobsInstrumentationOptions>? configure = null)
    {
        // Configure options
        if (configure != null)
        {
            builder.Services.Configure(configure);
        }
        else
        {
            builder.Services.TryAddSingleton(Options.Create(new ZapJobsInstrumentationOptions()));
        }

        // Decorate IJobExecutor with TracingJobExecutor
        builder.Services.Decorate<IJobExecutor, TracingJobExecutor>();

        return builder;
    }

    /// <summary>
    /// Decorates a service with a decorator implementation.
    /// This is a simple implementation that doesn't require Scrutor.
    /// </summary>
    private static void Decorate<TService, TDecorator>(this IServiceCollection services)
        where TService : class
        where TDecorator : class, TService
    {
        // Find the existing registration
        var existingRegistration = services.LastOrDefault(s => s.ServiceType == typeof(TService));
        if (existingRegistration == null)
        {
            throw new InvalidOperationException(
                $"No service of type {typeof(TService).Name} has been registered. " +
                "Call AddOpenTelemetry() after AddZapJobs().");
        }

        // Remove the existing registration
        services.Remove(existingRegistration);

        // Re-add the original implementation with a different key
        if (existingRegistration.ImplementationType != null)
        {
            services.Add(new ServiceDescriptor(
                existingRegistration.ImplementationType,
                existingRegistration.ImplementationType,
                existingRegistration.Lifetime));

            // Add the decorator that wraps the original
            services.Add(new ServiceDescriptor(
                typeof(TService),
                sp => ActivatorUtilities.CreateInstance<TDecorator>(
                    sp,
                    sp.GetRequiredService(existingRegistration.ImplementationType)),
                existingRegistration.Lifetime));
        }
        else if (existingRegistration.ImplementationFactory != null)
        {
            // For factory registrations
            var originalFactory = existingRegistration.ImplementationFactory;

            services.Add(new ServiceDescriptor(
                typeof(TService),
                sp =>
                {
                    var inner = (TService)originalFactory(sp);
                    return ActivatorUtilities.CreateInstance<TDecorator>(sp, inner);
                },
                existingRegistration.Lifetime));
        }
        else if (existingRegistration.ImplementationInstance != null)
        {
            // For instance registrations
            services.Add(new ServiceDescriptor(
                typeof(TService),
                sp => ActivatorUtilities.CreateInstance<TDecorator>(
                    sp,
                    existingRegistration.ImplementationInstance),
                ServiceLifetime.Singleton));
        }
    }
}
