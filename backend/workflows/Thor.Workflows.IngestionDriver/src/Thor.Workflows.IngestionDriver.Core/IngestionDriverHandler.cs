using Thor.DataConnectionManager;
using Thor.DataLayer.Repositories;
using Thor.S3;

namespace Thor.Workflows.IngestionDriver.Core;

/// <summary>Loads the run's <c>ScanManifest</c> and delegates to <see cref="ComputeTargetSelector"/> for the compute decision.</summary>
public sealed class IngestionDriverHandler(ITenantConnectionManager tenantConnectionManager, ComputeTargetSelector selector)
{
    public async Task<ComputeSelectionResult> HandleAsync(IngestionDriverRequest request, CancellationToken cancellationToken = default)
    {
        await using var db = await tenantConnectionManager.GetTenantDbContextAsync(request.TenantId, cancellationToken);
        var manifest = await new ScanManifestRepository(db).GetByIdAsync(request.ScanManifestId, cancellationToken)
            ?? throw new InvalidOperationException($"No scan manifest found for ScanManifestId {request.ScanManifestId}.");

        var bucket = S3Location.Parse(request.ExportLocation).Bucket;
        return await selector.SelectAsync(bucket, manifest.FileLocations ?? [], cancellationToken);
    }
}
