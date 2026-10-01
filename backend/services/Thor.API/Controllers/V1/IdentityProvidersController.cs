using Asp.Versioning;
using Microsoft.AspNetCore.Mvc;
using Thor.Api.Constants;
using Thor.Api.Exceptions;
using Thor.Api.Filters;
using Thor.Api.Models;
using Thor.Api.Services;
using Thor.DataConnectionManager.Exceptions;

namespace Thor.Api.Controllers.V1;

/// <summary>
/// Tenant-admin management of the tenant's SAML/OIDC identity providers (see
/// <see cref="IdentityProviderService"/>). Every action requires the caller to be a tenant admin.
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("v{version:apiVersion}/identity-providers")]
[RequireTenantAdmin]
public class IdentityProvidersController(
    IdentityProviderService identityProviderService,
    ILogger<IdentityProvidersController> logger) : ControllerBase
{
    // The Lambda authorizer validates the caller and API Gateway sets X-THOR-TENANT-ID /
    // X-THOR-CALLER-GROUPS from its verified context; requests reaching Thor.Api are trusted, so
    // the headers are read directly rather than re-verified here.
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<TenantIdentityProviderSummaryResponse>>(StatusCodes.Status200OK)]
    public Task<IActionResult> List(CancellationToken cancellationToken) =>
        HandleAsync(async tenantId => Ok(await identityProviderService.ListAsync(tenantId, cancellationToken)));

    [HttpGet("{providerName}")]
    [ProducesResponseType<TenantIdentityProviderResponse>(StatusCodes.Status200OK)]
    public Task<IActionResult> Get(string providerName, CancellationToken cancellationToken) =>
        HandleAsync(async tenantId => Ok(await identityProviderService.GetAsync(tenantId, providerName, cancellationToken)));

    [HttpPost]
    [ProducesResponseType<TenantIdentityProviderResponse>(StatusCodes.Status201Created)]
    public Task<IActionResult> Create(
        [FromBody] CreateTenantIdentityProviderRequest request, CancellationToken cancellationToken) =>
        HandleAsync(async tenantId => StatusCode(
            StatusCodes.Status201Created, await identityProviderService.CreateAsync(tenantId, request, cancellationToken)));

    [HttpPut("{providerName}")]
    [ProducesResponseType<TenantIdentityProviderResponse>(StatusCodes.Status200OK)]
    public Task<IActionResult> Update(
        string providerName, [FromBody] UpdateTenantIdentityProviderRequest request, CancellationToken cancellationToken) =>
        HandleAsync(async tenantId => Ok(await identityProviderService.UpdateAsync(tenantId, providerName, request, cancellationToken)));

    [HttpDelete("{providerName}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public Task<IActionResult> Delete(string providerName, CancellationToken cancellationToken) =>
        HandleAsync(async tenantId =>
        {
            await identityProviderService.DeleteAsync(tenantId, providerName, cancellationToken);
            return NoContent();
        });

    /// <summary>Parses the trusted tenant header, runs the action, and maps service exceptions to status codes.</summary>
    private async Task<IActionResult> HandleAsync(Func<Guid, Task<IActionResult>> action)
    {
        if (!Request.Headers.TryGetValue(TenantConstants.TenantHeaderName, out var tenantHeaderValue) ||
            !Guid.TryParse(tenantHeaderValue, out var tenantId))
        {
            return BadRequest($"Missing or invalid '{TenantConstants.TenantHeaderName}' header.");
        }

        try
        {
            return await action(tenantId);
        }
        catch (InvalidIdentityProviderException ex)
        {
            logger.LogWarning(ex, "Identity provider request rejected for tenant {TenantId}", tenantId);
            return BadRequest(ex.Message);
        }
        catch (IdentityProviderNotFoundException ex)
        {
            logger.LogWarning("Identity provider {ProviderName} not found for tenant {TenantId}", ex.ProviderName, tenantId);
            return NotFound(ex.Message);
        }
        catch (IdentityProviderConflictException ex)
        {
            logger.LogWarning("Identity provider {ProviderName} already exists for tenant {TenantId}", ex.ProviderName, tenantId);
            return Conflict(ex.Message);
        }
        catch (TenantNotFoundException)
        {
            logger.LogWarning("Identity provider request rejected: tenant {TenantId} not found", tenantId);
            return StatusCode(StatusCodes.Status403Forbidden);
        }
    }
}
