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
        var options = new PostgreSqlStorageOptions { ConnectionString = connectionString };
        services.TryAddSingleton(new PostgreSqlJobStorage(options));
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

        services.TryAddSingleton(new PostgreSqlJobStorage(options));
        services.TryAddSingleton<IJobStorage>(sp => sp.GetRequiredService<PostgreSqlJobStorage>());

        return services;
    }
}

/// <summary>
/// Options for PostgreSQL storage
/// </summary>
public class PostgreSqlStorageOptions
{
    /// <summary>
    /// PostgreSQL connection string
    /// </summary>
    public string ConnectionString { get; set; } = string.Empty;

    /// <summary>
    /// Schema name for ZapJobs tables. Default: "zapjobs"
    /// </summary>
    public string Schema { get; set; } = "zapjobs";

    /// <summary>
    /// Whether to automatically create schema and tables on startup. Default: true
    /// </summary>
    public bool AutoMigrate { get; set; } = true;
}
