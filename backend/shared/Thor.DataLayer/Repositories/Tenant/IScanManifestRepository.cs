using Thor.DataLayer.Models.Tenants;

namespace Thor.DataLayer.Repositories;

public interface IScanManifestRepository : IRepository<ScanManifest>
{
    /// <summary>Every manifest recorded for a scan, newest first.</summary>
    Task<IReadOnlyList<ScanManifest>> GetByScanAsync(Guid scanId, CancellationToken cancellationToken = default);
}
