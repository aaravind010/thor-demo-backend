using Microsoft.EntityFrameworkCore;
using Thor.DataLayer.Data;
using Thor.DataLayer.Models.Tenants;

namespace Thor.DataLayer.Repositories;

public sealed class SourceRepository(TenantDbContext context)
    : Repository<Source>(context), ISourceRepository
{
    public async Task<IReadOnlyList<Source>> GetByIdsAsync(HashSet<Guid> ids, CancellationToken cancellationToken = default) =>
        await context.Sources
            .Where(s => ids.Contains(s.Id))
            .ToListAsync(cancellationToken);
}
