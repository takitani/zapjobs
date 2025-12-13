using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using ZapJobs.AspNetCore.Api.Dtos;
using ZapJobs.Core;

namespace ZapJobs.AspNetCore.Api.Controllers;

/// <summary>
/// API endpoints for managing job definitions and triggering jobs
/// </summary>
[ApiController]
[Route("api/v1/jobs")]
[Produces("application/json")]
[Authorize]
public class JobsController : ControllerBase
{
    private readonly IJobScheduler _scheduler;
    private readonly IJobStorage _storage;

    public JobsController(IJobScheduler scheduler, IJobStorage storage)
    {
        _scheduler = scheduler;
        _storage = storage;
    }

    /// <summary>
    /// Trigger a job for immediate execution
    /// </summary>
    /// <param name="jobTypeId">The job type identifier</param>
    /// <param name="request">Optional input parameters and queue</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>The created run information</returns>
    /// <response code="202">Job triggered successfully</response>
    /// <response code="404">Job type not found</response>
    [HttpPost("{jobTypeId}/trigger")]
    [ProducesResponseType(typeof(TriggerResponse), StatusCodes.Status202Accepted)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Trigger(
        string jobTypeId,
        [FromBody] TriggerRequest? request = null,
        CancellationToken ct = default)
    {
        var definition = await _storage.GetJobDefinitionAsync(jobTypeId, ct);
        if (definition == null)
        {
            return NotFound(new ErrorResponse { Error = $"Job type '{jobTypeId}' not found" });
        }

        object? input = request?.Input.HasValue == true
            ? ConvertJsonElement(request.Input.Value)
            : null;

        var runId = await _scheduler.TriggerAsync(jobTypeId, input, ct);

        return Accepted(new TriggerResponse
        {
            RunId = runId,
            JobTypeId = jobTypeId,
            Status = "Pending"
        });
    }

    /// <summary>
    /// Schedule a job for later execution
    /// </summary>
    /// <param name="jobTypeId">The job type identifier</param>
    /// <param name="request">Schedule parameters including when to run</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>The scheduled run information</returns>
    /// <response code="202">Job scheduled successfully</response>
    /// <response code="400">Invalid request (missing runAt or delay)</response>
    /// <response code="404">Job type not found</response>
    [HttpPost("{jobTypeId}/schedule")]
    [ProducesResponseType(typeof(ScheduleResponse), StatusCodes.Status202Accepted)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Schedule(
        string jobTypeId,
        [FromBody] ScheduleRequest request,
        CancellationToken ct = default)
    {
        var definition = await _storage.GetJobDefinitionAsync(jobTypeId, ct);
        if (definition == null)
        {
            return NotFound(new ErrorResponse { Error = $"Job type '{jobTypeId}' not found" });
        }

        if (!request.RunAt.HasValue && !request.Delay.HasValue)
        {
            return BadRequest(new ErrorResponse { Error = "Either 'runAt' or 'delay' must be specified" });
        }

        object? input = request.Input.HasValue ? ConvertJsonElement(request.Input.Value) : null;
        Guid runId;
        DateTimeOffset scheduledFor;

        if (request.RunAt.HasValue)
        {
            runId = await _scheduler.ScheduleAsync(jobTypeId, request.RunAt.Value, input, request.Queue, ct);
            scheduledFor = request.RunAt.Value;
        }
        else
        {
            runId = await _scheduler.ScheduleAsync(jobTypeId, request.Delay!.Value, input, request.Queue, ct);
            scheduledFor = DateTimeOffset.UtcNow.Add(request.Delay!.Value);
        }

        return Accepted(new ScheduleResponse
        {
            RunId = runId,
            JobTypeId = jobTypeId,
            ScheduledFor = scheduledFor
        });
    }

    /// <summary>
    /// List all job definitions
    /// </summary>
    /// <param name="ct">Cancellation token</param>
    /// <returns>List of all job definitions</returns>
    /// <response code="200">List of job definitions</response>
    [HttpGet]
    [ProducesResponseType(typeof(IEnumerable<JobDefinitionDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> List(CancellationToken ct = default)
    {
        var definitions = await _storage.GetAllDefinitionsAsync(ct);
        return Ok(definitions.Select(JobDefinitionDto.FromEntity));
    }

    /// <summary>
    /// Get a specific job definition
    /// </summary>
    /// <param name="jobTypeId">The job type identifier</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>The job definition</returns>
    /// <response code="200">Job definition found</response>
    /// <response code="404">Job type not found</response>
    [HttpGet("{jobTypeId}")]
    [ProducesResponseType(typeof(JobDefinitionDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get(string jobTypeId, CancellationToken ct = default)
    {
        var definition = await _storage.GetJobDefinitionAsync(jobTypeId, ct);
        if (definition == null)
        {
            return NotFound(new ErrorResponse { Error = $"Job type '{jobTypeId}' not found" });
        }

        return Ok(JobDefinitionDto.FromEntity(definition));
    }

    /// <summary>
    /// Update a job definition
    /// </summary>
    /// <param name="jobTypeId">The job type identifier</param>
    /// <param name="request">The fields to update</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>The updated job definition</returns>
    /// <response code="200">Job definition updated</response>
    /// <response code="404">Job type not found</response>
    [HttpPut("{jobTypeId}")]
    [ProducesResponseType(typeof(JobDefinitionDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Update(
        string jobTypeId,
        [FromBody] UpdateJobDefinitionRequest request,
        CancellationToken ct = default)
    {
        var definition = await _storage.GetJobDefinitionAsync(jobTypeId, ct);
        if (definition == null)
        {
            return NotFound(new ErrorResponse { Error = $"Job type '{jobTypeId}' not found" });
        }

        // Apply updates
        if (request.DisplayName != null)
            definition.DisplayName = request.DisplayName;
        if (request.Description != null)
            definition.Description = request.Description;
        if (request.Queue != null)
            definition.Queue = request.Queue;
        if (request.IsEnabled.HasValue)
            definition.IsEnabled = request.IsEnabled.Value;
        if (request.MaxRetries.HasValue)
            definition.MaxRetries = request.MaxRetries.Value;
        if (request.TimeoutSeconds.HasValue)
            definition.TimeoutSeconds = request.TimeoutSeconds.Value;
        if (request.MaxConcurrency.HasValue)
            definition.MaxConcurrency = request.MaxConcurrency.Value;
        if (request.CronExpression != null)
        {
            definition.CronExpression = request.CronExpression;
            definition.ScheduleType = string.IsNullOrEmpty(request.CronExpression)
                ? ScheduleType.Manual
                : ScheduleType.Cron;
        }
        if (request.TimeZoneId != null)
            definition.TimeZoneId = request.TimeZoneId;

        definition.UpdatedAt = DateTime.UtcNow;

        await _storage.UpsertDefinitionAsync(definition, ct);

        return Ok(JobDefinitionDto.FromEntity(definition));
    }

    /// <summary>
    /// Delete a job definition
    /// </summary>
    /// <param name="jobTypeId">The job type identifier</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>No content on success</returns>
    /// <response code="204">Job definition deleted</response>
    /// <response code="404">Job type not found</response>
    [HttpDelete("{jobTypeId}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(string jobTypeId, CancellationToken ct = default)
    {
        var definition = await _storage.GetJobDefinitionAsync(jobTypeId, ct);
        if (definition == null)
        {
            return NotFound(new ErrorResponse { Error = $"Job type '{jobTypeId}' not found" });
        }

        await _storage.DeleteDefinitionAsync(jobTypeId, ct);

        return NoContent();
    }

    private static object? ConvertJsonElement(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.Object => JsonSerializer.Deserialize<Dictionary<string, object>>(element.GetRawText()),
            JsonValueKind.Array => JsonSerializer.Deserialize<List<object>>(element.GetRawText()),
            JsonValueKind.String => element.GetString(),
            JsonValueKind.Number => element.TryGetInt64(out var l) ? l : element.GetDouble(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Null => null,
            _ => element.GetRawText()
        };
    }
}
