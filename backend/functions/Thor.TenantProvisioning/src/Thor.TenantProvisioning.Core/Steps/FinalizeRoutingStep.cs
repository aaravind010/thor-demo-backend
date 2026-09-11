using Microsoft.Extensions.Logging;
using Thor.DataLayer.Models;
using Thor.DataLayer.Repositories;
using Thor.TenantProvisioning.Core.Models;

namespace Thor.TenantProvisioning.Core.Steps;

/// <summary>
/// Step 5 (final) — upserts the tenant_routing row (Cognito ids + DB routing) and flips
/// the tenant to <c>Active</c>. Fail-closed: the tenant is only activated once routing
/// is fully written. Idempotent: re-running updates the existing routing row in place.
/// </summary>
/// <remarks>
/// Both repositories share the same scoped <see cref="Thor.DataLayer.Data.MasterDbContext"/>,
/// so a single SaveChanges persists the routing upsert and the tenant activation together.
/// </remarks>
public sealed class FinalizeRoutingStep(
    ITenantRepository tenants,
    ITenantRoutingRepository routings,
    TenantRoutingOptions routingOptions,
    TimeProvider timeProvider,
    ILogger<FinalizeRoutingStep> logger)
{
    public async Task<ProvisioningState> RunAsync(ProvisioningState state, CancellationToken cancellationToken = default)
    {
        if (state.TenantId is not { } tenantId)
        {
            throw new InvalidOperationException("TenantId is not set; the SeedTenantMetadata step must run first.");
        }

        if (string.IsNullOrEmpty(state.DatabaseName) ||
            string.IsNullOrEmpty(state.RwDbUser) ||
            string.IsNullOrEmpty(state.RoDbUser))
        {
            throw new InvalidOperationException(
                "Database routing is not set; the CreateTenantDatabase step must run first.");
        }

        if (string.IsNullOrEmpty(state.UserPoolId) || string.IsNullOrEmpty(state.AppClientId))
        {
            throw new InvalidOperationException("Cognito ids are not set; the ProvisionCognito step must run first.");
        }

        var now = timeProvider.GetUtcNow();

        var routing = await routings.GetByIdAsync(tenantId, cancellationToken);
        if (routing is null)
        {
            routing = new TenantRouting
            {
                TenantId = tenantId,
                ClusterEndpoint = routingOptions.ClusterEndpoint,
                DatabaseName = state.DatabaseName,
                Region = routingOptions.Region,
                UserPoolId = state.UserPoolId,
                AppClientId = state.AppClientId,
                DbUser = state.RwDbUser,
                ReadOnlyDbUser = state.RoDbUser,
                CreatedAt = now,
                UpdatedAt = now,
            };
            await routings.AddAsync(routing, cancellationToken);
        }
        else
        {
            routing.ClusterEndpoint = routingOptions.ClusterEndpoint;
            routing.DatabaseName = state.DatabaseName;
            routing.Region = routingOptions.Region;
            routing.UserPoolId = state.UserPoolId;
            routing.AppClientId = state.AppClientId;
            routing.DbUser = state.RwDbUser;
            routing.ReadOnlyDbUser = state.RoDbUser;
            routing.UpdatedAt = now;
            routings.Update(routing);
        }

        var tenant = await tenants.GetByIdAsync(tenantId, cancellationToken)
            ?? throw new InvalidOperationException($"Tenant {tenantId} not found while finalizing routing.");
        tenant.StatusId = (short)TenantStatus.Active;
        tenant.UpdatedAt = now;
        tenants.Update(tenant);

        await routings.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Finalized routing and activated tenant {TenantId}.", tenantId);
        return state;
    }
}
