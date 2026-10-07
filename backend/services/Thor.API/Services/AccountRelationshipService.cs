using Thor.Api.Models;
using Thor.DataConnectionManager;
using Thor.DataLayer.Repositories;
using Thor.Graph;

namespace Thor.Api.Services;

/// <summary>
/// Backs the graph reads under <c>/accounts/{id}</c>. The account is looked up in the tenant's
/// Postgres database first: that resolves the tenant through its routing row (failing closed with
/// <see cref="Thor.DataConnectionManager.Exceptions.TenantNotFoundException"/>), and gives a
/// missing account a null (404) rather than an empty page. An account that exists but has not been
/// graph-loaded yet reads as an empty page.
/// </summary>
public sealed class AccountRelationshipService(
    ITenantConnectionManager tenantConnectionManager, IGraphRelationshipReader graphReader)
{
    private const string VertexType = "account";

    public async Task<CursorPage<RelationshipResponse>?> GetRelationshipsAsync(
        Guid tenantId, Guid accountId, GraphDirection direction, IReadOnlyCollection<string> relTypes,
        Guid? after, int limit, CancellationToken cancellationToken)
    {
        if (!await ExistsAsync(tenantId, accountId, cancellationToken))
        {
            return null;
        }

        var page = await graphReader.GetNeighborsAsync(
            tenantId, new GraphVertexRef(VertexType, accountId), direction, relTypes, after, limit, cancellationToken);
        return RelationshipResponseMapper.ToResponse(page);
    }

    /// <summary>Groups the account belongs to: directly when <paramref name="maxDepth"/> is 1, through nested groups beyond that.</summary>
    public async Task<CursorPage<ReachableEntityResponse>?> GetGroupsAsync(
        Guid tenantId, Guid accountId, int maxDepth, Guid? after, int limit, CancellationToken cancellationToken)
    {
        if (!await ExistsAsync(tenantId, accountId, cancellationToken))
        {
            return null;
        }

        var page = await graphReader.GetReachableAsync(
            tenantId, new GraphVertexRef(VertexType, accountId), GraphRelTypes.MemberOf, GraphDirection.Out,
            maxDepth, after, limit, cancellationToken);
        return RelationshipResponseMapper.ToResponse(page);
    }

    public async Task<CursorPage<AccessResponse>?> GetAccessAsync(
        Guid tenantId, Guid accountId, int maxDepth, Guid? after, int limit, CancellationToken cancellationToken)
    {
        if (!await ExistsAsync(tenantId, accountId, cancellationToken))
        {
            return null;
        }

        var page = await graphReader.GetEffectiveAccessAsync(
            tenantId, new GraphVertexRef(VertexType, accountId), maxDepth, after, limit, cancellationToken);
        return RelationshipResponseMapper.ToResponse(page);
    }

    private async Task<bool> ExistsAsync(Guid tenantId, Guid accountId, CancellationToken cancellationToken)
    {
        using var tenantDb = await tenantConnectionManager.GetTenantDbContextAsync(tenantId, cancellationToken);
        return await new AccountRepository(tenantDb).GetByIdAsync(accountId, cancellationToken) is not null;
    }
}
