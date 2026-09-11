using Microsoft.EntityFrameworkCore;
using Thor.DataLayer.Data;
using Thor.DataLayer.Models.Tenants;

namespace Thor.DataLayer.Repositories;

public sealed class ScanTaskRepository(TenantDbContext context)
    : Repository<ScanTask>(context), IScanTaskRepository
{
    public async Task<IReadOnlyList<ScanTask>> GetByScanIdAsync(Guid scanId, CancellationToken cancellationToken = default) =>
        await context.Tasks.Where(t => t.ScanId == scanId).ToListAsync(cancellationToken);
}
