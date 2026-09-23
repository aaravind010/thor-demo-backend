using Microsoft.EntityFrameworkCore;
using Thor.DataLayer.Data;
using Thor.DataLayer.Models;

namespace Thor.DataLayer.Repositories;

public sealed class ConnectorTypeRepository(MasterDbContext context)
    : Repository<ConnectorType>(context), IConnectorTypeRepository
{
    public async Task<IReadOnlyList<ConnectorType>> GetByIdsAsync(IReadOnlyList<short> ids, CancellationToken cancellationToken = default) =>
        await context.ConnectorTypes
            .Where(c => ids.Contains(c.Id))
            .ToListAsync(cancellationToken);
}
