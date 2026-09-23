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
[Route("v{version:apiVersion}/source")]
public class SourcesController(SourceService sourceService) : ControllerBase
{
    // The Lambda authorizer validates the caller and sets X-THOR-TENANT-ID / X-THOR-ACTOR-ID;
    // requests reaching Thor.Api are trusted, so the header is read directly rather than
    // re-verified here.
    [HttpPost]
    public async Task<ActionResult<SourceResponse>> Create(
        [FromBody] CreateSourceRequest request, CancellationToken cancellationToken)
    {
        if (!Request.Headers.TryGetValue(TenantConstants.TenantHeaderName, out var tenantHeaderValue) ||
            !Guid.TryParse(tenantHeaderValue, out var tenantId))
        {
            return BadRequest($"Missing or invalid '{TenantConstants.TenantHeaderName}' header.");
        }

        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return BadRequest("Name is required.");
        }

        if (string.IsNullOrWhiteSpace(request.Config))
        {
            return BadRequest("Config is required.");
        }

        try
        {
            var response = await sourceService.CreateAsync(tenantId, request, cancellationToken);
            return StatusCode(StatusCodes.Status201Created, response);
        }
        catch (ConnectorTypeNotFoundException ex)
        {
            return BadRequest(ex.Message);
        }
        catch (TenantNotFoundException)
        {
            return StatusCode(StatusCodes.Status403Forbidden);
        }
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<SourceResponse>>> List(CancellationToken cancellationToken)
    {
        if (!Request.Headers.TryGetValue(TenantConstants.TenantHeaderName, out var tenantHeaderValue) ||
            !Guid.TryParse(tenantHeaderValue, out var tenantId))
        {
            return BadRequest($"Missing or invalid '{TenantConstants.TenantHeaderName}' header.");
        }

        try
        {
            var response = await sourceService.ListAsync(tenantId, cancellationToken);
            return Ok(response);
        }
        catch (TenantNotFoundException)
        {
            return StatusCode(StatusCodes.Status403Forbidden);
        }
    }
}
