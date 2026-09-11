using Thor.DataLayer.Data;
using Thor.DataLayer.Models;

namespace Thor.DataLayer.Repositories;

public sealed class ConnectorConfigFieldRepository(MasterDbContext context)
    : Repository<ConnectorConfigField>(context), IConnectorConfigFieldRepository
{
}
