namespace ZapJobs.AspNetCore.Api.Dtos;

/// <summary>
/// Generic paginated result wrapper
/// </summary>
/// <typeparam name="T">Type of items in the result</typeparam>
public record PagedResult<T>
{
    /// <summary>The items in this page</summary>
    public required IReadOnlyList<T> Items { get; init; }

    /// <summary>Maximum number of items requested</summary>
    public required int Limit { get; init; }

    /// <summary>Number of items skipped</summary>
    public required int Offset { get; init; }

    /// <summary>Total count of items (if available)</summary>
    public int? TotalCount { get; init; }
}

/// <summary>
/// Standard error response
/// </summary>
public record ErrorResponse
{
    /// <summary>Error message</summary>
    public required string Error { get; init; }

    /// <summary>Additional details about the error</summary>
    public string? Details { get; init; }

    /// <summary>Error code for programmatic handling</summary>
    public string? Code { get; init; }
}

/// <summary>
/// Generic success response
/// </summary>
public record SuccessResponse
{
    /// <summary>Success message</summary>
    public required string Message { get; init; }
}
