using Thor.DataLayer.Models;

namespace Thor.DataLayer.Repositories;

public interface ITenantRepository : IRepository<Tenant>
{
    /// <summary>
    /// Looks up a tenant by its (unique) subdomain, or null if none exists. Used by
    /// tenant provisioning to make the seed step idempotent — a re-run resolves the
    /// existing tenant instead of inserting a duplicate.
    /// </summary>
    Task<Tenant?> GetBySubdomainAsync(string subdomain, CancellationToken cancellationToken = default);
}
