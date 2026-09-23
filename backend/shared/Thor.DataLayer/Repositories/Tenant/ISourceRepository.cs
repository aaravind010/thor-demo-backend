using Thor.DataLayer.Models.Tenants;

namespace Thor.DataLayer.Repositories;

public interface ISourceRepository : IRepository<Source>
{
    Task<IReadOnlyList<Source>> GetByIdsAsync(HashSet<Guid> ids, CancellationToken cancellationToken = default);
}
