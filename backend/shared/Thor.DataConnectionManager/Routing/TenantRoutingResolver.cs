using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using Thor.DataConnectionManager.Exceptions;
using Thor.DataLayer.Data;
using Thor.DataLayer.Models;

namespace Thor.DataConnectionManager.Routing;

public sealed class TenantRoutingResolver(IMasterDbContextFactory masterDbContextFactory, MasterConnectionInfo masterConnectionInfo)
    : ITenantRoutingResolver
{
    // Short TTL, no explicit invalidation: routing changes are rare provisioning events,
    // not routine traffic, so a bounded staleness window is an acceptable trade-off against
    // hitting the Master DB on every call (e.g. every presigned-URL request).
    private readonly ConcurrentDictionary<Guid, (TenantRouting Routing, DateTimeOffset ExpiresAt)> _cache = new();
    private readonly ConcurrentDictionary<string, (TenantRouting Routing, DateTimeOffset ExpiresAt)> _subdomainCache = new();

    public async Task<TenantRouting> ResolveAsync(Guid tenantId, CancellationToken cancellationToken = default)
    {
        if (_cache.TryGetValue(tenantId, out var cached) && cached.ExpiresAt > DateTimeOffset.UtcNow)
        {
            return cached.Routing;
        }

        using var db = masterDbContextFactory.Create(masterConnectionInfo);

        var routing = await db.TenantRoutings
            .SingleOrDefaultAsync(r => r.TenantId == tenantId, cancellationToken);

        if (routing is null)
        {
            throw new TenantNotFoundException(tenantId);
        }

        _cache[tenantId] = (routing, DateTimeOffset.UtcNow + TenantRoutingConstants.CacheTtl);
        return routing;
    }

    public async Task<TenantRouting> ResolveBySubdomainAsync(string subdomain, CancellationToken cancellationToken = default)
    {
        if (_subdomainCache.TryGetValue(subdomain, out var cached) && cached.ExpiresAt > DateTimeOffset.UtcNow)
        {
            return cached.Routing;
        }

        using var db = masterDbContextFactory.Create(masterConnectionInfo);

        var tenant = await db.Tenants
            .Include(t => t.Routing)
            .SingleOrDefaultAsync(t => t.Subdomain == subdomain, cancellationToken);

        if (tenant?.Routing is null)
        {
            throw new TenantNotFoundException(subdomain);
        }

        _subdomainCache[subdomain] = (tenant.Routing, DateTimeOffset.UtcNow + TenantRoutingConstants.CacheTtl);
        return tenant.Routing;
    }
}
