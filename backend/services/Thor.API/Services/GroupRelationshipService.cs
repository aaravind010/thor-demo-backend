using Thor.Api.Models;
using Thor.DataConnectionManager;
using Thor.DataLayer.Repositories;
using Thor.Graph;

namespace Thor.Api.Services;

/// <summary>
/// Backs the graph reads under <c>/groups/{id}</c>. Same existence and tenant check as
/// <see cref="AccountRelationshipService"/>, against the tenant's <c>grp</c> table.
/// </summary>
public sealed class GroupRelationshipService(
    ITenantConnectionManager tenantConnectionManager, IGraphRelationshipReader graphReader)
{
    private const string VertexType = "grp";

    public async Task<CursorPage<RelationshipResponse>?> GetRelationshipsAsync(
        Guid tenantId, Guid groupId, GraphDirection direction, IReadOnlyCollection<string> relTypes,
        Guid? after, int limit, CancellationToken cancellationToken)
    {
        if (!await ExistsAsync(tenantId, groupId, cancellationToken))
        {
            return null;
        }

        var page = await graphReader.GetNeighborsAsync(
            tenantId, new GraphVertexRef(VertexType, groupId), direction, relTypes, after, limit, cancellationToken);
        return RelationshipResponseMapper.ToResponse(page);
    }

    /// <summary>Accounts and groups that are members of this group: directly when <paramref name="maxDepth"/> is 1, through nested groups beyond that.</summary>
    public Task<CursorPage<ReachableEntityResponse>?> GetMembersAsync(
        Guid tenantId, Guid groupId, int maxDepth, Guid? after, int limit, CancellationToken cancellationToken) =>
        GetReachableAsync(tenantId, groupId, GraphDirection.In, maxDepth, after, limit, cancellationToken);

    /// <summary>Groups this group is a member of: directly when <paramref name="maxDepth"/> is 1, through nested groups beyond that.</summary>
    public Task<CursorPage<ReachableEntityResponse>?> GetParentsAsync(
        Guid tenantId, Guid groupId, int maxDepth, Guid? after, int limit, CancellationToken cancellationToken) =>
        GetReachableAsync(tenantId, groupId, GraphDirection.Out, maxDepth, after, limit, cancellationToken);

    public async Task<CursorPage<AccessResponse>?> GetAccessAsync(
        Guid tenantId, Guid groupId, int maxDepth, Guid? after, int limit, CancellationToken cancellationToken)
    {
        if (!await ExistsAsync(tenantId, groupId, cancellationToken))
        {
            return null;
        }

        var page = await graphReader.GetEffectiveAccessAsync(
            tenantId, new GraphVertexRef(VertexType, groupId), maxDepth, after, limit, cancellationToken);
        return RelationshipResponseMapper.ToResponse(page);
    }

    private async Task<CursorPage<ReachableEntityResponse>?> GetReachableAsync(
        Guid tenantId, Guid groupId, GraphDirection direction, int maxDepth, Guid? after, int limit,
        CancellationToken cancellationToken)
    {
        if (!await ExistsAsync(tenantId, groupId, cancellationToken))
        {
            return null;
        }

        var page = await graphReader.GetReachableAsync(
            tenantId, new GraphVertexRef(VertexType, groupId), GraphRelTypes.MemberOf, direction,
            maxDepth, after, limit, cancellationToken);
        return RelationshipResponseMapper.ToResponse(page);
    }

    private async Task<bool> ExistsAsync(Guid tenantId, Guid groupId, CancellationToken cancellationToken)
    {
        using var tenantDb = await tenantConnectionManager.GetTenantDbContextAsync(tenantId, cancellationToken);
        return await new GrpRepository(tenantDb).GetByIdAsync(groupId, cancellationToken) is not null;
    }
}
