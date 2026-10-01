using Microsoft.EntityFrameworkCore;
using Thor.DataLayer.Data;
using Thor.DataLayer.Models;

namespace Thor.DataLayer.Repositories;

public sealed class AuthenticationTypeRepository(MasterDbContext context)
    : Repository<AuthenticationType>(context), IAuthenticationTypeRepository
{
    public async Task<IReadOnlyList<AuthenticationType>> GetByConnectorTypeAsync(short connectorType, CancellationToken cancellationToken = default) =>
        await context.AuthenticationTypes
            .Where(t => t.ConnectorTypes.Any(m => m.ConnectorTypeId == connectorType))
            .ToListAsync(cancellationToken);

    public Task<bool> SupportsConnectorTypeAsync(Guid typeId, short connectorType, CancellationToken cancellationToken = default) =>
        context.AuthenticationTypeConnectorTypes
            .AnyAsync(m => m.AuthenticationTypeId == typeId && m.ConnectorTypeId == connectorType, cancellationToken);
}
