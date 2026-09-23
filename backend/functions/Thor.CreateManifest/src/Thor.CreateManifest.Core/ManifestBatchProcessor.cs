using Microsoft.Extensions.Logging;
using Thor.DataConnectionManager.Exceptions;
using Thor.DataConnectionManager.Routing;
using Thor.DataLayer.Models.Tenants;
using Thor.S3;

namespace Thor.CreateManifest.Core;

/// <summary>
/// Turns a batch of S3 upload events into <see cref="ScanManifest"/> rows, one per
/// <c>(TenantId, ScanId)</c> group, and triggers ingestion for each — skipping already-covered
/// keys and re-triggering manifests that were written but never processed.
/// </summary>
public sealed class ManifestBatchProcessor(
    IScanManifestStore manifestStore,
    ITenantRoutingResolver tenantRoutingResolver,
    IIngestionTrigger ingestionTrigger,
    ILogger<ManifestBatchProcessor> logger)
{
    public async Task ProcessBatchAsync(IReadOnlyList<UploadEvent> events, CancellationToken cancellationToken = default)
    {
        var parsed = new List<(Guid TenantId, Guid SourceId, Guid ScanId, string Bucket, string Key)>();
        foreach (var uploadEvent in events)
        {
            var result = UploadKeyParser.TryParse(uploadEvent.Key);
            if (result is not { } key)
            {
                logger.LogWarning("Skipping unparseable upload key '{Key}'", uploadEvent.Key);
                continue;
            }
            parsed.Add((key.TenantId, key.SourceId, key.ScanId, uploadEvent.Bucket, uploadEvent.Key));
        }

        foreach (var tenantGroup in parsed.GroupBy(p => p.TenantId))
        {
            var tenantId = tenantGroup.Key;
            try
            {
                // Fail closed: the tenant segment of the key is a hint, not authority (ADR §5.4)
                // — never trust it enough to touch that tenant's data without this check.
                await tenantRoutingResolver.ResolveAsync(tenantId, cancellationToken);
            }
            catch (TenantNotFoundException ex)
            {
                logger.LogWarning(ex, "Dropping {Count} record(s) — tenant {TenantId} has no routing entry in the Master DB", tenantGroup.Count(), tenantId);
                continue;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Dropping {Count} record(s) — tenant routing lookup failed unexpectedly for tenant {TenantId} ({ExceptionType}): {ExceptionMessage}", tenantGroup.Count(), tenantId, ex.GetType().Name, ex.Message);
                continue;
            }

            foreach (var scanGroup in tenantGroup.GroupBy(p => p.ScanId))
            {
                await ProcessScanGroupAsync(tenantId, scanGroup.Key, scanGroup.ToList(), cancellationToken);
            }
        }
    }

    private async Task ProcessScanGroupAsync(
        Guid tenantId,
        Guid scanId,
        IReadOnlyList<(Guid TenantId, Guid SourceId, Guid ScanId, string Bucket, string Key)> records,
        CancellationToken cancellationToken)
    {
        var batchKeys = records.Select(r => r.Key).Distinct().ToList();
        // Assumed: every file in one batch/scan shares a bucket (the ADR's "one shared uploads
        // bucket" model) — take the first rather than requiring exact agreement across records.
        var bucket = records[0].Bucket;

        var existingManifests = await manifestStore.GetManifestsForScanAsync(tenantId, scanId, cancellationToken);
        var alreadyCoveredKeys = existingManifests.SelectMany(m => m.FileLocations ?? []).ToHashSet();
        var newKeys = batchKeys.Where(k => !alreadyCoveredKeys.Contains(k)).ToList();

        if (newKeys.Count == 0)
        {
            var retryTarget = existingManifests.FirstOrDefault(m =>
                m.Status == "pending" && batchKeys.All(k => (m.FileLocations ?? []).Contains(k)));
            if (retryTarget is not null)
            {
                logger.LogInformation(
                    "Retrying trigger for scan manifest {ScanManifestId} (tenant {TenantId}, scan {ScanId}) — keys already recorded but never triggered",
                    retryTarget.Id, tenantId, scanId);
                await TriggerAndMarkProcessingAsync(tenantId, bucket, scanId, retryTarget.Id, cancellationToken);
            }
            else
            {
                logger.LogInformation(
                    "All {Count} key(s) for tenant {TenantId} scan {ScanId} already covered by a processed manifest — no-op",
                    batchKeys.Count, tenantId, scanId);
            }
            return;
        }

        var manifest = await manifestStore.CreateManifestAsync(tenantId, scanId, newKeys, cancellationToken);
        logger.LogInformation(
            "Created scan manifest {ScanManifestId} for tenant {TenantId} scan {ScanId} with {Count} file(s)",
            manifest.Id, tenantId, scanId, newKeys.Count);

        await TriggerAndMarkProcessingAsync(tenantId, bucket, scanId, manifest.Id, cancellationToken);
    }

    private async Task TriggerAndMarkProcessingAsync(
        Guid tenantId, string bucket, Guid scanId, Guid manifestId, CancellationToken cancellationToken)
    {
        await ingestionTrigger.TriggerAsync(
            new IngestionTriggerRequest(tenantId, $"s3://{bucket}", scanId, manifestId), cancellationToken);

        await manifestStore.MarkProcessingAsync(tenantId, manifestId, cancellationToken);
    }
}
