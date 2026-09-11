namespace Thor.Authorizer.Core.DataAccess;

public interface ITenantRoutingRepository
{
    Task<TenantRoute?> GetBySubdomainAsync(string subdomain);
}
