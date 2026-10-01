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
[Route("v{version:apiVersion}/account-type-rules")]
public class AccountTypeRulesController(AccountTypeRuleService accountTypeRuleService) : ControllerBase
{
    // The Lambda authorizer validates the caller and sets X-THOR-TENANT-ID / X-THOR-ACTOR-ID;
    // requests reaching Thor.Api are trusted, so the header is read directly rather than
    // re-verified here.
    [HttpPost]
    public async Task<ActionResult<AccountTypeRuleResponse>> Create(
        [FromBody] CreateAccountTypeRuleRequest request, CancellationToken cancellationToken)
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

        if (string.IsNullOrWhiteSpace(request.RuleDefinition))
        {
            return BadRequest("RuleDefinition is required.");
        }

        if (string.IsNullOrWhiteSpace(request.AppliesTo))
        {
            return BadRequest("AppliesTo is required.");
        }

        // A zero weight still fires and writes votes but can never win, so it's rejected as a mistake.
        if (request.PrecisionScore is <= 0 or > 1)
        {
            return BadRequest("PrecisionScore must be greater than 0 and at most 1.");
        }

        try
        {
            var response = await accountTypeRuleService.CreateAsync(tenantId, request, cancellationToken);
            return StatusCode(StatusCodes.Status201Created, response);
        }
        catch (InvalidRuleDefinitionException ex)
        {
            return BadRequest(ex.Message);
        }
        catch (AccountTypeNotFoundException ex)
        {
            return BadRequest(ex.Message);
        }
        catch (TenantNotFoundException)
        {
            return StatusCode(StatusCodes.Status403Forbidden);
        }
    }

    [HttpGet]
    public async Task<ActionResult<CursorPage<AccountTypeRuleResponse>>> List(
        [FromQuery] string? appliesTo,
        [FromQuery] bool? isActive,
        [FromQuery] Guid? targetAccountTypeId,
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
            return Ok(await accountTypeRuleService.ListAsync(
                tenantId, appliesTo, isActive, targetAccountTypeId, after, limit, cancellationToken));
        }
        catch (TenantNotFoundException)
        {
            return StatusCode(StatusCodes.Status403Forbidden);
        }
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<AccountTypeRuleResponse>> Get(Guid id, CancellationToken cancellationToken)
    {
        if (!Request.Headers.TryGetValue(TenantConstants.TenantHeaderName, out var tenantHeaderValue) ||
            !Guid.TryParse(tenantHeaderValue, out var tenantId))
        {
            return BadRequest($"Missing or invalid '{TenantConstants.TenantHeaderName}' header.");
        }

        try
        {
            var response = await accountTypeRuleService.GetAsync(tenantId, id, cancellationToken);
            return response is null ? NotFound() : Ok(response);
        }
        catch (TenantNotFoundException)
        {
            return StatusCode(StatusCodes.Status403Forbidden);
        }
    }
}
