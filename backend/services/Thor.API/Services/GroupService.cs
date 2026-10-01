using Thor.Api.Models;
using Thor.DataConnectionManager;
using Thor.DataLayer.Models.Tenants;
using Thor.DataLayer.Repositories;

namespace Thor.Api.Services;

/// <summary>Backs <c>GET /groups</c> and <c>GET /groups/{id}</c>: reads <see cref="Grp"/> rows from the caller's tenant database.</summary>
public sealed class GroupService(ITenantConnectionManager tenantConnectionManager)
{
    public async Task<CursorPage<GroupResponse>> ListAsync(
        Guid tenantId, Guid? sourceId, Guid? after, int limit, CancellationToken cancellationToken)
    {
        using var tenantDb = await tenantConnectionManager.GetTenantDbContextAsync(tenantId, cancellationToken);
        var page = await new GrpRepository(tenantDb).ListAsync(sourceId, after, limit, cancellationToken);
        return new CursorPage<GroupResponse>(
            page.Items.Select(g => ToResponse(g, includeRawAttributes: false)).ToList(), page.NextCursor);
    }

    public async Task<GroupResponse?> GetAsync(Guid tenantId, Guid id, CancellationToken cancellationToken)
    {
        using var tenantDb = await tenantConnectionManager.GetTenantDbContextAsync(tenantId, cancellationToken);
        var group = await new GrpRepository(tenantDb).GetByIdAsync(id, cancellationToken);
        return group is null ? null : ToResponse(group, includeRawAttributes: true);
    }

    // raw_attributes can be large, so list pages leave it out and only the single-row read returns it.
    private static GroupResponse ToResponse(Grp group, bool includeRawAttributes) => new(
        group.Id, group.SourceId, group.ConnectorType, group.NativeId, group.GroupClass, group.DisplayName,
        group.Email, group.DomainName, group.IsLargeGroup, group.IsDeleted, group.CreatedAt, group.UpdatedAt,
        includeRawAttributes ? group.RawAttributes : null);
}
