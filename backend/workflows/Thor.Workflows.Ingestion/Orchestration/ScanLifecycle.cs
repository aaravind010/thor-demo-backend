using Thor.DataLayer.Repositories;

namespace Thor.Workflows.Ingestion.Orchestration;

/// <summary>
/// Scan lifecycle reads/transitions the ingestion pipeline needs — kept separate from
/// <see cref="Steps.PromoteStep"/>/<see cref="Steps.GraphLoadStartStep"/> so those concerns can
/// be tested and reasoned about on their own.
/// </summary>
public sealed class ScanLifecycle(IScanRepository scans, IScanManifestRepository scanManifests)
{
    /// <summary>Whether this scan is a first (full) load rather than an incremental (CDC) one — read directly from <c>Scan.ScanType</c>, not inferred.</summary>
    public async Task<bool> IsInitialScanAsync(Guid scanId)
    {
        var scan = await scans.GetByIdAsync(scanId)
            ?? throw new InvalidOperationException($"Scan {scanId} not found.");
        return scan.ScanType == "initial";
    }

    public async Task MarkManifestStatusAsync(Guid scanManifestId, string status)
    {
        var manifest = await scanManifests.GetByIdAsync(scanManifestId)
            ?? throw new InvalidOperationException($"ScanManifest {scanManifestId} not found.");
        manifest.Status = status;
        manifest.UpdatedAt = DateTimeOffset.UtcNow;
        scanManifests.Update(manifest);
        await scanManifests.SaveChangesAsync();
    }
}
