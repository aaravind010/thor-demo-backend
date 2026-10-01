using Thor.DataLayer.Models.Tenants;

namespace Thor.DataLayer.Repositories;

public interface IIdentityRepository : IRepository<IdentityRecord>
{
    /// <summary>Keyset-paged read, optionally filtered by active state; a null filter is not applied.</summary>
    Task<KeysetPage<IdentityRecord>> ListAsync(bool? isActive, Guid? after, int limit, CancellationToken cancellationToken = default);
}
