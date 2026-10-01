using Thor.DataLayer.Models.Tenants;

namespace Thor.DataLayer.Repositories;

public interface IGrpRepository : IRepository<Grp>
{
    Task<IReadOnlyList<Grp>> GetByIdsAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken = default);

    /// <summary>Keyset-paged read, optionally filtered by source; a null filter is not applied.</summary>
    Task<KeysetPage<Grp>> ListAsync(Guid? sourceId, Guid? after, int limit, CancellationToken cancellationToken = default);
}
