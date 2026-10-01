namespace Thor.Authorizer.Core.DataAccess;

public interface ITenantRoutingRepository
{
    Task<TenantRoute?> GetByUserPoolIdAsync(string userPoolId);

    Task<TenantRoute?> GetByTenantIdAsync(string tenantId);
}
