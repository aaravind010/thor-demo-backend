using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using Thor.Core.Logging;
using Thor.DataLayer.Data;

namespace Thor.DataConnectionManager.Routing;

/// <summary>
/// Resolves a tenant's external HTTP log sink endpoint from the Master metadata DB.
/// Cached with a TTL like <see cref="TenantRoutingResolver"/> — this is called on every
/// log event routed through the tenant sink map, not just once per request, so an
/// uncached DB hit per call would be far too frequent even though it runs off the
/// request thread (see ThorLoggingExtensions.UseThorLogging).
/// </summary>
public sealed class TenantLogSinkResolver(IMasterDbContextFactory masterDbContextFactory, MasterConnectionInfo masterConnectionInfo)
    : ITenantLogSinkResolver
{
    private readonly ConcurrentDictionary<string, (TenantLogSinkOptions? Options, DateTimeOffset ExpiresAt)> _cache = new();

    public TenantLogSinkOptions? Resolve(string tenantId)
    {
        if (_cache.TryGetValue(tenantId, out var cached) && cached.ExpiresAt > DateTimeOffset.UtcNow)
        {
            return cached.Options;
        }

        if (!Guid.TryParse(tenantId, out var tenantGuid))
        {
            return null;
        }

        using var db = masterDbContextFactory.Create(masterConnectionInfo);

        var options = db.TenantLogSinkConfigs
            .AsNoTracking()
            .Where(c => c.TenantId == tenantGuid)
            .Select(c => new TenantLogSinkOptions(c.HttpEndpoint, c.BatchSizeLimit))
            .SingleOrDefault();

        _cache[tenantId] = (options, DateTimeOffset.UtcNow + TenantRoutingConstants.CacheTtl);
        return options;
    }
}
