using Thor.Api.Models;
using Thor.DataConnectionManager;
using Thor.DataLayer.Models.Tenants;
using Thor.DataLayer.Repositories;

namespace Thor.Api.Services;

/// <summary>Backs <c>GET /identities</c> and <c>GET /identities/{id}</c>: reads <see cref="IdentityRecord"/> rows from the caller's tenant database.</summary>
public sealed class IdentityService(ITenantConnectionManager tenantConnectionManager)
{
    public async Task<CursorPage<IdentityResponse>> ListAsync(
        Guid tenantId, bool? isActive, Guid? after, int limit, CancellationToken cancellationToken)
    {
        using var tenantDb = await tenantConnectionManager.GetTenantDbContextAsync(tenantId, cancellationToken);
        var page = await new IdentityRepository(tenantDb).ListAsync(isActive, after, limit, cancellationToken);
        return new CursorPage<IdentityResponse>(page.Items.Select(ToResponse).ToList(), page.NextCursor);
    }

    public async Task<IdentityResponse?> GetAsync(Guid tenantId, Guid id, CancellationToken cancellationToken)
    {
        using var tenantDb = await tenantConnectionManager.GetTenantDbContextAsync(tenantId, cancellationToken);
        var identity = await new IdentityRepository(tenantDb).GetByIdAsync(id, cancellationToken);
        return identity is null ? null : ToResponse(identity);
    }

    private static IdentityResponse ToResponse(IdentityRecord identity) => new(
        identity.Id, identity.Source, identity.HrEmployeeId, identity.DisplayName, identity.Email,
        identity.GivenName, identity.Surname, identity.Department, identity.Title, identity.Company,
        identity.IsActive, identity.CreatedAt, identity.UpdatedAt);
}
