using System.Collections.Concurrent;
using Thor.Authorizer.Core.DataAccess;
using Thor.Authorizer.Core.Tenancy.Interface;

namespace Thor.Authorizer.Core.Tenancy;

public sealed class TenantRoutingCacheOptions
{
    public TimeSpan Ttl { get; init; } = TimeSpan.FromMinutes(5);
}

/// <summary>
/// Caches tenant routing lookups (including negative lookups) for the configured TTL.
/// Must be registered as a singleton in DI — a scoped/transient lifetime silently
/// defeats caching across warm Lambda invocations.
/// </summary>
public sealed class TenantRoutingCache : ITenantRoutingCache
{
    private readonly ITenantRoutingRepository _repository;
    private readonly TenantRoutingCacheOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly ConcurrentDictionary<string, CacheEntry> _entries = new(StringComparer.OrdinalIgnoreCase);

    public TenantRoutingCache(ITenantRoutingRepository repository, TenantRoutingCacheOptions options, TimeProvider timeProvider)
    {
        _repository = repository;
        _options = options;
        _timeProvider = timeProvider;
    }

    public async Task<TenantRoute?> GetOrAddAsync(string subdomain)
    {
        var now = _timeProvider.GetUtcNow();

        if (_entries.TryGetValue(subdomain, out var cached) && cached.ExpiresAt > now)
        {
            return cached.Route;
        }

        var route = await _repository.GetBySubdomainAsync(subdomain);
        _entries[subdomain] = new CacheEntry(route, now + _options.Ttl);
        return route;
    }

    private sealed record CacheEntry(TenantRoute? Route, DateTimeOffset ExpiresAt);
}
