using Microsoft.Extensions.Logging;
using Thor.DataLayer.Models;
using Thor.DataLayer.Repositories;
using Thor.TenantProvisioning.Core.Models;

namespace Thor.TenantProvisioning.Core.Steps;

/// <summary>
/// Step 1 — writes the tenant row to the Master DB with status <c>Provisioning</c> and
/// returns its id. Idempotent: if a tenant with the same subdomain already exists, its
/// id is reused instead of inserting a duplicate.
/// </summary>
public sealed class SeedTenantMetadataStep(
    ITenantRepository tenants,
    TimeProvider timeProvider,
    ILogger<SeedTenantMetadataStep> logger)
{
    public async Task<ProvisioningState> RunAsync(ProvisioningState state, CancellationToken cancellationToken = default)
    {
        var existing = await tenants.GetBySubdomainAsync(state.Subdomain, cancellationToken);
        if (existing is not null)
        {
            logger.LogInformation("Tenant for subdomain {Subdomain} already exists ({TenantId}); reusing.",
                state.Subdomain, existing.TenantId);
            return state with { TenantId = existing.TenantId };
        }

        var now = timeProvider.GetUtcNow();
        var tenant = new Tenant
        {
            TenantId = Guid.NewGuid(),
            DisplayName = state.DisplayName,
            Subdomain = state.Subdomain,
            TierId = state.TierId,
            IsolationTypeId = state.IsolationTypeId,
            StatusId = (short)TenantStatus.Provisioning,
            CreatedAt = now,
            UpdatedAt = now,
        };

        await tenants.AddAsync(tenant, cancellationToken);
        await tenants.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Seeded tenant {TenantId} for subdomain {Subdomain}.", tenant.TenantId, state.Subdomain);
        return state with { TenantId = tenant.TenantId };
    }
}
