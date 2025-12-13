using System.Reflection;
using System.Text;
using Microsoft.AspNetCore.Http;

namespace ZapJobs.AspNetCore.Dashboard;

/// <summary>
/// Middleware that serves the ZapJobs Dashboard
/// </summary>
public class DashboardMiddleware
{
    private readonly RequestDelegate _next;
    private readonly DashboardOptions _options;
    private readonly Assembly _assembly;
    private readonly string _resourceNamespace;

    private static readonly Dictionary<string, string> ContentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        { ".html", "text/html; charset=utf-8" },
        { ".css", "text/css; charset=utf-8" },
        { ".js", "application/javascript; charset=utf-8" },
        { ".json", "application/json; charset=utf-8" },
        { ".svg", "image/svg+xml" },
        { ".png", "image/png" },
        { ".ico", "image/x-icon" }
    };

    public DashboardMiddleware(RequestDelegate next, DashboardOptions options)
    {
        _next = next;
        _options = options;
        _assembly = typeof(DashboardMiddleware).Assembly;
        _resourceNamespace = "ZapJobs.AspNetCore.Dashboard.wwwroot";
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var path = context.Request.Path.Value ?? "";

        // Check if this request is for the dashboard
        if (!path.StartsWith(_options.BasePath, StringComparison.OrdinalIgnoreCase))
        {
            await _next(context);
            return;
        }

        // Check authorization
        if (_options.Authorization != null)
        {
            var authorized = await _options.Authorization(context);
            if (!authorized)
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                return;
            }
        }

        // Get the relative path within the dashboard
        var relativePath = path[_options.BasePath.Length..];
        if (string.IsNullOrEmpty(relativePath) || relativePath == "/")
        {
            relativePath = "/index.html";
        }

        // Try to serve static file
        if (await TryServeStaticFile(context, relativePath))
        {
            return;
        }

        // For SPA-style routing, serve the layout template for HTML requests
        if (IsHtmlRequest(context))
        {
            await ServeLayout(context, relativePath);
            return;
        }

        context.Response.StatusCode = StatusCodes.Status404NotFound;
    }

    private async Task<bool> TryServeStaticFile(HttpContext context, string relativePath)
    {
        // Remove leading slash and convert to resource path
        var resourcePath = relativePath.TrimStart('/').Replace('/', '.');
        var fullResourceName = $"{_resourceNamespace}.{resourcePath}";

        using var stream = _assembly.GetManifestResourceStream(fullResourceName);
        if (stream == null)
        {
            return false;
        }

        var extension = Path.GetExtension(relativePath);
        if (ContentTypes.TryGetValue(extension, out var contentType))
        {
            context.Response.ContentType = contentType;
        }

        // Enable caching for static assets
        if (extension is ".css" or ".js" or ".svg" or ".png" or ".ico")
        {
            context.Response.Headers.CacheControl = "public, max-age=31536000, immutable";
        }

        await stream.CopyToAsync(context.Response.Body);
        return true;
    }

    private async Task ServeLayout(HttpContext context, string relativePath)
    {
        var layoutResourceName = "ZapJobs.AspNetCore.Dashboard.Templates.layout.html";

        using var stream = _assembly.GetManifestResourceStream(layoutResourceName);
        if (stream == null)
        {
            context.Response.StatusCode = StatusCodes.Status500InternalServerError;
            await context.Response.WriteAsync("Dashboard layout template not found.");
            return;
        }

        using var reader = new StreamReader(stream, Encoding.UTF8);
        var template = await reader.ReadToEndAsync();

        // Determine current page
        var pageInfo = GetPageInfo(relativePath);

        // Replace placeholders
        var html = template
            .Replace("{{basePath}}", _options.BasePath)
            .Replace("{{pageTitle}}", pageInfo.Title)
            .Replace("{{content}}", GetPageContent(relativePath))
            .Replace("{{#if isHome}}", pageInfo.IsHome ? "" : "<!--")
            .Replace("{{/if}}", pageInfo.IsHome ? "" : "-->")
            .Replace("{{#if isJobs}}", pageInfo.Page == "jobs" ? "" : "<!--")
            .Replace("{{#if isRuns}}", pageInfo.Page == "runs" ? "" : "<!--")
            .Replace("{{#if isDeadLetter}}", pageInfo.Page == "dead-letter" ? "" : "<!--")
            .Replace("{{#if isWorkers}}", pageInfo.Page == "workers" ? "" : "<!--");

        // Hide theme switcher if not allowed
        if (!_options.AllowThemeChange)
        {
            html = html.Replace("<div class=\"theme-switcher\">", "<div class=\"theme-switcher hidden\">");
        }

        context.Response.ContentType = "text/html; charset=utf-8";
        await context.Response.WriteAsync(html);
    }

    private static bool IsHtmlRequest(HttpContext context)
    {
        var accept = context.Request.Headers.Accept.ToString();
        return string.IsNullOrEmpty(accept) ||
               accept.Contains("text/html") ||
               accept.Contains("*/*");
    }

    private static (string Page, string Title, bool IsHome) GetPageInfo(string path)
    {
        var normalizedPath = path.TrimStart('/').ToLowerInvariant();

        return normalizedPath switch
        {
            "" or "index.html" => ("home", "Dashboard", true),
            "jobs" or "jobs/" => ("jobs", "Job Definitions", false),
            "runs" or "runs/" => ("runs", "Job Runs", false),
            "dead-letter" or "dead-letter/" => ("dead-letter", "Dead Letter Queue", false),
            "workers" or "workers/" => ("workers", "Workers", false),
            _ => ("home", "Dashboard", normalizedPath is "" or "index.html")
        };
    }

    private string GetPageContent(string path)
    {
        var normalizedPath = path.TrimStart('/').ToLowerInvariant();

        // Return placeholder content - in a real implementation, this would
        // render actual page content or load Vue/React components
        return normalizedPath switch
        {
            "" or "index.html" => GetDashboardContent(),
            "jobs" or "jobs/" => GetJobsContent(),
            "runs" or "runs/" => GetRunsContent(),
            "dead-letter" or "dead-letter/" => GetDeadLetterContent(),
            "workers" or "workers/" => GetWorkersContent(),
            _ => GetDashboardContent()
        };
    }

    private string GetDashboardContent()
    {
        return """
            <div class="stats-grid">
                <div class="stat-card">
                    <div class="stat-label">Total Jobs</div>
                    <div class="stat-value" id="stat-total-jobs">-</div>
                </div>
                <div class="stat-card">
                    <div class="stat-label">Running</div>
                    <div class="stat-value" id="stat-running">-</div>
                </div>
                <div class="stat-card">
                    <div class="stat-label">Pending</div>
                    <div class="stat-value" id="stat-pending">-</div>
                </div>
                <div class="stat-card">
                    <div class="stat-label">Failed (24h)</div>
                    <div class="stat-value" id="stat-failed">-</div>
                </div>
            </div>
            <div class="card">
                <div class="card-header">
                    <h2 class="card-title">Recent Activity</h2>
                </div>
                <div class="card-body">
                    <p class="text-muted">Job activity will appear here.</p>
                </div>
            </div>
            """;
    }

    private string GetJobsContent()
    {
        return """
            <div class="card">
                <div class="card-header">
                    <h2 class="card-title">Job Definitions</h2>
                </div>
                <div class="card-body">
                    <div class="table-responsive">
                        <table class="table">
                            <thead>
                                <tr>
                                    <th>Job Type ID</th>
                                    <th>Queue</th>
                                    <th>Schedule</th>
                                    <th>Status</th>
                                    <th>Actions</th>
                                </tr>
                            </thead>
                            <tbody id="jobs-table-body">
                                <tr>
                                    <td colspan="5" class="text-muted text-center">Loading...</td>
                                </tr>
                            </tbody>
                        </table>
                    </div>
                </div>
            </div>
            """;
    }

    private string GetRunsContent()
    {
        return """
            <div class="card">
                <div class="card-header d-flex justify-content-between align-items-center">
                    <h2 class="card-title">Job Runs</h2>
                    <div class="d-flex gap-2">
                        <select class="form-input" id="status-filter" style="width: auto;">
                            <option value="">All Statuses</option>
                            <option value="Pending">Pending</option>
                            <option value="Running">Running</option>
                            <option value="Completed">Completed</option>
                            <option value="Failed">Failed</option>
                        </select>
                    </div>
                </div>
                <div class="card-body">
                    <div class="table-responsive">
                        <table class="table">
                            <thead>
                                <tr>
                                    <th>Run ID</th>
                                    <th>Job Type</th>
                                    <th>Status</th>
                                    <th>Started</th>
                                    <th>Duration</th>
                                    <th>Actions</th>
                                </tr>
                            </thead>
                            <tbody id="runs-table-body">
                                <tr>
                                    <td colspan="6" class="text-muted text-center">Loading...</td>
                                </tr>
                            </tbody>
                        </table>
                    </div>
                    <div class="pagination">
                        <button class="pagination-btn" id="prev-page" disabled>Previous</button>
                        <span class="pagination-info" id="page-info">Page 1</span>
                        <button class="pagination-btn" id="next-page">Next</button>
                    </div>
                </div>
            </div>
            """;
    }

    private string GetDeadLetterContent()
    {
        return """
            <div class="card">
                <div class="card-header d-flex justify-content-between align-items-center">
                    <h2 class="card-title">Dead Letter Queue</h2>
                    <button class="btn btn-secondary btn-sm" id="requeue-all">Requeue All</button>
                </div>
                <div class="card-body">
                    <div class="table-responsive">
                        <table class="table">
                            <thead>
                                <tr>
                                    <th>ID</th>
                                    <th>Job Type</th>
                                    <th>Failed At</th>
                                    <th>Reason</th>
                                    <th>Status</th>
                                    <th>Actions</th>
                                </tr>
                            </thead>
                            <tbody id="dlq-table-body">
                                <tr>
                                    <td colspan="6" class="text-muted text-center">Loading...</td>
                                </tr>
                            </tbody>
                        </table>
                    </div>
                </div>
            </div>
            """;
    }

    private string GetWorkersContent()
    {
        return """
            <div class="card">
                <div class="card-header">
                    <h2 class="card-title">Active Workers</h2>
                </div>
                <div class="card-body">
                    <div class="table-responsive">
                        <table class="table">
                            <thead>
                                <tr>
                                    <th>Worker ID</th>
                                    <th>Hostname</th>
                                    <th>Queues</th>
                                    <th>Started</th>
                                    <th>Last Heartbeat</th>
                                    <th>Status</th>
                                </tr>
                            </thead>
                            <tbody id="workers-table-body">
                                <tr>
                                    <td colspan="6" class="text-muted text-center">Loading...</td>
                                </tr>
                            </tbody>
                        </table>
                    </div>
                </div>
            </div>
            """;
    }
}
