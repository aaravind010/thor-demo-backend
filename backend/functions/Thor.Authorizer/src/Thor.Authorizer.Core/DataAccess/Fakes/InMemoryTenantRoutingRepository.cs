namespace Thor.Authorizer.Core.DataAccess.Fakes;

public sealed class InMemoryTenantRoutingRepository : ITenantRoutingRepository
{
    private readonly Dictionary<string, TenantRoute> _routesBySubdomain;

    public InMemoryTenantRoutingRepository(IEnumerable<TenantRoute> seedRoutes)
    {
        _routesBySubdomain = seedRoutes.ToDictionary(r => r.Subdomain, StringComparer.OrdinalIgnoreCase);
    }

    public Task<TenantRoute?> GetBySubdomainAsync(string subdomain) =>
        Task.FromResult(_routesBySubdomain.GetValueOrDefault(subdomain));
}
