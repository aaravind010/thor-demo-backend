using System.Collections.Concurrent;
using Thor.Authorizer.Core.DataAccess;
using Thor.Authorizer.Core.Tenancy.Interface;

namespace Thor.Authorizer.Core.Tenancy;

public sealed class TenantRoutingCacheOptions
{
    public TimeSpan Ttl { get; init; } = TimeSpan.FromMinutes(5);
}

/// <summary>
/// Caches tenant routing lookups (including negative lookups) for the configured TTL, keyed
/// separately by user pool id and by tenant id. Keys compare ordinally: Cognito pool ids are
/// case-sensitive, so "us-east-1_AbC" and "us-east-1_abc" are different pools.
/// Must be registered as a singleton in DI — a scoped/transient lifetime silently
/// defeats caching across warm Lambda invocations.
/// </summary>
public sealed class TenantRoutingCache : ITenantRoutingCache
{
    private readonly ITenantRoutingRepository _repository;
    private readonly TenantRoutingCacheOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly ConcurrentDictionary<string, CacheEntry> _byUserPoolId = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, CacheEntry> _byTenantId = new(StringComparer.Ordinal);

    public TenantRoutingCache(ITenantRoutingRepository repository, TenantRoutingCacheOptions options, TimeProvider timeProvider)
    {
        _repository = repository;
        _options = options;
        _timeProvider = timeProvider;
    }

    public Task<TenantRoute?> GetByUserPoolIdAsync(string userPoolId) =>
        GetOrAddAsync(_byUserPoolId, userPoolId, _repository.GetByUserPoolIdAsync);

    public Task<TenantRoute?> GetByTenantIdAsync(string tenantId) =>
        GetOrAddAsync(_byTenantId, tenantId, _repository.GetByTenantIdAsync);

    private async Task<TenantRoute?> GetOrAddAsync(
        ConcurrentDictionary<string, CacheEntry> entries, string key, Func<string, Task<TenantRoute?>> load)
    {
        var now = _timeProvider.GetUtcNow();

        if (entries.TryGetValue(key, out var cached) && cached.ExpiresAt > now)
        {
            return cached.Route;
        }

        var route = await load(key);
        entries[key] = new CacheEntry(route, now + _options.Ttl);
        return route;
    }

    private sealed record CacheEntry(TenantRoute? Route, DateTimeOffset ExpiresAt);
}
