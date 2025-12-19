using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using ZapJobs.Core;

namespace ZapJobs.Storage.InMemory;

/// <summary>
/// Extension methods for registering InMemory storage
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Use in-memory storage for ZapJobs (for development and testing)
    /// </summary>
    public static IServiceCollection UseInMemoryStorage(this IServiceCollection services)
    {
        services.TryAddSingleton<InMemoryJobStorage>();
        services.TryAddSingleton<IJobStorage>(sp => sp.GetRequiredService<InMemoryJobStorage>());

        // Register webhook storage
        services.TryAddSingleton<InMemoryWebhookStorage>();
        services.TryAddSingleton<IWebhookStorage>(sp => sp.GetRequiredService<InMemoryWebhookStorage>());

        return services;
    }
}
