using System.Reflection;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.OpenApi.Models;
using ZapJobs.AspNetCore.Api.Auth;

namespace ZapJobs.AspNetCore;

/// <summary>
/// Extension methods for adding ZapJobs REST API to ASP.NET Core applications
/// </summary>
public static class ApiServiceCollectionExtensions
{
    /// <summary>
    /// Add ZapJobs REST API services
    /// </summary>
    /// <param name="services">The service collection</param>
    /// <param name="configure">Optional configuration action</param>
    /// <returns>The service collection for chaining</returns>
    public static IServiceCollection AddZapJobsApi(
        this IServiceCollection services,
        Action<ZapJobsApiOptions>? configure = null)
    {
        var options = new ZapJobsApiOptions();
        configure?.Invoke(options);

        services.AddSingleton(options);

        // Add controllers from this assembly
        services.AddControllers()
            .AddApplicationPart(typeof(ApiServiceCollectionExtensions).Assembly);

        // Add authentication
        if (options.RequireAuthentication)
        {
            services.AddAuthentication(ApiKeyAuthOptions.SchemeName)
                .AddScheme<ApiKeyAuthOptions, ApiKeyAuthHandler>(
                    ApiKeyAuthOptions.SchemeName,
                    authOptions =>
                    {
                        authOptions.HeaderName = options.ApiKeyHeaderName;
                        authOptions.ValidApiKeys = [.. options.ApiKeys];
                        authOptions.AllowAnonymous = options.AllowAnonymous;
                    });

            services.AddAuthorization(authOptions =>
            {
                authOptions.DefaultPolicy = new AuthorizationPolicyBuilder()
                    .RequireAuthenticatedUser()
                    .AddAuthenticationSchemes(ApiKeyAuthOptions.SchemeName)
                    .Build();
            });
        }
        else
        {
            // Add a no-op authentication that always succeeds
            services.AddAuthentication("Anonymous")
                .AddScheme<AuthenticationSchemeOptions, AnonymousAuthHandler>("Anonymous", null);

            services.AddAuthorization(authOptions =>
            {
                authOptions.DefaultPolicy = new AuthorizationPolicyBuilder()
                    .AddAuthenticationSchemes("Anonymous")
                    .RequireAssertion(_ => true)
                    .Build();
            });
        }

        // Add Swagger
        if (options.EnableSwagger)
        {
            services.AddEndpointsApiExplorer();
            services.AddSwaggerGen(c =>
            {
                c.SwaggerDoc("v1", new OpenApiInfo
                {
                    Title = "ZapJobs API",
                    Version = "v1",
                    Description = "REST API for ZapJobs job scheduler. Provides endpoints for managing jobs, runs, workers, and more.",
                    Contact = new OpenApiContact
                    {
                        Name = "ZapJobs",
                        Url = new Uri("https://github.com/takitani/zapjobs")
                    }
                });

                // Include XML comments if available
                var xmlFile = $"{Assembly.GetExecutingAssembly().GetName().Name}.xml";
                var xmlPath = Path.Combine(AppContext.BaseDirectory, xmlFile);
                if (File.Exists(xmlPath))
                {
                    c.IncludeXmlComments(xmlPath);
                }

                // Add API Key authentication to Swagger
                if (options.RequireAuthentication)
                {
                    c.AddSecurityDefinition("ApiKey", new OpenApiSecurityScheme
                    {
                        Type = SecuritySchemeType.ApiKey,
                        In = ParameterLocation.Header,
                        Name = options.ApiKeyHeaderName,
                        Description = "API Key authentication. Enter your API key in the header."
                    });

                    c.AddSecurityRequirement(new OpenApiSecurityRequirement
                    {
                        {
                            new OpenApiSecurityScheme
                            {
                                Reference = new OpenApiReference
                                {
                                    Type = ReferenceType.SecurityScheme,
                                    Id = "ApiKey"
                                }
                            },
                            Array.Empty<string>()
                        }
                    });
                }
            });
        }

        return services;
    }

    /// <summary>
    /// Add ZapJobs REST API services with configuration from IConfiguration
    /// </summary>
    /// <param name="services">The service collection</param>
    /// <param name="configuration">The configuration</param>
    /// <returns>The service collection for chaining</returns>
    public static IServiceCollection AddZapJobsApi(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var section = configuration.GetSection(ZapJobsApiOptions.SectionName);

        return services.AddZapJobsApi(options =>
        {
            section.Bind(options);
        });
    }

    /// <summary>
    /// Configure the application to use ZapJobs API middleware
    /// </summary>
    /// <param name="app">The application builder</param>
    /// <returns>The application builder for chaining</returns>
    public static IApplicationBuilder UseZapJobsApi(this IApplicationBuilder app)
    {
        var options = app.ApplicationServices.GetService<ZapJobsApiOptions>()
            ?? new ZapJobsApiOptions();

        if (options.EnableSwagger)
        {
            app.UseSwagger();
            app.UseSwaggerUI(c =>
            {
                c.SwaggerEndpoint("/swagger/v1/swagger.json", "ZapJobs API v1");
                c.RoutePrefix = "swagger";
            });
        }

        app.UseAuthentication();
        app.UseAuthorization();

        return app;
    }

    /// <summary>
    /// Configure the WebApplication to use ZapJobs API with endpoint mapping
    /// </summary>
    /// <param name="app">The web application</param>
    /// <returns>The web application for chaining</returns>
    public static WebApplication UseZapJobsApi(this WebApplication app)
    {
        ((IApplicationBuilder)app).UseZapJobsApi();

        app.MapControllers();

        return app;
    }
}

/// <summary>
/// Anonymous authentication handler for when authentication is disabled
/// </summary>
internal class AnonymousAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public AnonymousAuthHandler(
        Microsoft.Extensions.Options.IOptionsMonitor<AuthenticationSchemeOptions> options,
        Microsoft.Extensions.Logging.ILoggerFactory logger,
        System.Text.Encodings.Web.UrlEncoder encoder)
        : base(options, logger, encoder)
    {
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var claims = new[] { new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.Name, "anonymous") };
        var identity = new System.Security.Claims.ClaimsIdentity(claims, Scheme.Name);
        var principal = new System.Security.Claims.ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, Scheme.Name);

        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
