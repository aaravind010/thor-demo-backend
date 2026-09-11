using Thor.DataLayer.Models.Tenants;

namespace Thor.DataLayer.Repositories;

public interface IWorkflowGraphRepository : IRepository<WorkflowGraph>
{
    Task<WorkflowGraph?> GetByIdAsync(Guid parentWorkflowId, Guid childWorkflowId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<WorkflowGraph>> GetByParentWorkflowIdAsync(Guid parentWorkflowId, CancellationToken cancellationToken = default);
}
