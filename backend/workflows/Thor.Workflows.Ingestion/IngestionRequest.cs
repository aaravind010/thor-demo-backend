namespace Thor.Workflows.Ingestion;

/// <summary>
/// One ingestion run's identity and location, deserialized from the container's
/// <c>THOR_INPUT</c> environment variable (JSON), or from a Lambda invocation's raw event body.
/// <c>ExportLocation</c> is just the bucket (e.g. <c>s3://thor-uploads-dev</c>, no prefix) —
/// per-file paths come from <see cref="Thor.DataLayer.Models.Tenants.ScanManifest.FileLocations"/>.
/// No <c>SourceId</c>: <see cref="Steps.ExtractAndStageStep"/> resolves it per file (a manifest's
/// files can span more than one connector), since <c>Stager</c>/<c>Promoter</c> already carry
/// <c>SourceId</c> on the staged/promoted row data itself rather than needing one shared value
/// for the whole run. <c>RunId</c> is the optional attempt id workflow tracking keys on (see
/// <see cref="Thor.Workflows.Abstractions.WorkflowLifecycle"/>).
///
/// <c>FileLocation</c>/<c>BatchSeq</c> are used only by <see cref="Steps.ExtractAndStageStep"/>,
/// which is invoked twice per manifest, both times as the same Lambda function/`THOR_STEP`
/// value: once with <c>FileLocation</c> left <c>null</c> (list this manifest's files and emit one
/// <see cref="IngestionRequest"/> per file, each with <c>FileLocation</c>/<c>BatchSeq</c> set, for
/// a Step Functions Distributed Map to fan out over), and once per Map item with
/// <c>FileLocation</c> set (extract+normalize+stage that one file). Every other step ignores both
/// fields.
/// </summary>
public sealed record IngestionRequest(
    Guid TenantId, string ExportLocation, Guid ScanId, Guid ScanManifestId,
    string? FileLocation = null, int BatchSeq = 0, Guid? RunId = null);
