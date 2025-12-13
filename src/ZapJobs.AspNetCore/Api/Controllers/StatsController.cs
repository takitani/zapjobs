using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using ZapJobs.AspNetCore.Api.Dtos;
using ZapJobs.Core;

namespace ZapJobs.AspNetCore.Api.Controllers;

/// <summary>
/// API endpoints for retrieving statistics
/// </summary>
[ApiController]
[Route("api/v1/stats")]
[Produces("application/json")]
[Authorize]
public class StatsController : ControllerBase
{
    private readonly IJobStorage _storage;

    public StatsController(IJobStorage storage)
    {
        _storage = storage;
    }

    /// <summary>
    /// Get overall statistics
    /// </summary>
    /// <param name="ct">Cancellation token</param>
    /// <returns>Statistics about jobs, runs, and workers</returns>
    /// <response code="200">Statistics retrieved</response>
    [HttpGet]
    [ProducesResponseType(typeof(StatsDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> Get(CancellationToken ct = default)
    {
        var stats = await _storage.GetStatsAsync(ct);
        var definitions = await _storage.GetAllDefinitionsAsync(ct);

        return Ok(new StatsDto
        {
            TotalJobs = stats.TotalJobs,
            EnabledJobs = definitions.Count(d => d.IsEnabled),
            PendingRuns = stats.PendingRuns,
            RunningRuns = stats.RunningRuns,
            CompletedToday = stats.CompletedToday,
            FailedToday = stats.FailedToday,
            ActiveWorkers = stats.ActiveWorkers,
            TotalRuns = stats.TotalRuns,
            TotalLogEntries = stats.TotalLogEntries
        });
    }
}
