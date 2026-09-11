using Microsoft.EntityFrameworkCore;
using Thor.DataLayer.Data;
using Thor.DataLayer.Models.Tenants;

namespace Thor.DataLayer.Repositories;

public sealed class WorkflowRepository(TenantDbContext context)
    : Repository<WorkflowEntity>(context), IWorkflowRepository
{
    public async Task<IReadOnlyList<WorkflowEntity>> GetByScanIdAsync(Guid scanId, CancellationToken cancellationToken = default) =>
        await context.Workflows.Where(w => w.ScanId == scanId).ToListAsync(cancellationToken);
}
