using Microsoft.EntityFrameworkCore;
using Thor.DataLayer.Data;
using Thor.DataLayer.Models.Tenants;

namespace Thor.DataLayer.Repositories;

public sealed class ScanConfigRepository(TenantDbContext context)
    : Repository<ScanConfig>(context), IScanConfigRepository
{
    public async Task<ScanConfig?> GetByIdWithSourcesAsync(Guid id, CancellationToken cancellationToken = default) =>
        await context.ScanConfigs
            .Include(sc => sc.SourceMappings)
                .ThenInclude(m => m.Source)
            .FirstOrDefaultAsync(sc => sc.Id == id, cancellationToken);
}
