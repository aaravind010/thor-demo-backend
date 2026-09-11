using Thor.Authorizer.Core.Tenancy.Interface;

namespace Thor.Authorizer.Core.Tenancy;

public sealed class TenantResolver
{
    private readonly ITenantRoutingCache _cache;

    public TenantResolver(ITenantRoutingCache cache) => _cache = cache;

    public async Task<TenantResolutionResult> ResolveAsync(string? hostHeader)
    {
        var subdomain = HostParser.ExtractSubdomain(hostHeader);
        if (subdomain is null)
        {
            return new TenantResolutionResult(false, null);
        }

        var route = await _cache.GetOrAddAsync(subdomain);
        return new TenantResolutionResult(route is not null, route);
    }
}
