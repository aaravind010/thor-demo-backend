using Asp.Versioning;
using Microsoft.AspNetCore.Mvc;
using Thor.Api.Constants;
using Thor.Api.Models;
using Thor.Api.Services;
using Thor.DataConnectionManager.Exceptions;

namespace Thor.Api.Controllers.V1;

/// <summary>ATRE's account type votes (<c>atre_vote</c>).</summary>
[ApiController]
[ApiVersion("1.0")]
[Route("v{version:apiVersion}/account-votes")]
public class AccountVotesController(AccountVoteService accountVoteService) : ControllerBase
{
    // The Lambda authorizer validates the caller and sets X-THOR-TENANT-ID / X-THOR-ACTOR-ID;
    // requests reaching Thor.Api are trusted, so the header is read directly rather than
    // re-verified here.
    [HttpGet]
    public async Task<ActionResult<CursorPage<AccountVoteResponse>>> List(
        [FromQuery] Guid? entityId,
        [FromQuery] Guid? ruleId,
        [FromQuery] string? runId,
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
            return Ok(await accountVoteService.ListAsync(tenantId, entityId, ruleId, runId, after, limit, cancellationToken));
        }
        catch (TenantNotFoundException)
        {
            return StatusCode(StatusCodes.Status403Forbidden);
        }
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<AccountVoteResponse>> Get(Guid id, CancellationToken cancellationToken)
    {
        if (!Request.Headers.TryGetValue(TenantConstants.TenantHeaderName, out var tenantHeaderValue) ||
            !Guid.TryParse(tenantHeaderValue, out var tenantId))
        {
            return BadRequest($"Missing or invalid '{TenantConstants.TenantHeaderName}' header.");
        }

        try
        {
            var response = await accountVoteService.GetAsync(tenantId, id, cancellationToken);
            return response is null ? NotFound() : Ok(response);
        }
        catch (TenantNotFoundException)
        {
            return StatusCode(StatusCodes.Status403Forbidden);
        }
    }
}
