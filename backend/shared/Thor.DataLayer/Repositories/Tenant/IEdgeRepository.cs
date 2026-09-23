using Thor.DataLayer.Models.Tenants;

namespace Thor.DataLayer.Repositories;

public interface IEdgeRepository : IRepository<Edge>
{
    Task<IReadOnlyList<Edge>> GetByIdsAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken = default);
}
