using Microsoft.Extensions.Logging;
using Thor.TenantProvisioning.Core.Abstractions;
using Thor.TenantProvisioning.Core.Models;

namespace Thor.TenantProvisioning.Core.Steps;

/// <summary>
/// Step 3 — applies <c>TenantDbContext</c>'s EF Core migrations to the tenant's database and
/// grants the <c>_ro</c> role visibility into the resulting <c>tenant</c> schema. Runs right
/// after the database + roles exist (needs <see cref="ProvisioningState.DatabaseName"/> and
/// the RW/RO role names). Idempotent via the migrator.
/// </summary>
public sealed class CreateTenantTablesStep(
    ITenantSchemaMigrator migrator,
    ILogger<CreateTenantTablesStep> logger)
{
    public async Task<ProvisioningState> RunAsync(ProvisioningState state, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(state.DatabaseName) ||
            string.IsNullOrEmpty(state.RwDbUser) ||
            string.IsNullOrEmpty(state.RoDbUser))
        {
            throw new InvalidOperationException(
                "Database routing is not set; the CreateTenantDatabase step must run first.");
        }

        await migrator.MigrateAsync(
            new TenantSchemaMigrationRequest(state.DatabaseName, state.RwDbUser, state.RoDbUser), cancellationToken);

        logger.LogInformation("Applied tenant schema migrations to {DatabaseName}.", state.DatabaseName);

        return state;
    }
}
