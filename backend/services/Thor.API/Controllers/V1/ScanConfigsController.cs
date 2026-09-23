using Microsoft.AspNetCore.Mvc;
using Thor.Api.Constants;
using Thor.Api.Exceptions;
using Thor.Api.Models;
using Thor.Api.Services;
using Thor.DataConnectionManager.Exceptions;

namespace Thor.Api.Controllers.V1;

[ApiController]
[Route("/scan-config")]
public class ScanConfigsController(ScanConfigService scanConfigService) : ControllerBase
{
    // The Lambda authorizer validates the caller and sets X-THOR-TENANT-ID / X-THOR-ACTOR-ID;
    // requests reaching Thor.Api are trusted, so the headers are read directly rather than
    // re-verified here.
    [HttpPost]
    public async Task<ActionResult<ScanConfigResponse>> Create(
        [FromBody] CreateScanConfigRequest request, CancellationToken cancellationToken)
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

        if (request.SourceIds.Count == 0)
        {
            return BadRequest("At least one source id is required.");
        }

        try
        {
            var response = await scanConfigService.CreateAsync(
                tenantId, actorHeaderValue!, request, cancellationToken);
            return StatusCode(StatusCodes.Status201Created, response);
        }
        catch (SourceNotFoundException ex)
        {
            return BadRequest(ex.Message);
        }
        catch (MixedSourceConnectorTypesException ex)
        {
            return BadRequest(ex.Message);
        }
        catch (AuthenticationMethodNotFoundException ex)
        {
            return BadRequest(ex.Message);
        }
        catch (AuthenticationTypeNotFoundException ex)
        {
            return BadRequest(ex.Message);
        }
        catch (AuthMethodConnectorTypeMismatchException ex)
        {
            return BadRequest(ex.Message);
        }
        catch (InvalidScanConnectorConfigValuesException ex)
        {
            return BadRequest(ex.Message);
        }
        catch (TenantNotFoundException)
        {
            return StatusCode(StatusCodes.Status403Forbidden);
        }
    }
}
