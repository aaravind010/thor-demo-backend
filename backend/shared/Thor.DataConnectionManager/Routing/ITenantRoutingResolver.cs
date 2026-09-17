using Thor.DataLayer.Models;

namespace Thor.DataConnectionManager.Routing;

/// <summary>
/// Resolves a tenant's routing row (cluster, database, secret reference) from the
/// Master metadata DB (see ADR §6.2).
/// </summary>
public interface ITenantRoutingResolver
{
    Task<TenantRouting> ResolveAsync(Guid tenantId, CancellationToken cancellationToken = default);
}
