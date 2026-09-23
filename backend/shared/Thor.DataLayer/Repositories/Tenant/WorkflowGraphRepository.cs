using Microsoft.EntityFrameworkCore;
using Thor.DataLayer.Data;
using Thor.DataLayer.Models.Tenants;

namespace Thor.DataLayer.Repositories;

public sealed class WorkflowGraphRepository(TenantDbContext context)
    : Repository<WorkflowGraph>(context), IWorkflowGraphRepository
{
    public async Task<WorkflowGraph?> GetByIdAsync(Guid parentWorkflowId, Guid childWorkflowId, CancellationToken cancellationToken = default) =>
        await context.WorkflowGraphs.FindAsync([parentWorkflowId, childWorkflowId], cancellationToken);

    public async Task<IReadOnlyList<WorkflowGraph>> GetByParentWorkflowIdAsync(Guid parentWorkflowId, CancellationToken cancellationToken = default) =>
        await context.WorkflowGraphs.Where(g => g.ParentWorkflowId == parentWorkflowId).ToListAsync(cancellationToken);
}
