using Microsoft.EntityFrameworkCore;
using Thor.Authorizer.Core.DataAccess;
using Thor.DataLayer.Data;

namespace Thor.Authorizer.Function.DataAccess;

/// <summary>
/// Resolves a tenant's routing (by subdomain) from the Master DB over a direct, in-VPC
/// connection through the RDS Proxy, authenticating with an RDS IAM token as the least-privilege
/// read-only <c>thor_authorizer</c> role — no password, no RDS Data API. Reads only the two
/// <c>auth</c> tables via <see cref="MasterDbContext"/>. Returns null on no match (the caller
/// negative-caches), never throws for a missing tenant.
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

    public async Task<TenantRoute?> GetBySubdomainAsync(string subdomain)
    {
        await using var context = _contextFactory.Create(_connectionInfo);

        var tenant = await context.Tenants
            .AsNoTracking()
            .Include(t => t.Routing)
            .FirstOrDefaultAsync(t => t.Subdomain == subdomain);

        if (tenant?.Routing is null)
        {
            return null;
        }

        return new TenantRoute(
            TenantId: tenant.TenantId.ToString(),
            Subdomain: tenant.Subdomain,
            UserPoolId: tenant.Routing.UserPoolId,
            AppClientId: tenant.Routing.AppClientId,
            Region: tenant.Routing.Region);
    }
}
