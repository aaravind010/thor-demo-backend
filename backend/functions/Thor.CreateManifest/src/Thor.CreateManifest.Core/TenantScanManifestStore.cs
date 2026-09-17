using Thor.DataConnectionManager;
using Thor.DataLayer.Models.Tenants;
using Thor.DataLayer.Repositories;

namespace Thor.CreateManifest.Core;

/// <summary>
/// Opens the tenant's database (via <see cref="ITenantConnectionManager"/>, which caches/pools
/// validated connections internally) per call and delegates to <see cref="ScanManifestRepository"/>.
/// </summary>
public sealed class TenantScanManifestStore(ITenantConnectionManager tenantConnectionManager) : IScanManifestStore
{
    public async Task<IReadOnlyList<ScanManifest>> GetManifestsForScanAsync(Guid tenantId, Guid scanId, CancellationToken cancellationToken = default)
    {
        await using var db = await tenantConnectionManager.GetTenantDbContextAsync(tenantId, cancellationToken);
        return await new ScanManifestRepository(db).GetByScanAsync(scanId, cancellationToken);
    }

    public async Task<ScanManifest> CreateManifestAsync(Guid tenantId, Guid scanId, IReadOnlyList<string> fileLocations, CancellationToken cancellationToken = default)
    {
        await using var db = await tenantConnectionManager.GetTenantDbContextAsync(tenantId, cancellationToken);
        var repository = new ScanManifestRepository(db);
        var manifest = new ScanManifest
        {
            Id = Guid.NewGuid(),
            ScanId = scanId,
            FileLocations = fileLocations.ToArray(),
            Status = "pending",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };
        await repository.AddAsync(manifest, cancellationToken);
        await repository.SaveChangesAsync(cancellationToken);
        return manifest;
    }

    public async Task MarkProcessingAsync(Guid tenantId, Guid manifestId, CancellationToken cancellationToken = default)
    {
        await using var db = await tenantConnectionManager.GetTenantDbContextAsync(tenantId, cancellationToken);
        var repository = new ScanManifestRepository(db);
        var manifest = await repository.GetByIdAsync(manifestId, cancellationToken)
            ?? throw new InvalidOperationException($"No scan manifest found for ScanManifestId {manifestId}.");
        manifest.Status = "processing";
        manifest.UpdatedAt = DateTimeOffset.UtcNow;
        repository.Update(manifest);
        await repository.SaveChangesAsync(cancellationToken);
    }
}
