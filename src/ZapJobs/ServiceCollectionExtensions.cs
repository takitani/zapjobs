using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using ZapJobs.Batches;
using ZapJobs.Checkpoints;
using ZapJobs.Core;
using ZapJobs.Core.Checkpoints;
using ZapJobs.Core.Events;
using ZapJobs.DeadLetter;
using ZapJobs.Events;
using ZapJobs.Execution;
using ZapJobs.HostedServices;
using ZapJobs.RateLimiting;
using ZapJobs.Scheduling;
using ZapJobs.Tracking;
using ZapJobs.Webhooks;

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

        // Register scheduler, tracker, and dead letter manager
        services.TryAddSingleton<IJobScheduler, JobSchedulerService>();
        services.TryAddSingleton<IJobTracker, JobTrackerService>();
        services.TryAddSingleton<IJobExecutor, JobExecutor>();
        services.TryAddSingleton<IDeadLetterManager, DeadLetterManager>();

        // Register batch service
        services.TryAddSingleton<BatchService>();
        services.TryAddSingleton<IBatchService>(sp => sp.GetRequiredService<BatchService>());

        // Wire up batch service with executor
        services.AddSingleton<IBatchExecutorInitializer, BatchExecutorInitializer>();

        // Register rate limiter
        services.TryAddSingleton<IRateLimiter, SlidingWindowRateLimiter>();

        // Register checkpoint services
        services.Configure<CheckpointOptions>(_ => { });
        services.TryAddSingleton<ICheckpointStore, CheckpointService>();
        services.AddHostedService<CheckpointCleanupService>();

        // Register event dispatcher and background service
        services.TryAddSingleton<JobEventDispatcher>();
        services.TryAddSingleton<IJobEventDispatcher>(sp => sp.GetRequiredService<JobEventDispatcher>());
        services.AddHostedService<JobEventBackgroundService>();

        // Register webhook services
        services.AddHttpClient("ZapJobsWebhook");
        services.TryAddSingleton<WebhookService>();
        services.TryAddSingleton<IWebhookService>(sp => sp.GetRequiredService<WebhookService>());
        services.AddHostedService<WebhookDeliveryService>();

        // Register heartbeat and processor
        services.TryAddSingleton<HeartbeatService>();
        services.AddHostedService(sp => sp.GetRequiredService<HeartbeatService>());
        services.AddHostedService<JobRegistrationHostedService>();
        services.AddHostedService<JobProcessorHostedService>();
        services.AddHostedService<RateLimitCleanupService>();

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
        services.TryAddSingleton<IDeadLetterManager, DeadLetterManager>();

        // Register batch service
        services.TryAddSingleton<BatchService>();
        services.TryAddSingleton<IBatchService>(sp => sp.GetRequiredService<BatchService>());
        services.AddSingleton<IBatchExecutorInitializer, BatchExecutorInitializer>();

        // Register rate limiter
        services.TryAddSingleton<IRateLimiter, SlidingWindowRateLimiter>();

        // Register checkpoint services
        services.Configure<CheckpointOptions>(configuration.GetSection("ZapJobs:Checkpoints"));
        services.TryAddSingleton<ICheckpointStore, CheckpointService>();
        services.AddHostedService<CheckpointCleanupService>();

        // Register event dispatcher and background service
        services.TryAddSingleton<JobEventDispatcher>();
        services.TryAddSingleton<IJobEventDispatcher>(sp => sp.GetRequiredService<JobEventDispatcher>());
        services.AddHostedService<JobEventBackgroundService>();

        // Register webhook services
        services.AddHttpClient("ZapJobsWebhook");
        services.TryAddSingleton<WebhookService>();
        services.TryAddSingleton<IWebhookService>(sp => sp.GetRequiredService<WebhookService>());
        services.AddHostedService<WebhookDeliveryService>();

        services.TryAddSingleton<HeartbeatService>();
        services.AddHostedService(sp => sp.GetRequiredService<HeartbeatService>());
        services.AddHostedService<JobRegistrationHostedService>();
        services.AddHostedService<JobProcessorHostedService>();
        services.AddHostedService<RateLimitCleanupService>();

        return new ZapJobsBuilder(services);
    }
}

/// <summary>
/// Interface for initializing batch-executor integration
/// </summary>
public interface IBatchExecutorInitializer
{
    void Initialize();
}

/// <summary>
/// Wires up BatchService with JobExecutor
/// </summary>
internal class BatchExecutorInitializer : IBatchExecutorInitializer
{
    private readonly IJobExecutor _executor;
    private readonly BatchService _batchService;

    public BatchExecutorInitializer(IJobExecutor executor, BatchService batchService)
    {
        _executor = executor;
        _batchService = batchService;
    }

