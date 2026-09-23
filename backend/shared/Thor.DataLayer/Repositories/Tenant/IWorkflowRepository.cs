using Thor.DataLayer.Models.Tenants;

namespace Thor.DataLayer.Repositories;

public interface IWorkflowRepository : IRepository<WorkflowEntity>
{
    Task<IReadOnlyList<WorkflowEntity>> GetByScanIdAsync(Guid scanId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<WorkflowEntity>> GetByScanManifestIdAsync(Guid scanManifestId, CancellationToken cancellationToken = default);

    Task<WorkflowEntity?> GetByRunIdAsync(Guid runId, CancellationToken cancellationToken = default);
}
