namespace ZapJobs.AspNetCore.Api.Dtos;

/// <summary>
/// DTO representing a dead letter entry (failed job that won't be retried)
/// </summary>
public record DeadLetterDto
{
    /// <summary>Dead letter entry ID</summary>
    public required Guid Id { get; init; }

    /// <summary>Original run ID</summary>
    public required Guid RunId { get; init; }

    /// <summary>Job type identifier</summary>
    public required string JobTypeId { get; init; }

    /// <summary>Queue the job was in</summary>
    public required string Queue { get; init; }

    /// <summary>When the job was created</summary>
    public required DateTime CreatedAt { get; init; }

    /// <summary>When the job failed permanently</summary>
    public required DateTime FailedAt { get; init; }

    /// <summary>Number of attempts made</summary>
    public required int AttemptCount { get; init; }

    /// <summary>Last error message</summary>
    public required string ErrorMessage { get; init; }

    /// <summary>Last error type</summary>
    public string? ErrorType { get; init; }

    /// <summary>Input data as JSON</summary>
    public string? InputJson { get; init; }
}

/// <summary>
/// Response after requeuing a dead letter
/// </summary>
public record RequeueResponse
{
    /// <summary>New run ID created</summary>
    public required Guid RunId { get; init; }

    /// <summary>Confirmation message</summary>
    public required string Message { get; init; }
}
