using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using ZapJobs.AspNetCore.Api.Dtos;
using ZapJobs.Core;

namespace ZapJobs.AspNetCore.Api.Controllers;

/// <summary>
/// API endpoints for managing job runs
/// </summary>
[ApiController]
[Route("api/v1/runs")]
[Produces("application/json")]
[Authorize]
public class RunsController : ControllerBase
{
    private readonly IJobScheduler _scheduler;
    private readonly IJobStorage _storage;

    public RunsController(IJobScheduler scheduler, IJobStorage storage)
    {
        _scheduler = scheduler;
        _storage = storage;
    }

    /// <summary>
    /// List job runs with optional filters
    /// </summary>
    /// <param name="jobTypeId">Filter by job type</param>
    /// <param name="status">Filter by status</param>
    /// <param name="limit">Maximum number of results (default: 50)</param>
    /// <param name="offset">Number of results to skip (default: 0)</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>Paginated list of job runs</returns>
    /// <response code="200">List of job runs</response>
    [HttpGet]
    [ProducesResponseType(typeof(PagedResult<JobRunDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> List(
        [FromQuery] string? jobTypeId = null,
        [FromQuery] JobRunStatus? status = null,
        [FromQuery] int limit = 50,
        [FromQuery] int offset = 0,
        CancellationToken ct = default)
    {
        // Validate and clamp parameters
        limit = Math.Clamp(limit, 1, 1000);
        offset = Math.Max(0, offset);

        IReadOnlyList<JobRun> runs;

        if (!string.IsNullOrEmpty(jobTypeId))
        {
            runs = await _storage.GetRunsByJobTypeAsync(jobTypeId, limit, offset, ct);
        }
        else if (status.HasValue)
        {
            runs = await _storage.GetRunsByStatusAsync(status.Value, limit, offset, ct);
        }
        else
        {
            // Default: get all pending runs
            runs = await _storage.GetRunsByStatusAsync(JobRunStatus.Pending, limit, offset, ct);
        }

        return Ok(new PagedResult<JobRunDto>
        {
            Items = runs.Select(JobRunDto.FromEntity).ToList(),
            Limit = limit,
            Offset = offset
        });
    }

    /// <summary>
    /// Get detailed information about a specific run
    /// </summary>
    /// <param name="runId">The run ID</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>Detailed run information including logs</returns>
    /// <response code="200">Run details found</response>
    /// <response code="404">Run not found</response>
    [HttpGet("{runId:guid}")]
    [ProducesResponseType(typeof(JobRunDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get(Guid runId, CancellationToken ct = default)
    {
        var run = await _storage.GetRunAsync(runId, ct);
        if (run == null)
        {
            return NotFound(new ErrorResponse { Error = $"Run '{runId}' not found" });
        }

        var logs = await _storage.GetLogsAsync(runId, limit: 100, ct);

        return Ok(JobRunDetailDto.FromEntity(run, logs));
    }

    /// <summary>
    /// Cancel a pending or running job
    /// </summary>
    /// <param name="runId">The run ID to cancel</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>No content on success</returns>
    /// <response code="204">Run cancelled successfully</response>
    /// <response code="404">Run not found</response>
    /// <response code="409">Run cannot be cancelled (already completed or failed)</response>
    [HttpPost("{runId:guid}/cancel")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Cancel(Guid runId, CancellationToken ct = default)
    {
        var cancelled = await _scheduler.CancelAsync(runId, ct);

        if (!cancelled)
        {
            var run = await _storage.GetRunAsync(runId, ct);
            if (run == null)
            {
                return NotFound(new ErrorResponse { Error = $"Run '{runId}' not found" });
            }

            return Conflict(new ErrorResponse { Error = $"Cannot cancel run in status '{run.Status}'" });
        }

        return NoContent();
    }

    /// <summary>
    /// Get logs for a specific run
    /// </summary>
    /// <param name="runId">The run ID</param>
    /// <param name="limit">Maximum number of log entries (default: 500)</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>List of log entries</returns>
    /// <response code="200">Log entries found</response>
    /// <response code="404">Run not found</response>
    [HttpGet("{runId:guid}/logs")]
    [ProducesResponseType(typeof(IEnumerable<JobLogDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetLogs(
        Guid runId,
        [FromQuery] int limit = 500,
        CancellationToken ct = default)
    {
        var run = await _storage.GetRunAsync(runId, ct);
        if (run == null)
        {
            return NotFound(new ErrorResponse { Error = $"Run '{runId}' not found" });
        }

        limit = Math.Clamp(limit, 1, 10000);
        var logs = await _storage.GetLogsAsync(runId, limit, ct);

        return Ok(logs.Select(JobLogDto.FromEntity));
    }
}
