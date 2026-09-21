namespace Thor.CreateManifest.Core;

/// <summary>
/// Payload for triggering an ingestion run. <c>ExportLocation</c> is just the bucket
/// (<c>s3://{bucket}</c>, no prefix) — extraction reads each file's path from <c>FileLocations</c>.
/// </summary>
public sealed record IngestionTriggerRequest(Guid TenantId, string ExportLocation, Guid ScanId, Guid ScanManifestId);

/// <summary>Starts the ingestion Step Functions execution for a newly-ready (or retried) manifest.</summary>
public interface IIngestionTrigger
{
    Task TriggerAsync(IngestionTriggerRequest request, CancellationToken cancellationToken = default);
}
