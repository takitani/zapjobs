using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using ZapJobs.AspNetCore.Api.Dtos;
using ZapJobs.Core;

namespace ZapJobs.AspNetCore.Api.Controllers;

/// <summary>
/// API endpoints for monitoring workers
/// </summary>
[ApiController]
[Route("api/v1/workers")]
[Produces("application/json")]
[Authorize]
public class WorkersController : ControllerBase
{
    private readonly IJobStorage _storage;
    private readonly ZapJobsOptions _options;

    public WorkersController(IJobStorage storage, IOptions<ZapJobsOptions> options)
    {
        _storage = storage;
        _options = options.Value;
    }

    /// <summary>
    /// List all active workers
    /// </summary>
    /// <param name="ct">Cancellation token</param>
    /// <returns>List of workers with their current status</returns>
    /// <response code="200">List of workers</response>
    [HttpGet]
    [ProducesResponseType(typeof(IEnumerable<WorkerDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> List(CancellationToken ct = default)
    {
        var heartbeats = await _storage.GetHeartbeatsAsync(ct);

        var workers = heartbeats
            .Select(h => WorkerDto.FromEntity(h, _options.StaleWorkerThreshold))
            .OrderByDescending(w => w.IsHealthy)
            .ThenByDescending(w => w.LastHeartbeat)
            .ToList();

        return Ok(workers);
    }

    /// <summary>
    /// Get a specific worker by ID
    /// </summary>
    /// <param name="workerId">The worker ID</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>Worker details</returns>
    /// <response code="200">Worker found</response>
    /// <response code="404">Worker not found</response>
    [HttpGet("{workerId}")]
    [ProducesResponseType(typeof(WorkerDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get(string workerId, CancellationToken ct = default)
    {
        var heartbeats = await _storage.GetHeartbeatsAsync(ct);
        var heartbeat = heartbeats.FirstOrDefault(h =>
            h.WorkerId.Equals(workerId, StringComparison.OrdinalIgnoreCase));

        if (heartbeat == null)
        {
            return NotFound(new ErrorResponse { Error = $"Worker '{workerId}' not found" });
        }

        return Ok(WorkerDto.FromEntity(heartbeat, _options.StaleWorkerThreshold));
    }
}
