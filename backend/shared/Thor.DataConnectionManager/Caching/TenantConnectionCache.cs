using System.Collections.Concurrent;

namespace Thor.DataConnectionManager.Caching;

/// <summary>
/// In-memory cache of resolved tenant connection strings, keyed by tenant ID. Keyed by
/// tenant rather than the cluster/database/user embedded in the string itself, since the
/// DB user is only known after resolving the tenant's secret — the very round-trip this
/// cache exists to avoid. Physical connection reuse (the ADR's "keyed by cluster+db+user"
/// pool, §6.3) happens one layer down, inside Npgsql's own ADO.NET connection pool.
///
/// No TTL/invalidation yet — deferred until secret-rotation handling is designed.
/// </summary>
public sealed class TenantConnectionCache
{
    private readonly ConcurrentDictionary<Guid, TenantConnectionEntry> _cache = new();

    public bool TryGet(Guid tenantId, out TenantConnectionEntry entry) => _cache.TryGetValue(tenantId, out entry!);

    public void Set(Guid tenantId, TenantConnectionEntry entry) => _cache[tenantId] = entry;

    public void Evict(Guid tenantId) => _cache.TryRemove(tenantId, out _);
}
