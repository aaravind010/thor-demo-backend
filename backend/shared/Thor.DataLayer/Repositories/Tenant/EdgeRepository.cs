using Microsoft.EntityFrameworkCore;
using Thor.DataLayer.Data;
using Thor.DataLayer.Models.Tenants;

namespace Thor.DataLayer.Repositories;

public sealed class EdgeRepository(TenantDbContext context)
    : Repository<Edge>(context), IEdgeRepository
{
    public async Task<IReadOnlyList<Edge>> GetByIdsAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken = default) =>
        await context.Edges.Where(e => ids.Contains(e.Id)).ToListAsync(cancellationToken);
}
