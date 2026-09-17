using Thor.DataLayer.Models.Tenants;

namespace Thor.DataLayer.Repositories;

public interface IWorkflowRepository : IRepository<WorkflowEntity>
{
    Task<IReadOnlyList<WorkflowEntity>> GetByScanIdAsync(Guid scanId, CancellationToken cancellationToken = default);
}
