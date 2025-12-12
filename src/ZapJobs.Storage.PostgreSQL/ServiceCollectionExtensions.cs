using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using ZapJobs.Core;

namespace ZapJobs.Storage.PostgreSQL;

/// <summary>
/// Extension methods for registering PostgreSQL storage
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Use PostgreSQL storage for ZapJobs
    /// </summary>
    public static IServiceCollection UsePostgreSqlStorage(this IServiceCollection services, string connectionString)
    {
        services.TryAddSingleton(new PostgreSqlJobStorage(connectionString));
        services.TryAddSingleton<IJobStorage>(sp => sp.GetRequiredService<PostgreSqlJobStorage>());
        return services;
    }

    /// <summary>
    /// Use PostgreSQL storage with configuration callback
    /// </summary>
    public static IServiceCollection UsePostgreSqlStorage(
        this IServiceCollection services,
        Action<PostgreSqlStorageOptions> configure)
    {
        var options = new PostgreSqlStorageOptions();
        configure(options);

        services.TryAddSingleton(new PostgreSqlJobStorage(options.ConnectionString));
        services.TryAddSingleton<IJobStorage>(sp => sp.GetRequiredService<PostgreSqlJobStorage>());

        return services;
    }
}

/// <summary>
/// Options for PostgreSQL storage
/// </summary>
public class PostgreSqlStorageOptions
{
    public string ConnectionString { get; set; } = string.Empty;
    public bool AutoMigrate { get; set; } = true;
}
