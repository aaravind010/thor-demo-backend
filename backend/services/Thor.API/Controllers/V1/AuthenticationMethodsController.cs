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
[Route("v{version:apiVersion}/authentication-methods")]
public class AuthenticationMethodsController(
    AuthenticationMethodService authenticationMethodService,
    ILogger<AuthenticationMethodsController> logger) : ControllerBase
{
    // The Lambda authorizer validates the caller and sets X-THOR-TENANT-ID / X-THOR-ACTOR-ID;
    // requests reaching Thor.Api are trusted, so the headers are read directly rather than
    // re-verified here.
    [HttpPost]
    public async Task<ActionResult<AuthenticationMethodResponse>> Create(
        [FromBody] CreateAuthenticationMethodRequest request, CancellationToken cancellationToken)
    {
        if (!Request.Headers.TryGetValue(TenantConstants.TenantHeaderName, out var tenantHeaderValue) ||
            !Guid.TryParse(tenantHeaderValue, out var tenantId))
        {
            return BadRequest($"Missing or invalid '{TenantConstants.TenantHeaderName}' header.");
        }

        if (!Request.Headers.TryGetValue(TenantConstants.ActorHeaderName, out var actorHeaderValue) ||
            string.IsNullOrWhiteSpace(actorHeaderValue))
        {
            return BadRequest($"Missing '{TenantConstants.ActorHeaderName}' header.");
        }

        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return BadRequest("Name is required.");
        }

        if (request.Values.Count == 0)
        {
            return BadRequest("At least one authentication value is required.");
        }

        try
        {
            var response = await authenticationMethodService.CreateAsync(
                tenantId, actorHeaderValue!, request, cancellationToken);

            return StatusCode(StatusCodes.Status201Created, response);
        }
        catch (AuthenticationTypeNotFoundException ex)
        {
            logger.LogWarning(ex, "Authentication method creation rejected: authentication type not found");
            return BadRequest(ex.Message);
        }
        catch (InvalidAuthenticationFieldsException ex)
        {
            logger.LogWarning(ex, "Authentication method creation rejected: invalid authentication fields");
            return BadRequest(ex.Message);
        }
        catch (TenantNotFoundException)
        {
            logger.LogWarning("Authentication method creation rejected: tenant {TenantId} not found", tenantId);
            return StatusCode(StatusCodes.Status403Forbidden);
        }
    }
}
