using Thor.DataLayer.Models;

namespace Thor.DataLayer.Repositories;

public interface IConnectorConfigFieldRepository : IRepository<ConnectorConfigField>
{
    Task<IReadOnlyList<ConnectorConfigField>> GetByConnectorTypeAsync(short connectorType, CancellationToken cancellationToken = default);
}
