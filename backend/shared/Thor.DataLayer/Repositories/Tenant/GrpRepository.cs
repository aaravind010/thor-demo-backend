using Microsoft.EntityFrameworkCore;
using Thor.DataLayer.Data;
using Thor.DataLayer.Models.Tenants;

namespace Thor.DataLayer.Repositories;

public sealed class GrpRepository(TenantDbContext context)
    : Repository<Grp>(context), IGrpRepository
{
    public async Task<IReadOnlyList<Grp>> GetByIdsAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken = default) =>
        await context.Grps.Where(g => ids.Contains(g.Id)).ToListAsync(cancellationToken);
}
