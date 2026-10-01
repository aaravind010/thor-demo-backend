using Asp.Versioning;
using Microsoft.AspNetCore.Mvc;
using Thor.Api.Constants;
using Thor.Api.Models;
using Thor.Api.Services;
using Thor.DataConnectionManager.Exceptions;

namespace Thor.Api.Controllers.V1;

[ApiController]
[ApiVersion("1.0")]
[Route("v{version:apiVersion}/groups")]
public class GroupsController(GroupService groupService) : ControllerBase
{
    // The Lambda authorizer validates the caller and sets X-THOR-TENANT-ID / X-THOR-ACTOR-ID;
    // requests reaching Thor.Api are trusted, so the header is read directly rather than
    // re-verified here.
    [HttpGet]
    public async Task<ActionResult<CursorPage<GroupResponse>>> List(
        [FromQuery] Guid? sourceId,
        [FromQuery] Guid? after,
        [FromQuery] int limit = PagingConstants.DefaultLimit,
        CancellationToken cancellationToken = default)
    {
        if (!Request.Headers.TryGetValue(TenantConstants.TenantHeaderName, out var tenantHeaderValue) ||
            !Guid.TryParse(tenantHeaderValue, out var tenantId))
        {
            return BadRequest($"Missing or invalid '{TenantConstants.TenantHeaderName}' header.");
        }

        if (limit is < 1 or > PagingConstants.MaxLimit)
        {
            return BadRequest(PagingConstants.InvalidLimitMessage);
        }

        try
        {
            return Ok(await groupService.ListAsync(tenantId, sourceId, after, limit, cancellationToken));
        }
        catch (TenantNotFoundException)
        {
            return StatusCode(StatusCodes.Status403Forbidden);
        }
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<GroupResponse>> Get(Guid id, CancellationToken cancellationToken)
    {
        if (!Request.Headers.TryGetValue(TenantConstants.TenantHeaderName, out var tenantHeaderValue) ||
            !Guid.TryParse(tenantHeaderValue, out var tenantId))
        {
            return BadRequest($"Missing or invalid '{TenantConstants.TenantHeaderName}' header.");
        }

        try
        {
            var response = await groupService.GetAsync(tenantId, id, cancellationToken);
            return response is null ? NotFound() : Ok(response);
        }
        catch (TenantNotFoundException)
        {
            return StatusCode(StatusCodes.Status403Forbidden);
        }
    }
}
