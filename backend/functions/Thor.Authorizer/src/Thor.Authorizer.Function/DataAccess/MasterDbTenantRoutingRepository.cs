using Microsoft.EntityFrameworkCore;
using Thor.Authorizer.Core.DataAccess;
using Thor.DataLayer.Data;
using Thor.DataLayer.Models;

namespace Thor.Authorizer.Function.DataAccess;

/// <summary>
/// Resolves a tenant's routing (by Cognito user pool id or tenant id) from the Master DB over a
/// direct, in-VPC connection through the RDS Proxy, authenticating with an RDS IAM token as the
/// least-privilege read-only <c>thor_authorizer</c> role — no password, no RDS Data API. Reads only
/// <c>auth.tenant_routing</c> via <see cref="MasterDbContext"/>. Returns null on no match (the
/// caller negative-caches), never throws for a missing tenant.
/// </summary>
/// <remarks>
/// A fresh <see cref="MasterDbContext"/> is created and disposed per call: the factory mints a
/// short-lived IAM token per <c>Create</c>, and the authorizer's <c>TenantRoutingCache</c>
/// (10-min TTL) already gates how often this repository is hit, so per-call context creation is
/// simple and avoids holding a DbContext in a singleton.
/// </remarks>
public sealed class MasterDbTenantRoutingRepository : ITenantRoutingRepository
{
    private readonly IMasterDbContextFactory _contextFactory;
    private readonly MasterConnectionInfo _connectionInfo;

    public MasterDbTenantRoutingRepository(IMasterDbContextFactory contextFactory, MasterConnectionInfo connectionInfo)
    {
        _contextFactory = contextFactory;
        _connectionInfo = connectionInfo;
    }

    // user_pool_id is unique (IX_tenant_routing_user_pool_id), one pool per tenant (ADR §5).
    public async Task<TenantRoute?> GetByUserPoolIdAsync(string userPoolId)
    {
        await using var context = _contextFactory.Create(_connectionInfo);

        var routing = await context.TenantRoutings
            .AsNoTracking()
            .SingleOrDefaultAsync(r => r.UserPoolId == userPoolId);

        return routing is null ? null : ToRoute(routing);
    }

    public async Task<TenantRoute?> GetByTenantIdAsync(string tenantId)
    {
        if (!Guid.TryParse(tenantId, out var id))
        {
            return null;
        }

        await using var context = _contextFactory.Create(_connectionInfo);

        var routing = await context.TenantRoutings
            .AsNoTracking()
            .SingleOrDefaultAsync(r => r.TenantId == id);

        return routing is null ? null : ToRoute(routing);
    }

    private static TenantRoute ToRoute(TenantRouting routing) => new(
        TenantId: routing.TenantId.ToString(),
        UserPoolId: routing.UserPoolId,
        AppClientId: routing.AppClientId,
        Region: routing.Region);
}
