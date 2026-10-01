using System.Text.Json;
using Asp.Versioning;
using Microsoft.AspNetCore.Mvc;
using Thor.DataConnectionManager.Exceptions;
using Thor.DataLayer.Repositories;
using Thor.TaskApi.Constants;
using Thor.TaskApi.Models;
using Thor.TaskApi.Services;

namespace Thor.TaskApi.Controllers.V1;

// The Lambda authorizer validates the caller and sets X-THOR-TENANT-ID; requests reaching
// TaskApi are trusted, so the header is read directly rather than re-verified here (same
// pattern as UploadsController). Scope enforcement (a connector may only be granted a
// "scanner"-style role, checked at token issuance) is not yet re-validated on this endpoint —
// the Authorizer only validates Cognito JWTs today, not the connector JWT this endpoint's
// callers present, so there is no trusted scope claim to filter on here yet.
[ApiController]
[ApiVersion("1.0")]
[Route("v{version:apiVersion}/tasks")]
public class TasksController(TaskService taskService) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<TaskListResponse>> Get(CancellationToken cancellationToken)
    {
        if (!Request.Headers.TryGetValue(UploadConstants.TenantHeaderName, out var tenantHeaderValue) ||
            !Guid.TryParse(tenantHeaderValue, out var tenantId))
        {
            return BadRequest($"Missing or invalid '{UploadConstants.TenantHeaderName}' header.");
        }

        var response = await taskService.ClaimTasksAsync(tenantId, cancellationToken);
        return Ok(response);
    }

    // Lets a connector confirm it's still actively working a claimed task, so the stall-reclaim
    // sweep in GET /task leaves it alone. Conditioned on the task still being InProgress (see
    // TaskService.HeartbeatAsync) — if it lost that race against a reclaim, this returns 409 so
    // the connector knows to stop working it rather than silently continuing on a lost claim.
    [HttpPost("{taskId:guid}/heartbeat")]
    public async Task<IActionResult> Heartbeat(Guid taskId, CancellationToken cancellationToken)
    {
        if (!Request.Headers.TryGetValue(UploadConstants.TenantHeaderName, out var tenantHeaderValue) ||
            !Guid.TryParse(tenantHeaderValue, out var tenantId))
        {
            return BadRequest($"Missing or invalid '{UploadConstants.TenantHeaderName}' header.");
        }

        var result = await taskService.HeartbeatAsync(tenantId, taskId, cancellationToken);

        return result switch
        {
            ScanTaskHeartbeatResult.Refreshed => NoContent(),
            ScanTaskHeartbeatResult.NotInProgress => Conflict("Task is no longer InProgress; its claim may have been reclaimed."),
            ScanTaskHeartbeatResult.NotFound => NotFound(),
            _ => throw new InvalidOperationException($"Unexpected heartbeat result: {result}"),
        };
    }

    // Lets a connector report the outcome of a claimed task: Completed/Failed to finalize it, or
    // Pending to voluntarily release it back to the queue for another pass. Conditioned on the
    // task still being InProgress (same race-safety as Heartbeat) so a status update that loses
    // the race against a stall-reclaim doesn't resurrect a claim that's no longer this connector's.
    [HttpPut("{taskId:guid}/status")]
    public async Task<IActionResult> UpdateStatus(Guid taskId, [FromBody] UpdateTaskStatusRequest request, CancellationToken cancellationToken)
    {
        if (!Request.Headers.TryGetValue(UploadConstants.TenantHeaderName, out var tenantHeaderValue) ||
            !Guid.TryParse(tenantHeaderValue, out var tenantId))
        {
            return BadRequest($"Missing or invalid '{UploadConstants.TenantHeaderName}' header.");
        }

        if (!ScanTaskStatus.Validate(request.Status, out var status))
        {
            return BadRequest($"Status must be one of: {string.Join(", ", ScanTaskStatus.SettableValues)}.");
        }

        var result = await taskService.UpdateStatusAsync(tenantId, taskId, status, cancellationToken);

        return result switch
        {
            ScanTaskStatusUpdateResult.Updated => NoContent(),
            ScanTaskStatusUpdateResult.NotInProgress => Conflict("Task is no longer InProgress; its claim may have been reclaimed."),
            ScanTaskStatusUpdateResult.NotFound => NotFound(),
            _ => throw new InvalidOperationException($"Unexpected status update result: {result}"),
        };
    }

    // Lets a connector report progress on a claimed task (elapsed time, percent complete, and
    // per-item-type counts) as an append-only log entry, so progress history for a task is
    // preserved rather than overwritten.
    [HttpPost("{taskId:guid}/progress")]
    public async Task<ActionResult<TaskProgressResponse>> AddProgress(Guid taskId, [FromBody] TaskProgressRequest request, CancellationToken cancellationToken)
    {
        if (!Request.Headers.TryGetValue(UploadConstants.TenantHeaderName, out var tenantHeaderValue) ||
            !Guid.TryParse(tenantHeaderValue, out var tenantId))
        {
            return BadRequest($"Missing or invalid '{UploadConstants.TenantHeaderName}' header.");
        }

        if (request.PercentageComplete is < 0 or > 100)
        {
            return BadRequest("percentageComplete must be between 0 and 100.");
        }

        if (request.TimeElapsed < 0)
        {
            return BadRequest("timeElapsed must not be negative.");
        }

        if (request.Attributes.ValueKind != JsonValueKind.Object)
        {
            return BadRequest("attributes must be a JSON object.");
        }

        var attributesJson = request.Attributes.GetRawText();

        var taskProgressId = await taskService.AddProgressAsync(
            tenantId, taskId, request.TimeElapsed, request.PercentageComplete, attributesJson, cancellationToken);

        return taskProgressId is null ? NotFound() : Ok(new TaskProgressResponse(taskProgressId.Value));
    }

    // Lets a connector fetch the connector-specific config and decrypted authentication field
    // values a claimed task's ScanConfig was created with, so it can authenticate against its
    // target system and read its own settings without any of that living in the task payload
    // itself.
    [HttpGet("{taskId:guid}/settings")]
    public async Task<ActionResult<TaskSettingsResponse>> GetSettings(Guid taskId, CancellationToken cancellationToken)
    {
        if (!Request.Headers.TryGetValue(UploadConstants.TenantHeaderName, out var tenantHeaderValue) ||
            !Guid.TryParse(tenantHeaderValue, out var tenantId))
        {
            return BadRequest($"Missing or invalid '{UploadConstants.TenantHeaderName}' header.");
        }

        try
        {
            var response = await taskService.GetSettingsAsync(tenantId, taskId, cancellationToken);
            return response is null ? NotFound() : Ok(response);
        }
        catch (TenantNotFoundException)
        {
            return StatusCode(StatusCodes.Status403Forbidden);
        }
    }
}
