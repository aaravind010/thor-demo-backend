using Thor.DataLayer.Models.Tenants;

namespace Thor.DataLayer.Repositories;

public interface IGrpRepository : IRepository<Grp>
{
    Task<IReadOnlyList<Grp>> GetByIdsAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken = default);
}
