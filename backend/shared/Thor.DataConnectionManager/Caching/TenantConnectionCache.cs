using System.Collections.Concurrent;

namespace Thor.DataConnectionManager.Caching;

/// <summary>
/// In-memory cache of per-tenant <see cref="TenantConnectionEntry"/> (each holding a tenant's
/// NpgsqlDataSource), keyed by tenant ID. One data source per tenant gives a stable ADO.NET
/// connection pool — the IAM token is refreshed out-of-band by the data source's periodic
/// password provider, so the pool is never fragmented by a changing password.
///
/// No TTL: routing changes are rare provisioning events, and the underlying
/// TenantRoutingResolver already caps staleness with its own short TTL. Disposes each data
/// source on eviction (and all of them on shutdown) so pooled connections are released.
/// </summary>
public sealed class TenantConnectionCache : IAsyncDisposable
{
    private readonly ConcurrentDictionary<Guid, TenantConnectionEntry> _cache = new();

    public bool TryGet(Guid tenantId, out TenantConnectionEntry entry) => _cache.TryGetValue(tenantId, out entry!);

    /// <summary>
    /// Adds <paramref name="entry"/> if the tenant has none yet, else returns the existing one.
    /// The caller disposes its now-redundant data source when this returns a different entry
    /// (a benign race under first-use contention).
    /// </summary>
    public TenantConnectionEntry GetOrAdd(Guid tenantId, TenantConnectionEntry entry) => _cache.GetOrAdd(tenantId, entry);

    public async ValueTask EvictAsync(Guid tenantId)
    {
        if (_cache.TryRemove(tenantId, out var entry))
        {
            await entry.DataSource.DisposeAsync();
        }
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var key in _cache.Keys)
        {
            if (_cache.TryRemove(key, out var entry))
            {
                await entry.DataSource.DisposeAsync();
            }
        }
    }
}
