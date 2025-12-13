namespace ZapJobs.AspNetCore;

/// <summary>
/// Configuration options for the ZapJobs REST API
/// </summary>
public class ZapJobsApiOptions
{
    /// <summary>
    /// The configuration section name
    /// </summary>
    public const string SectionName = "ZapJobsApi";

    /// <summary>
    /// Whether to enable Swagger/OpenAPI documentation
    /// </summary>
    public bool EnableSwagger { get; set; } = true;

    /// <summary>
    /// The route prefix for the API (default: "api/v1")
    /// </summary>
    public string RoutePrefix { get; set; } = "api/v1";

    /// <summary>
    /// Whether to require authentication for API endpoints
    /// </summary>
    public bool RequireAuthentication { get; set; } = true;

    /// <summary>
    /// List of valid API keys for authentication
    /// </summary>
    public List<string> ApiKeys { get; set; } = [];

    /// <summary>
    /// The header name for API key authentication
    /// </summary>
    public string ApiKeyHeaderName { get; set; } = "X-Api-Key";

    /// <summary>
    /// Whether to allow anonymous access when no API key is provided
    /// (only applies if RequireAuthentication is true)
    /// </summary>
    public bool AllowAnonymous { get; set; }
}
