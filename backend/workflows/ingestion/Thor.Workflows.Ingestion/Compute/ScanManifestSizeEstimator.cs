using Thor.DataConnectionManager;
using Thor.DataLayer.Repositories;
using Thor.S3;
using Thor.Workflows.Abstractions.Compute;

namespace Thor.Workflows.Ingestion.Compute;

/// <summary>
/// Ingestion's answer to "how big is this run?": the total size of every file the run's
/// <c>ScanManifest</c> lists. This is the only ingestion-specific part of compute selection — the
/// threshold and the decision itself are <see cref="SizeThresholdPolicy"/>'s.
/// </summary>
public sealed class ScanManifestSizeEstimator(
    ITenantConnectionManager tenantConnectionManager,
    IS3ObjectStore s3ObjectStore) : IComputeEstimator<IngestionRequest>
{
    public async Task<ComputeEstimate> EstimateAsync(IngestionRequest request, CancellationToken cancellationToken = default)
    {
        await using var db = await tenantConnectionManager.GetTenantDbContextAsync(request.TenantId, cancellationToken);

        var manifest = await new ScanManifestRepository(db).GetByIdAsync(request.ScanManifestId, cancellationToken)
            ?? throw new InvalidOperationException($"No scan manifest found for ScanManifestId {request.ScanManifestId}.");

        // ExportLocation is the bucket only (see IngestionRequest); per-file keys come off the manifest.
        var bucket = S3Location.Parse(request.ExportLocation).Bucket;
        IReadOnlyList<string> fileLocations = manifest.FileLocations ?? [];

        long totalBytes = 0;
        foreach (var key in fileLocations)
        {
            totalBytes += await s3ObjectStore.GetObjectSizeAsync(bucket, key, cancellationToken);
        }

        return new ComputeEstimate(totalBytes, fileLocations.Count);
    }
}
