using Microsoft.EntityFrameworkCore;
using Thor.DataLayer.Data;
using Thor.DataLayer.Models;

namespace Thor.DataLayer.Repositories;

public sealed class ConnectorConfigFieldRepository(MasterDbContext context)
    : Repository<ConnectorConfigField>(context), IConnectorConfigFieldRepository
{
    public async Task<IReadOnlyList<ConnectorConfigField>> GetByConnectorTypeAsync(short connectorType, CancellationToken cancellationToken = default) =>
        await context.ConnectorConfigFields
            .Where(f => f.ConnectorTypeId == connectorType)
            .ToListAsync(cancellationToken);
}
