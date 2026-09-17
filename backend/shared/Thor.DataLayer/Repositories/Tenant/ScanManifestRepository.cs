using Microsoft.EntityFrameworkCore;
using Thor.DataLayer.Data;
using Thor.DataLayer.Models.Tenants;

namespace Thor.DataLayer.Repositories;

public sealed class ScanManifestRepository(TenantDbContext context)
    : Repository<ScanManifest>(context), IScanManifestRepository
{
    public async Task<IReadOnlyList<ScanManifest>> GetByScanAsync(Guid scanId, CancellationToken cancellationToken = default) =>
        await context.ScanManifests
            .Where(m => m.ScanId == scanId)
            .OrderByDescending(m => m.CreatedAt)
            .ToListAsync(cancellationToken);
}
