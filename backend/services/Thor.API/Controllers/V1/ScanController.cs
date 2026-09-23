using Microsoft.AspNetCore.Mvc;
using Thor.Api.Constants;
using Thor.Api.Exceptions;
using Thor.Api.Models;
using Thor.Api.Services;
using Thor.DataConnectionManager.Exceptions;
using Thor.DataLayer.Models.Tenants;

namespace Thor.Api.Controllers.V1;

[ApiController]
[Route("/scan")]
public class ScanController(ScanService scanService) : ControllerBase
{
    // The Lambda authorizer validates the caller and sets X-THOR-TENANT-ID; requests reaching
    // Thor.Api are trusted, so the header is read directly rather than re-verified here. This
    // endpoint is called both by frontend users and a scheduled background service, so it does
    // not require an actor identity (Scan/ScanTask carry no created-by/updated-by audit columns).
    [HttpPost]
    public async Task<ActionResult<ScanResponse>> Create(
        [FromBody] CreateScanRequest request, CancellationToken cancellationToken)
    {
        if (!Request.Headers.TryGetValue(TenantConstants.TenantHeaderName, out var tenantHeaderValue) ||
            !Guid.TryParse(tenantHeaderValue, out var tenantId))
        {
            return BadRequest($"Missing or invalid '{TenantConstants.TenantHeaderName}' header.");
        }

        if (request.ScanConfigId == Guid.Empty)
        {
            return BadRequest("ScanConfigId is required.");
        }

        if (request.ScanType is not (ScanTriggerType.Scheduled or ScanTriggerType.Instant))
        {
            return BadRequest($"ScanType must be '{ScanTriggerType.Scheduled}' or '{ScanTriggerType.Instant}'.");
        }

        try
        {
            var response = await scanService.CreateAsync(tenantId, request, cancellationToken);
            return StatusCode(StatusCodes.Status201Created, response);
        }
        catch (ScanConfigNotFoundException ex)
        {
            return BadRequest(ex.Message);
        }
        catch (TenantNotFoundException)
        {
            return StatusCode(StatusCodes.Status403Forbidden);
        }
    }
}
