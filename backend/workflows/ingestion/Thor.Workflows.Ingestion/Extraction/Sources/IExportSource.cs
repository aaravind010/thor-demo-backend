namespace Thor.Workflows.Ingestion.Extraction.Sources;

/// <summary>
/// Where a connector's raw zip export archives come from — separate from unzipping them
/// (<see cref="ZipExportReader"/>) or adapting their unzipped shape (<see cref="AdExtractor"/>).
/// Which files exist is driven entirely by <see cref="Thor.DataLayer.Models.Tenants.ScanManifest.FileLocations"/>
/// (each entry a file's full S3 object key) — there's no discovery/listing step, only reads.
/// </summary>
public interface IExportSource
{
    /// <summary>Reads one archive's raw zip bytes, given an identifier built from a <c>ScanManifest.FileLocations</c> entry.</summary>
    Task<byte[]> ReadAsync(string identifier, CancellationToken cancellationToken = default);
}
