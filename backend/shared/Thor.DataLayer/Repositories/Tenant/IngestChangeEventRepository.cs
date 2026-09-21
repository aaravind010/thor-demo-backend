using Microsoft.EntityFrameworkCore;
using Thor.DataLayer.Data;
using Thor.DataLayer.Models.Tenants;

namespace Thor.DataLayer.Repositories;

public sealed class IngestChangeEventRepository(TenantDbContext context)
    : Repository<IngestChangeEvent>(context), IIngestChangeEventRepository
{
    public async Task<IReadOnlyList<IngestChangeEvent>> GetByScanAsync(
        Guid scanId, Guid? scanManifestId, IReadOnlyCollection<string>? entityTypes, CancellationToken cancellationToken = default)
    {
        var query = context.IngestChangeEvents.Where(e => e.ScanId == scanId);

        if (scanManifestId is { } manifestId)
        {
            query = query.Where(e => e.ScanManifestId == manifestId);
        }

        if (entityTypes is not null)
        {
            query = query.Where(e => entityTypes.Contains(e.EntityType));
        }

        return await query.ToListAsync(cancellationToken);
    }
}
