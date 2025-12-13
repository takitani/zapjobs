using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using ZapJobs.AspNetCore.Api.Dtos;
using ZapJobs.Core;

namespace ZapJobs.AspNetCore.Api.Controllers;

/// <summary>
/// API endpoints for managing dead letter entries (permanently failed jobs)
/// </summary>
/// <remarks>
/// Dead letter entries are jobs that have failed and exhausted all retry attempts.
/// They are stored in the regular runs table with status Failed and no NextRetryAt.
/// </remarks>
[ApiController]
[Route("api/v1/deadletter")]
[Produces("application/json")]
[Authorize]
public class DeadLetterController : ControllerBase
{
    private readonly IJobScheduler _scheduler;
    private readonly IJobStorage _storage;

    public DeadLetterController(IJobScheduler scheduler, IJobStorage storage)
    {
        _scheduler = scheduler;
        _storage = storage;
    }

    /// <summary>
    /// List dead letter entries (permanently failed jobs)
    /// </summary>
    /// <param name="limit">Maximum number of results (default: 50)</param>
    /// <param name="offset">Number of results to skip (default: 0)</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>Paginated list of dead letter entries</returns>
    /// <response code="200">List of dead letter entries</response>
    [HttpGet]
    [ProducesResponseType(typeof(PagedResult<DeadLetterDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> List(
        [FromQuery] int limit = 50,
        [FromQuery] int offset = 0,
        CancellationToken ct = default)
    {
        limit = Math.Clamp(limit, 1, 1000);
        offset = Math.Max(0, offset);

        // Get failed runs - these are effectively "dead letter" entries
        var failedRuns = await _storage.GetRunsByStatusAsync(JobRunStatus.Failed, limit, offset, ct);

        // Filter to only include runs that have no pending retry (permanently failed)
        var deadLetterRuns = failedRuns
            .Where(r => r.NextRetryAt == null)
            .ToList();

        var items = deadLetterRuns.Select(r => new DeadLetterDto
        {
            Id = r.Id,
            RunId = r.Id,
            JobTypeId = r.JobTypeId,
            Queue = r.Queue,
            CreatedAt = r.CreatedAt,
            FailedAt = r.CompletedAt ?? r.CreatedAt,
            AttemptCount = r.AttemptNumber,
            ErrorMessage = r.ErrorMessage ?? "Unknown error",
            ErrorType = r.ErrorType,
            InputJson = r.InputJson
        }).ToList();

        return Ok(new PagedResult<DeadLetterDto>
        {
            Items = items,
            Limit = limit,
            Offset = offset
        });
    }

    /// <summary>
    /// Get a specific dead letter entry
    /// </summary>
    /// <param name="id">The dead letter entry ID (same as run ID)</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>The dead letter entry details</returns>
    /// <response code="200">Dead letter entry found</response>
    /// <response code="404">Dead letter entry not found</response>
    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(DeadLetterDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct = default)
    {
        var run = await _storage.GetRunAsync(id, ct);

        if (run == null || run.Status != JobRunStatus.Failed || run.NextRetryAt != null)
        {
            return NotFound(new ErrorResponse { Error = $"Dead letter entry '{id}' not found" });
        }

        return Ok(new DeadLetterDto
        {
            Id = run.Id,
            RunId = run.Id,
            JobTypeId = run.JobTypeId,
            Queue = run.Queue,
            CreatedAt = run.CreatedAt,
            FailedAt = run.CompletedAt ?? run.CreatedAt,
            AttemptCount = run.AttemptNumber,
            ErrorMessage = run.ErrorMessage ?? "Unknown error",
            ErrorType = run.ErrorType,
            InputJson = run.InputJson
        });
    }

    /// <summary>
    /// Requeue a dead letter entry for execution
    /// </summary>
    /// <param name="id">The dead letter entry ID to requeue</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>The new run ID</returns>
    /// <response code="202">Entry requeued successfully</response>
    /// <response code="404">Dead letter entry not found</response>
    [HttpPost("{id:guid}/requeue")]
    [ProducesResponseType(typeof(RequeueResponse), StatusCodes.Status202Accepted)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Requeue(Guid id, CancellationToken ct = default)
    {
        var run = await _storage.GetRunAsync(id, ct);

        if (run == null || run.Status != JobRunStatus.Failed || run.NextRetryAt != null)
        {
            return NotFound(new ErrorResponse { Error = $"Dead letter entry '{id}' not found" });
        }

        // Parse the original input if present
        object? input = null;
        if (!string.IsNullOrEmpty(run.InputJson))
        {
            try
            {
                input = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object>>(run.InputJson);
            }
            catch
            {
                // If we can't parse as dictionary, use the raw string
                input = run.InputJson;
            }
        }

        // Create a new run with the same parameters
        var newRunId = await _scheduler.EnqueueAsync(run.JobTypeId, input, run.Queue, ct);

        return Accepted(new RequeueResponse
        {
            RunId = newRunId,
            Message = $"Job '{run.JobTypeId}' requeued with new run ID '{newRunId}'"
        });
    }

    /// <summary>
    /// Discard a dead letter entry (mark as acknowledged/handled)
    /// </summary>
    /// <param name="id">The dead letter entry ID to discard</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>Success message</returns>
    /// <response code="200">Entry discarded successfully</response>
    /// <response code="404">Dead letter entry not found</response>
    /// <remarks>
    /// Note: This marks the run as cancelled to indicate it was acknowledged.
    /// The run history is preserved for auditing purposes.
    /// </remarks>
    [HttpPost("{id:guid}/discard")]
    [ProducesResponseType(typeof(SuccessResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Discard(Guid id, CancellationToken ct = default)
    {
        var run = await _storage.GetRunAsync(id, ct);

        if (run == null || run.Status != JobRunStatus.Failed || run.NextRetryAt != null)
        {
            return NotFound(new ErrorResponse { Error = $"Dead letter entry '{id}' not found" });
        }

        // Mark as cancelled to indicate it was acknowledged/discarded
        run.Status = JobRunStatus.Cancelled;
        run.ProgressMessage = "Discarded from dead letter queue";

        await _storage.UpdateRunAsync(run, ct);

        return Ok(new SuccessResponse
        {
            Message = $"Dead letter entry '{id}' has been discarded"
        });
    }
}
