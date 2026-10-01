using Thor.Authorizer.Core.DataAccess;

namespace Thor.Authorizer.Core.Tenancy.Interface;

public interface ITenantRoutingCache
{
    Task<TenantRoute?> GetByUserPoolIdAsync(string userPoolId);

    Task<TenantRoute?> GetByTenantIdAsync(string tenantId);
}
