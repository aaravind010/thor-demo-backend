namespace Thor.Workflows.Ingestion;

/// <summary>
/// One ingestion run's identity and location, deserialized from the container's
/// <c>THOR_INPUT</c> environment variable (JSON). <c>ExportLocation</c> is just the bucket (e.g.
/// <c>s3://thor-uploads-dev</c>, no prefix) — per-file paths come from
/// <see cref="Thor.DataLayer.Models.Tenants.ScanManifest.FileLocations"/>. No <c>SourceId</c>:
/// <see cref="Steps.ExtractAndStageStep"/> resolves it per file (a manifest's files can span more
/// than one connector), since <c>Stager</c>/<c>Promoter</c> already carry <c>SourceId</c> on the
/// staged/promoted row data itself rather than needing one shared value for the whole run.
/// </summary>
public sealed record IngestionRequest(
    Guid TenantId, string ExportLocation, Guid ScanId, Guid ScanManifestId, int BatchSeq = 0);
