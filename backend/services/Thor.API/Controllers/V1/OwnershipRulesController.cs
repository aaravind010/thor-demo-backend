using Asp.Versioning;
using Microsoft.AspNetCore.Mvc;
using Thor.Api.Constants;
using Thor.Api.Exceptions;
using Thor.Api.Models;
using Thor.Api.Services;
using Thor.DataConnectionManager.Exceptions;

namespace Thor.Api.Controllers.V1;

[ApiController]
[ApiVersion("1.0")]
[Route("v{version:apiVersion}/ownership-rules")]
public class OwnershipRulesController(OwnershipRuleService ownershipRuleService) : ControllerBase
{
    // The Lambda authorizer validates the caller and sets X-THOR-TENANT-ID / X-THOR-ACTOR-ID;
    // requests reaching Thor.Api are trusted, so the header is read directly rather than
    // re-verified here.
    [HttpPost]
    public async Task<ActionResult<OwnershipRuleResponse>> Create(
        [FromBody] CreateOwnershipRuleRequest request, CancellationToken cancellationToken)
    {
        if (!Request.Headers.TryGetValue(TenantConstants.TenantHeaderName, out var tenantHeaderValue) ||
            !Guid.TryParse(tenantHeaderValue, out var tenantId))
        {
            return BadRequest($"Missing or invalid '{TenantConstants.TenantHeaderName}' header.");
        }

        if (string.IsNullOrWhiteSpace(request.RuleName))
        {
            return BadRequest("RuleName is required.");
        }

        if (string.IsNullOrWhiteSpace(request.RuleType))
        {
            return BadRequest("RuleType is required.");
        }

        if (string.IsNullOrWhiteSpace(request.AppliesTo))
        {
            return BadRequest("AppliesTo is required.");
        }

        if (string.IsNullOrWhiteSpace(request.RuleDefinition))
        {
            return BadRequest("RuleDefinition is required.");
        }

        // A zero weight can never win a vote, so it's rejected as a mistake.
        if (request.PrecisionScore is <= 0 or > 1)
        {
            return BadRequest("PrecisionScore must be greater than 0 and at most 1.");
        }

        try
        {
            var response = await ownershipRuleService.CreateAsync(tenantId, request, cancellationToken);
            return StatusCode(StatusCodes.Status201Created, response);
        }
        catch (InvalidRuleDefinitionException ex)
        {
            return BadRequest(ex.Message);
        }
        catch (OwnershipRuleNameConflictException ex)
        {
            return Conflict(ex.Message);
        }
        catch (TenantNotFoundException)
        {
            return StatusCode(StatusCodes.Status403Forbidden);
        }
    }

    [HttpGet]
    public async Task<ActionResult<CursorPage<OwnershipRuleResponse>>> List(
        [FromQuery] string? ruleType,
        [FromQuery] string? appliesTo,
        [FromQuery] bool? isActive,
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
            return Ok(await ownershipRuleService.ListAsync(
                tenantId, ruleType, appliesTo, isActive, after, limit, cancellationToken));
        }
        catch (TenantNotFoundException)
        {
            return StatusCode(StatusCodes.Status403Forbidden);
        }
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<OwnershipRuleResponse>> Get(Guid id, CancellationToken cancellationToken)
    {
        if (!Request.Headers.TryGetValue(TenantConstants.TenantHeaderName, out var tenantHeaderValue) ||
            !Guid.TryParse(tenantHeaderValue, out var tenantId))
        {
            return BadRequest($"Missing or invalid '{TenantConstants.TenantHeaderName}' header.");
        }

        try
        {
            var response = await ownershipRuleService.GetAsync(tenantId, id, cancellationToken);
            return response is null ? NotFound() : Ok(response);
        }
        catch (TenantNotFoundException)
        {
            return StatusCode(StatusCodes.Status403Forbidden);
        }
    }
}
