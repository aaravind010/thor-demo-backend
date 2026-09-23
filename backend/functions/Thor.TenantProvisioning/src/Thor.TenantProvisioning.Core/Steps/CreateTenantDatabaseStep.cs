using Microsoft.Extensions.Logging;
using Thor.TenantProvisioning.Core.Abstractions;
using Thor.TenantProvisioning.Core.Models;

namespace Thor.TenantProvisioning.Core.Steps;

/// <summary>
/// Step 2 — creates the tenant's database + <c>_rw</c>/<c>_ro</c> IAM roles on the existing
/// cluster, recording the database name and role names in the state. Runs right after the
/// tenant row is seeded (needs <see cref="ProvisioningState.TenantId"/>). Idempotent via the
/// provisioner.
/// </summary>
public sealed class CreateTenantDatabaseStep(
    ITenantDatabaseProvisioner provisioner,
    ILogger<CreateTenantDatabaseStep> logger)
{
    public async Task<ProvisioningState> RunAsync(ProvisioningState state, CancellationToken cancellationToken = default)
    {
        if (state.TenantId is not { } tenantId)
        {
            throw new InvalidOperationException("TenantId is not set; the SeedTenantMetadata step must run first.");
        }

        var result = await provisioner.ProvisionAsync(
            new TenantDatabaseRequest(tenantId, state.Subdomain), cancellationToken);

        logger.LogInformation("Provisioned database {DatabaseName} with RW/RO roles for tenant {TenantId}.",
            result.DatabaseName, tenantId);

        return state with
        {
            DatabaseName = result.DatabaseName,
            RwDbUser = result.RwDbUser,
            RoDbUser = result.RoDbUser,
        };
    }
}
