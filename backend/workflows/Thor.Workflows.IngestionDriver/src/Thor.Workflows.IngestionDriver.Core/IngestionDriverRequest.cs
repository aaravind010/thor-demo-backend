namespace Thor.Workflows.IngestionDriver.Core;

/// <summary>
/// Input to the ingestion driver, deserialized from the Step Functions state input. Field names
/// match <c>Thor.Workflows.Ingestion.IngestionRequest</c> so the same payload can flow unchanged
/// into both.
/// </summary>
public sealed record IngestionDriverRequest(Guid TenantId, string ExportLocation, Guid ScanId, Guid ScanManifestId);
