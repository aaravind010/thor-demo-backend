using Thor.Authorizer.Core.DataAccess;

namespace Thor.Authorizer.Core.Tenancy.Interface;

public interface ITenantRoutingCache
{
    Task<TenantRoute?> GetOrAddAsync(string subdomain);
}
