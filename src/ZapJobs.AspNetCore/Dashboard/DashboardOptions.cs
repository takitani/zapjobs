namespace ZapJobs.AspNetCore.Dashboard;

/// <summary>
/// Configuration options for the ZapJobs Dashboard
/// </summary>
public class DashboardOptions
{
    /// <summary>
    /// The base path where the dashboard will be available.
    /// Default: "/zapjobs"
    /// </summary>
    public string BasePath { get; set; } = "/zapjobs";

    /// <summary>
    /// Default theme for new users.
    /// Valid values: "light", "dark", or "auto"
    /// Default: "auto" (follows system preference)
    /// </summary>
    public string DefaultTheme { get; set; } = "auto";

    /// <summary>
    /// Allow users to change the theme.
    /// When false, the theme switcher is hidden and only the default theme is used.
    /// Default: true
    /// </summary>
    public bool AllowThemeChange { get; set; } = true;

    /// <summary>
    /// The title shown in the browser tab.
    /// Default: "ZapJobs Dashboard"
    /// </summary>
    public string Title { get; set; } = "ZapJobs Dashboard";

    /// <summary>
    /// Enable real-time updates via polling.
    /// Default: true
    /// </summary>
    public bool EnableRealTimeUpdates { get; set; } = true;

    /// <summary>
    /// Polling interval for real-time updates in seconds.
    /// Default: 5 seconds
    /// </summary>
    public int PollingIntervalSeconds { get; set; } = 5;

    /// <summary>
    /// Number of items to show per page in tables.
    /// Default: 25
    /// </summary>
    public int PageSize { get; set; } = 25;

    /// <summary>
    /// Authorization callback to determine if a user can access the dashboard.
    /// When null, the dashboard is accessible to all authenticated users.
    /// </summary>
    public Func<Microsoft.AspNetCore.Http.HttpContext, Task<bool>>? Authorization { get; set; }

    /// <summary>
    /// Validates the options and normalizes values.
    /// </summary>
    internal void Validate()
    {
        if (string.IsNullOrWhiteSpace(BasePath))
            BasePath = "/zapjobs";

        // Ensure base path starts with / but doesn't end with /
        if (!BasePath.StartsWith('/'))
            BasePath = "/" + BasePath;

        BasePath = BasePath.TrimEnd('/');

        // Validate theme
        if (DefaultTheme != "light" && DefaultTheme != "dark" && DefaultTheme != "auto")
            DefaultTheme = "auto";

        // Ensure reasonable polling interval
        if (PollingIntervalSeconds < 1)
            PollingIntervalSeconds = 1;
        else if (PollingIntervalSeconds > 60)
            PollingIntervalSeconds = 60;

        // Ensure reasonable page size
        if (PageSize < 10)
            PageSize = 10;
        else if (PageSize > 100)
            PageSize = 100;
    }
}
