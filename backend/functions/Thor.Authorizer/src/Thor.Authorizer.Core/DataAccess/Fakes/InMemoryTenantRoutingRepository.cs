namespace Thor.Authorizer.Core.DataAccess.Fakes;

public sealed class InMemoryTenantRoutingRepository : ITenantRoutingRepository
{
    private readonly TenantRoute[] _routes;

    public InMemoryTenantRoutingRepository(IEnumerable<TenantRoute> seedRoutes)
    {
        _routes = seedRoutes.ToArray();
    }

    public Task<TenantRoute?> GetByUserPoolIdAsync(string userPoolId) =>
        Task.FromResult(_routes.SingleOrDefault(r => string.Equals(r.UserPoolId, userPoolId, StringComparison.Ordinal)));

    public Task<TenantRoute?> GetByTenantIdAsync(string tenantId) =>
        Task.FromResult(_routes.SingleOrDefault(r => string.Equals(r.TenantId, tenantId, StringComparison.Ordinal)));
}
