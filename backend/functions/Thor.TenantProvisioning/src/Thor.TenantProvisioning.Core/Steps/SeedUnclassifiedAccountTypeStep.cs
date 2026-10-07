using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Thor.DataConnectionManager;
using Thor.DataLayer.Models.Tenants;
using Thor.TenantProvisioning.Core.Models;

namespace Thor.TenantProvisioning.Core.Steps;

/// <summary>
/// Final step — seeds the well-known <c>Unclassified</c> account type into the tenant's database through
/// the tenant connection manager. Runs after <see cref="FinalizeRoutingStep"/>, which writes the
/// routing row the connection manager resolves from. Idempotent: a no-op if the row already exists.
/// </summary>
public sealed class SeedUnclassifiedAccountTypeStep(
    ITenantConnectionManager tenantConnectionManager,
    ILogger<SeedUnclassifiedAccountTypeStep> logger)
{
    public async Task<ProvisioningState> RunAsync(ProvisioningState state, CancellationToken cancellationToken = default)
    {
        if (state.TenantId is not { } tenantId)
        {
            throw new InvalidOperationException("TenantId is not set; the SeedTenantMetadata step must run first.");
        }

        using var tenantDb = await tenantConnectionManager.GetTenantDbContextAsync(tenantId, cancellationToken);

        if (await tenantDb.AccountTypes.AnyAsync(a => a.Id == WellKnownAccountTypes.Unclassified, cancellationToken))
        {
            logger.LogInformation("Unclassified account type already present for tenant {TenantId}; skipping.", tenantId);
            return state;
        }

        tenantDb.AccountTypes.Add(new AccountType
        {
            Id = WellKnownAccountTypes.Unclassified,
            Name = WellKnownAccountTypes.UnclassifiedName,
            Description = WellKnownAccountTypes.UnclassifiedDescription,
            IsHuman = false,
        });
        await tenantDb.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Seeded Unclassified account type for tenant {TenantId}.", tenantId);
        return state;
    }
}
