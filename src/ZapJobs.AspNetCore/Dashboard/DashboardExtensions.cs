using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

namespace ZapJobs.AspNetCore.Dashboard;

/// <summary>
/// Extension methods for adding ZapJobs Dashboard to ASP.NET Core applications
/// </summary>
public static class DashboardExtensions
{
    /// <summary>
    /// Adds ZapJobs Dashboard services to the service collection.
    /// </summary>
    /// <param name="services">The service collection</param>
    /// <param name="configure">Optional configuration action</param>
    /// <returns>The service collection for chaining</returns>
    public static IServiceCollection AddZapJobsDashboard(
        this IServiceCollection services,
        Action<DashboardOptions>? configure = null)
    {
        var options = new DashboardOptions();
        configure?.Invoke(options);
        options.Validate();

        services.AddSingleton(options);

        return services;
    }

    /// <summary>
    /// Adds the ZapJobs Dashboard middleware to the application pipeline.
    /// </summary>
    /// <param name="app">The application builder</param>
    /// <param name="configure">Optional configuration action to override service-registered options</param>
    /// <returns>The application builder for chaining</returns>
    public static IApplicationBuilder UseZapJobsDashboard(
        this IApplicationBuilder app,
        Action<DashboardOptions>? configure = null)
    {
        var options = app.ApplicationServices.GetService<DashboardOptions>()
                     ?? new DashboardOptions();

        if (configure != null)
        {
            // Create a copy to avoid modifying the registered singleton
            var overrideOptions = new DashboardOptions
            {
                BasePath = options.BasePath,
                DefaultTheme = options.DefaultTheme,
                AllowThemeChange = options.AllowThemeChange,
                Title = options.Title,
                EnableRealTimeUpdates = options.EnableRealTimeUpdates,
                PollingIntervalSeconds = options.PollingIntervalSeconds,
                PageSize = options.PageSize,
                Authorization = options.Authorization
            };
            configure(overrideOptions);
            overrideOptions.Validate();
            options = overrideOptions;
        }
        else
        {
            options.Validate();
        }

        app.UseMiddleware<DashboardMiddleware>(options);

        return app;
    }

    /// <summary>
    /// Adds the ZapJobs Dashboard middleware with a specific base path.
    /// </summary>
    /// <param name="app">The application builder</param>
    /// <param name="basePath">The base path for the dashboard (e.g., "/zapjobs")</param>
    /// <returns>The application builder for chaining</returns>
    public static IApplicationBuilder UseZapJobsDashboard(
        this IApplicationBuilder app,
        string basePath)
    {
        return app.UseZapJobsDashboard(options => options.BasePath = basePath);
    }
}
