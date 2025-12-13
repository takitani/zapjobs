using Microsoft.AspNetCore.Authentication;

namespace ZapJobs.AspNetCore.Api.Auth;

/// <summary>
/// Options for API Key authentication
/// </summary>
public class ApiKeyAuthOptions : AuthenticationSchemeOptions
{
    /// <summary>
    /// The authentication scheme name
    /// </summary>
    public const string SchemeName = "ApiKey";

    /// <summary>
    /// The header name to look for the API key
    /// </summary>
    public string HeaderName { get; set; } = "X-Api-Key";

    /// <summary>
    /// Set of valid API keys
    /// </summary>
    public HashSet<string> ValidApiKeys { get; set; } = [];

    /// <summary>
    /// Whether to allow anonymous access when no API key is provided
    /// </summary>
    public bool AllowAnonymous { get; set; }
}
