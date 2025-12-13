using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ZapJobs.AspNetCore.Api.Auth;

/// <summary>
/// Authentication handler for API Key authentication
/// </summary>
public class ApiKeyAuthHandler : AuthenticationHandler<ApiKeyAuthOptions>
{
    public ApiKeyAuthHandler(
        IOptionsMonitor<ApiKeyAuthOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : base(options, logger, encoder)
    {
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(Options.HeaderName, out var apiKeyHeader))
        {
            if (Options.AllowAnonymous)
            {
                return Task.FromResult(AuthenticateResult.NoResult());
            }

            return Task.FromResult(AuthenticateResult.Fail("API key is required"));
        }

        var apiKey = apiKeyHeader.ToString();

        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return Task.FromResult(AuthenticateResult.Fail("API key is required"));
        }

        if (!Options.ValidApiKeys.Contains(apiKey))
        {
            return Task.FromResult(AuthenticateResult.Fail("Invalid API key"));
        }

        var claims = new[]
        {
            new Claim(ClaimTypes.Name, "api-client"),
            new Claim(ClaimTypes.AuthenticationMethod, "api-key"),
            new Claim("api_key_hash", GetKeyHash(apiKey))
        };

        var identity = new ClaimsIdentity(claims, Scheme.Name);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, Scheme.Name);

        return Task.FromResult(AuthenticateResult.Success(ticket));
    }

    protected override Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        Response.StatusCode = 401;
        Response.Headers.WWWAuthenticate = $"{Scheme.Name} realm=\"ZapJobs API\"";
        return Task.CompletedTask;
    }

    protected override Task HandleForbiddenAsync(AuthenticationProperties properties)
    {
        Response.StatusCode = 403;
        return Task.CompletedTask;
    }

    private static string GetKeyHash(string key)
    {
        // Return a truncated hash for logging purposes (not the actual key)
        var hash = key.GetHashCode().ToString("x8");
        return hash[..Math.Min(8, hash.Length)];
    }
}
