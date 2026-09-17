using Thor.DataLayer.Models.Tenants;

namespace Thor.DataLayer.Repositories;

public interface IScanTaskRepository : IRepository<ScanTask>
{
    Task<IReadOnlyList<ScanTask>> GetByScanIdAsync(Guid scanId, CancellationToken cancellationToken = default);
}
