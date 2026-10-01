using Thor.Authorizer.Core.Tenancy.Interface;

namespace Thor.Authorizer.Core.Tenancy;

/// <summary>
/// Resolves a tenant's routing from an identifier taken from the caller's credential — never
/// from the request's Host or any other header (ADR §5). The identifier is only a lookup key: the
/// caller still verifies the credential against the route this returns.
/// </summary>
public sealed class TenantResolver
{
    private readonly ITenantRoutingCache _cache;

    public TenantResolver(ITenantRoutingCache cache) => _cache = cache;

    public async Task<TenantResolutionResult> ResolveByUserPoolIdAsync(string userPoolId)
    {
        var route = await _cache.GetByUserPoolIdAsync(userPoolId);
        return new TenantResolutionResult(route is not null, route);
    }

    public async Task<TenantResolutionResult> ResolveByTenantIdAsync(string tenantId)
    {
        var route = await _cache.GetByTenantIdAsync(tenantId);
        return new TenantResolutionResult(route is not null, route);
    }
}
