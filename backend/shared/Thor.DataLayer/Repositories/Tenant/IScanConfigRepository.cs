using Thor.DataLayer.Models.Tenants;

namespace Thor.DataLayer.Repositories;

public interface IScanConfigRepository : IRepository<ScanConfig>
{
    Task<ScanConfig?> GetByIdWithSourcesAsync(Guid id, CancellationToken cancellationToken = default);
}
