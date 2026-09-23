using Asp.Versioning;
using Microsoft.AspNetCore.Mvc;
using Thor.Api.Constants;
using Thor.Api.Models;
using Thor.Api.Services;
using Thor.DataConnectionManager.Exceptions;

namespace Thor.Api.Controllers.V1;

[ApiController]
[ApiVersion("1.0")]
[Route("v{version:apiVersion}/account-type")]
public class AccountTypesController(AccountTypeService accountTypeService) : ControllerBase
{
    // The Lambda authorizer validates the caller and sets X-THOR-TENANT-ID / X-THOR-ACTOR-ID;
    // requests reaching Thor.Api are trusted, so the header is read directly rather than
    // re-verified here.
    [HttpPost]
    public async Task<ActionResult<AccountTypeResponse>> Create(
        [FromBody] CreateAccountTypeRequest request, CancellationToken cancellationToken)
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

        if (string.IsNullOrWhiteSpace(request.Description))
        {
            return BadRequest("Description is required.");
        }

        try
        {
            var response = await accountTypeService.CreateAsync(tenantId, request, cancellationToken);
            return StatusCode(StatusCodes.Status201Created, response);
        }
        catch (TenantNotFoundException)
        {
            return StatusCode(StatusCodes.Status403Forbidden);
        }
    }
}
