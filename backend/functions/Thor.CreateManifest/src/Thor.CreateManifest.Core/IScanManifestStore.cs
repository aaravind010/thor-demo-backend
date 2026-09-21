using Thor.DataLayer.Models.Tenants;

namespace Thor.CreateManifest.Core;

/// <summary>Tenant-scoped access to <see cref="ScanManifest"/> rows.</summary>
public interface IScanManifestStore
{
    Task<IReadOnlyList<ScanManifest>> GetManifestsForScanAsync(Guid tenantId, Guid scanId, CancellationToken cancellationToken = default);

    Task<ScanManifest> CreateManifestAsync(Guid tenantId, Guid scanId, IReadOnlyList<string> fileLocations, CancellationToken cancellationToken = default);

    Task MarkProcessingAsync(Guid tenantId, Guid manifestId, CancellationToken cancellationToken = default);
}
