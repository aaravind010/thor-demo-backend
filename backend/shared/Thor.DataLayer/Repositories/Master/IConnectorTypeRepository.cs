using Thor.DataLayer.Models;

namespace Thor.DataLayer.Repositories;

public interface IConnectorTypeRepository : IRepository<ConnectorType>
{
    Task<IReadOnlyList<ConnectorType>> GetByIdsAsync(IReadOnlyList<short> ids, CancellationToken cancellationToken = default);
}
