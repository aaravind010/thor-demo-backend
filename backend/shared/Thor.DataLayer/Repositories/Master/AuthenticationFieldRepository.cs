using Microsoft.EntityFrameworkCore;
using Thor.DataLayer.Data;
using Thor.DataLayer.Models;

namespace Thor.DataLayer.Repositories;

public sealed class AuthenticationFieldRepository(MasterDbContext context)
    : Repository<AuthenticationField>(context), IAuthenticationFieldRepository
{
    public async Task<IReadOnlyList<AuthenticationField>> GetByTypeIdAsync(Guid typeId, CancellationToken cancellationToken = default) =>
        await context.AuthenticationFields
            .Where(f => f.TypeId == typeId)
            .ToListAsync(cancellationToken);
}
