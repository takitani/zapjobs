using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using ZapJobs.Core;
using ZapJobs.Execution;
using ZapJobs.HostedServices;
using ZapJobs.Scheduling;
using ZapJobs.Tracking;

namespace ZapJobs;

/// <summary>
/// Extension methods for registering ZapJobs services
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Add ZapJobs services to the service collection
    /// </summary>
    public static ZapJobsBuilder AddZapJobs(
        this IServiceCollection services,
        Action<ZapJobsOptions>? configure = null)
    {
        // Configure options
        if (configure != null)
        {
            services.Configure(configure);
        }
        else
        {
            services.Configure<ZapJobsOptions>(_ => { });
        }

        // Register core services
        services.TryAddSingleton<ICronScheduler, CronScheduler>();
        services.TryAddSingleton<RetryHandler>();
        services.TryAddSingleton<IJobLoggerFactory, JobLoggerFactory>();

        // Register scheduler and tracker
        services.TryAddSingleton<IJobScheduler, JobSchedulerService>();
        services.TryAddSingleton<IJobTracker, JobTrackerService>();
        services.TryAddSingleton<IJobExecutor, JobExecutor>();

        // Register heartbeat and processor
        services.TryAddSingleton<HeartbeatService>();
        services.AddHostedService(sp => sp.GetRequiredService<HeartbeatService>());
        services.AddHostedService<JobRegistrationHostedService>();
        services.AddHostedService<JobProcessorHostedService>();

        return new ZapJobsBuilder(services);
    }

    /// <summary>
    /// Add ZapJobs services with configuration from IConfiguration
    /// </summary>
    public static ZapJobsBuilder AddZapJobs(
        this IServiceCollection services,
        Microsoft.Extensions.Configuration.IConfiguration configuration)
    {
        services.Configure<ZapJobsOptions>(configuration.GetSection(ZapJobsOptions.SectionName));

        // Register same services
        services.TryAddSingleton<ICronScheduler, CronScheduler>();
        services.TryAddSingleton<RetryHandler>();
        services.TryAddSingleton<IJobLoggerFactory, JobLoggerFactory>();
        services.TryAddSingleton<IJobScheduler, JobSchedulerService>();
        services.TryAddSingleton<IJobTracker, JobTrackerService>();
        services.TryAddSingleton<IJobExecutor, JobExecutor>();
        services.TryAddSingleton<HeartbeatService>();
        services.AddHostedService(sp => sp.GetRequiredService<HeartbeatService>());
        services.AddHostedService<JobRegistrationHostedService>();
        services.AddHostedService<JobProcessorHostedService>();

        return new ZapJobsBuilder(services);
    }
}

/// <summary>
/// Builder for configuring ZapJobs
/// </summary>
public class ZapJobsBuilder
{
    public IServiceCollection Services { get; }

    public ZapJobsBuilder(IServiceCollection services)
    {
        Services = services;
    }

    /// <summary>
    /// Register a job type
    /// </summary>
    public ZapJobsBuilder AddJob<TJob>() where TJob : class, IJob
    {
        Services.AddTransient<TJob>();

        // Register job type with executor on startup
        Services.AddSingleton<IJobRegistration>(sp => new JobRegistration<TJob>());

        return this;
    }

    /// <summary>
    /// Register a job type with a factory
    /// </summary>
    public ZapJobsBuilder AddJob<TJob>(Func<IServiceProvider, TJob> factory) where TJob : class, IJob
    {
        Services.AddTransient(factory);
        Services.AddSingleton<IJobRegistration>(sp => new JobRegistration<TJob>());

        return this;
    }
}

/// <summary>
/// Marker interface for job registration
/// </summary>
public interface IJobRegistration
{
    Type JobType { get; }
}

/// <summary>
/// Job registration implementation
/// </summary>
internal class JobRegistration<TJob> : IJobRegistration where TJob : IJob
{
    public Type JobType => typeof(TJob);
}
