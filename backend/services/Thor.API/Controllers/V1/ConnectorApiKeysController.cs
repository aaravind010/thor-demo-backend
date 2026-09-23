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
[Route("v{version:apiVersion}/connector-api-keys")]
public class ConnectorApiKeysController(
    ConnectorApiKeyService connectorApiKeyService,
    ILogger<ConnectorApiKeysController> logger) : ControllerBase
{
    // The Lambda authorizer validates the caller and sets X-THOR-TENANT-ID / X-THOR-ACTOR-ID;
    // requests reaching Thor.Api are trusted, so the headers are read directly rather than
    // re-verified here.
    [HttpPost]
    public async Task<ActionResult<ConnectorApiKeyResponse>> Create(
        [FromBody] CreateConnectorApiKeyRequest request, CancellationToken cancellationToken)
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

        if (string.IsNullOrWhiteSpace(request.Scope))
        {
            return BadRequest("Scope is required.");
        }

        try
        {
            var response = await connectorApiKeyService.CreateAsync(tenantId, request, cancellationToken);
            return StatusCode(StatusCodes.Status201Created, response);
        }
        catch (ScopeNotFoundException ex)
        {
            logger.LogWarning(ex, "Connector API key creation rejected: scope not found");
            return BadRequest(ex.Message);
        }
        catch (TenantNotFoundException)
        {
            logger.LogWarning("Connector API key creation rejected: tenant {TenantId} not found", tenantId);
            return StatusCode(StatusCodes.Status403Forbidden);
        }
    }
}