    public void Initialize()
    {
        if (_executor is JobExecutor jobExecutor)
        {
            jobExecutor.SetBatchService(_batchService);
        }
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
    /// Register a job type with configuration
    /// </summary>
    public ZapJobsBuilder AddJob<TJob>(Action<JobDefinitionBuilder> configure) where TJob : class, IJob
    {
        Services.AddTransient<TJob>();

        var builder = new JobDefinitionBuilder();
        configure(builder);

        // Register job type with executor on startup, including configuration
        Services.AddSingleton<IJobRegistration>(sp => new JobRegistration<TJob>(builder));

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

    /// <summary>
    /// Register a job type with a factory and configuration
    /// </summary>
    public ZapJobsBuilder AddJob<TJob>(Func<IServiceProvider, TJob> factory, Action<JobDefinitionBuilder> configure) where TJob : class, IJob
    {
        Services.AddTransient(factory);

        var builder = new JobDefinitionBuilder();
        configure(builder);

        Services.AddSingleton<IJobRegistration>(sp => new JobRegistration<TJob>(builder));

        return this;
    }

    /// <summary>
    /// Configure a rate limit for a specific queue
    /// </summary>
    /// <param name="queue">Queue name</param>
    /// <param name="policy">Rate limit policy to apply</param>
    public ZapJobsBuilder UseQueueRateLimit(string queue, RateLimitPolicy policy)
    {
        Services.Configure<ZapJobsOptions>(options =>
        {
            options.QueueRateLimits[queue] = policy;
        });

        return this;
    }

    /// <summary>
    /// Configure a rate limit for a specific queue
    /// </summary>
    /// <param name="queue">Queue name</param>
    /// <param name="limit">Maximum executions within the window</param>
    /// <param name="window">Time window for the rate limit</param>
    public ZapJobsBuilder UseQueueRateLimit(string queue, int limit, TimeSpan window)
    {
        return UseQueueRateLimit(queue, RateLimitPolicy.Create(limit, window));
    }

    /// <summary>
    /// Enable webhooks for job events. Requires IWebhookStorage to be registered.
    /// This registers the WebhookEventHandler to publish job events to webhook subscribers.
    /// </summary>
    /// <param name="configure">Optional configuration for webhook options</param>
    /// <example>
    /// <code>
    /// services.AddZapJobs()
    ///     .UseWebhooks(options => {
    ///         options.MaxRetryAttempts = 3;
    ///         options.RequestTimeout = TimeSpan.FromSeconds(30);
    ///     });
    /// </code>
    /// </example>
    public ZapJobsBuilder UseWebhooks(Action<WebhookOptions>? configure = null)
    {
        if (configure != null)
        {
            var options = new WebhookOptions();
            configure(options);
            Services.TryAddSingleton(options);
        }
        else
        {
            Services.TryAddSingleton(new WebhookOptions());
        }

        // Register webhook event handler for all job events
        AddEventHandler<WebhookEventHandler>();

        return this;
    }

    /// <summary>
    /// Register an event handler for job lifecycle events.
    /// Handlers are called asynchronously in the background and do not block job execution.
    /// </summary>
    /// <typeparam name="THandler">The handler type that implements one or more IJobEventHandler&lt;TEvent&gt; interfaces</typeparam>
    /// <example>
    /// <code>
    /// services.AddZapJobs()
    ///     .AddEventHandler&lt;SlackNotificationHandler&gt;()
    ///     .AddEventHandler&lt;MetricsHandler&gt;();
    /// </code>
    /// </example>
    public ZapJobsBuilder AddEventHandler<THandler>() where THandler : class
    {
        var handlerType = typeof(THandler);
        var handlerInterfaces = handlerType.GetInterfaces()
            .Where(i => i.IsGenericType &&
                        i.GetGenericTypeDefinition() == typeof(IJobEventHandler<>));

        if (!handlerInterfaces.Any())
        {
            throw new ArgumentException(
                $"Type {handlerType.Name} does not implement any IJobEventHandler<TEvent> interfaces",
                nameof(THandler));
        }

        // Register the handler for each event type it handles
        foreach (var handlerInterface in handlerInterfaces)
        {
            Services.AddScoped(handlerInterface, handlerType);
        }

        return this;
    }
}

/// <summary>
/// Marker interface for job registration
/// </summary>
public interface IJobRegistration
{
    Type JobType { get; }
    JobDefinitionBuilder? Builder { get; }
}

/// <summary>
/// Job registration implementation
/// </summary>
internal class JobRegistration<TJob> : IJobRegistration where TJob : IJob
{
    public Type JobType => typeof(TJob);
    public JobDefinitionBuilder? Builder { get; }

    public JobRegistration(JobDefinitionBuilder? builder = null)
    {
        Builder = builder;
    }
}
