using System.Text.Json;
using Amazon.S3;
using Microsoft.Extensions.Logging;
using Thor.DataConnectionManager;
using Thor.DataLayer.Data;
using Thor.DataLayer.Repositories;
using Thor.S3;
using Thor.Workflows.Abstractions;
using Thor.Workflows.Ingestion.AttributeMapping;
using Thor.Workflows.Ingestion.Constants;
using Thor.Workflows.Ingestion.Composition;
using Thor.Workflows.Ingestion.Extraction.Sources;
using Thor.Workflows.Ingestion.Normalization;
using Thor.Workflows.Ingestion.Staging;

namespace Thor.Workflows.Ingestion.Steps;

public sealed record ExtractAndStageFileResult(string FileLocation, int AccountsStaged, int GroupsStaged, int AssetsStaged, int EntitlementsStaged);

public sealed record ListIngestionFilesResult(
    Guid TenantId, string ExportLocation, Guid ScanId, Guid ScanManifestId, Guid? RunId,
    int FileCount, IReadOnlyList<IngestionRequest> Files);

/// <summary>
/// One Lambda function/`THOR_STEP` value, two invocation shapes — both driving the ingestion
/// pipeline's Step Functions Distributed Map fan-out without a second deployed function:
///
/// <list type="bullet">
/// <item>Called once per manifest with <see cref="IngestionRequest.FileLocation"/> left
/// <c>null</c>: looks up the <see cref="Thor.DataLayer.Models.Tenants.ScanManifest"/> for the
/// given ScanManifestId, calls <see cref="WorkflowLifecycle.EnsureStartedAsync"/> once, and
/// returns a <see cref="ListIngestionFilesResult"/> with one <see cref="IngestionRequest"/> per
/// file in <c>FileLocations</c> — this is what a Step Functions Distributed Map's
/// <c>ItemsPath</c> fans out over, invoking this same Lambda function again for each item.</item>
/// <item>Called once per file (a Map item) with <c>FileLocation</c> set: extracts, normalizes,
/// and stages exactly that one file. Which connector's <see cref="IConnectorNormalizer"/> handles
/// it is resolved from the file's own upload key via <see cref="Thor.S3.UploadKeyParser"/> (a
/// manifest can span more than one connector even though it's always scoped to one tenant and one
/// scan) — the parsed <c>TenantId</c>/<c>ScanId</c> are validated against the request's own fields
/// as a data-integrity check.</item>
/// </list>
///
/// A thrown exception from the per-file path is intentionally left uncaught (no workflow-row
/// bookkeeping): the manifest-level workflow row is shared by every file being processed
/// concurrently, and the pipeline's partial-failure policy is "mark this file failed and continue
/// with the rest" — so one file's failure must never flip the whole manifest's status. That's
/// handled by the Step Functions ASL layer (Catch on the per-file Task state reshapes the error
/// into a non-throwing, tolerated result), not here. The list path keeps the usual
/// try/catch-then-MarkFailedAsync shape, since a failure there (e.g. manifest not found) is a
/// genuine whole-manifest failure.
///
/// Reads no state <see cref="PromoteStep"/> depends on other than the staging rows it writes.
/// </summary>
public sealed class ExtractAndStageStep : IWorkflowStep
{
    private readonly ILoggerFactory _loggerFactory;
    private readonly IExportSource _exportSource;
    private readonly ITenantConnectionManager _tenantConnectionManager;

    // Built once per Lambda container (or process): real file I/O in its ctor (loads every
    // attribute-map config file) and connector-agnostic (holds mappings for every connector type
    // already). LambdaEntry constructs this step once per container and reuses the same instance
    // across every warm invocation, and the per-file path below now runs once per *file* rather
    // than once per *manifest* — rebuilding this per call would re-read every connector's
    // attribute-map config off disk on every single file.
    private readonly AttributeMapProvider _attributeMapProvider = new();

    /// <summary>Test seam — lets tests substitute fakes for the S3/tenant-routing dependencies the parameterless constructor builds for real.</summary>
    public ExtractAndStageStep(ILoggerFactory loggerFactory, IExportSource exportSource, ITenantConnectionManager tenantConnectionManager)
    {
        _loggerFactory = loggerFactory;
        _exportSource = exportSource;
        _tenantConnectionManager = tenantConnectionManager;
    }

    public ExtractAndStageStep()
    {
        _loggerFactory = LoggerFactory.Create(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Information));
        _exportSource = new S3ExportSource(new S3ObjectStore(new AmazonS3Client()));
        _tenantConnectionManager = TenantConnectionManagerFactory.Build();
    }

    public async Task<(object? Result, bool IsInProgress)> ExecuteAsync(string inputJson, CancellationToken cancellationToken)
    {
        var request = JsonSerializer.Deserialize<IngestionRequest>(inputJson, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new InvalidOperationException($"{WorkflowHost.InputEnvVar} did not deserialize to a valid IngestionRequest.");

        await using var db = await _tenantConnectionManager.GetTenantDbContextAsync(request.TenantId, cancellationToken);

        if (request.FileLocation is null)
        {
            var listResult = await ListFilesAsync(db, request.TenantId, request.ExportLocation, request.ScanId, request.ScanManifestId, request.RunId, cancellationToken);
            return (listResult, false);
        }

        var fileResult = await RunAsync(
            db, request.TenantId, request.ScanId, request.ScanManifestId, request.ExportLocation,
            request.FileLocation, request.BatchSeq, cancellationToken);
        return (fileResult, false);
    }

    /// <summary>Fan-out point: lists a manifest's files as one <see cref="IngestionRequest"/> per file, and starts the manifest's workflow tracking row.</summary>
    public async Task<ListIngestionFilesResult> ListFilesAsync(
        TenantDbContext db, Guid tenantId, string exportLocation, Guid scanId, Guid scanManifestId, Guid? runId = null,
        CancellationToken cancellationToken = default)
    {
        var logger = _loggerFactory.CreateLogger<ExtractAndStageStep>();
        var workflowLifecycle = new WorkflowLifecycle(new WorkflowRepository(db));

        try
        {
            var manifest = await new ScanManifestRepository(db).GetByIdAsync(scanManifestId, cancellationToken)
                ?? throw new InvalidOperationException($"No scan manifest found for ScanManifestId {scanManifestId}.");

            // Only tracked once the manifest is confirmed real — scanManifestId is a foreign key
            // on the workflow row, so tracking a phantom manifest would fail the insert itself.
            await workflowLifecycle.EnsureStartedAsync(scanId, scanManifestId, WorkflowTypes.Ingestion, WorkflowTriggers.StepFunctions, runId, cancellationToken);

            var fileLocations = manifest.FileLocations ?? [];
            var files = fileLocations
                .Select((fileLocation, i) => new IngestionRequest(tenantId, exportLocation, scanId, scanManifestId, fileLocation, i, runId))
                .ToList();

            logger.LogInformation("Listed {FileCount} file(s) for scan manifest {ScanManifestId}", files.Count, scanManifestId);

            return new ListIngestionFilesResult(tenantId, exportLocation, scanId, scanManifestId, runId, files.Count, files);
        }
        catch (Exception ex)
        {
            await workflowLifecycle.MarkFailedAsync(scanManifestId, WorkflowTypes.Ingestion, ex.Message, runId, IngestionStatuses.ExtractAndStageFailed, cancellationToken);
            throw;
        }
    }

    /// <summary>Per-file unit of work: the Distributed Map item a Step Functions Map runs concurrently across every file <see cref="ListFilesAsync"/> listed for a manifest.</summary>
    public async Task<ExtractAndStageFileResult> RunAsync(
        TenantDbContext db, Guid tenantId, Guid scanId, Guid scanManifestId, string exportLocation,
        string fileLocation, int fileSeq, CancellationToken cancellationToken = default)
    {
        var logger = _loggerFactory.CreateLogger<ExtractAndStageStep>();
        var stager = new Stager(new StagingAccountRepository(db), new StagingGrpRepository(db), new StagingAssetRepository(db), new StagingEntitlementRepository(db), _loggerFactory.CreateLogger<Stager>());

        var bucket = S3Location.Parse(exportLocation).Bucket;

        var parsed = UploadKeyParser.TryParse(fileLocation)
            ?? throw new InvalidOperationException($"Could not parse tenant/source/scan from file location '{fileLocation}'.");
        if (parsed.TenantId != tenantId)
        {
            throw new InvalidOperationException($"File location '{fileLocation}' belongs to tenant {parsed.TenantId}, expected {tenantId}.");
        }
        if (parsed.ScanId != scanId)
        {
            throw new InvalidOperationException($"File location '{fileLocation}' belongs to scan {parsed.ScanId}, expected {scanId}.");
        }

        var source = await new SourceRepository(db).GetByIdAsync(parsed.SourceId, cancellationToken)
            ?? throw new InvalidOperationException($"No source found for SourceId {parsed.SourceId}.");
        var normalizer = ConnectorNormalizerFactory.Create(source.ConnectorType, _loggerFactory, _attributeMapProvider);

        var identifier = $"s3://{bucket}/{fileLocation}";
        var zipBytes = await _exportSource.ReadAsync(identifier, cancellationToken);
        var rawBytes = ZipExportReader.ReadFirstEntry(zipBytes);
        var batch = normalizer.Normalize(rawBytes, parsed.SourceId);
        if (batch.RepairedCount > 0)
        {
            logger.LogInformation("Recovered {RepairedCount} malformed/oddly-shaped record(s) from {Identifier}", batch.RepairedCount, identifier);
        }
        if (batch.SkippedCount > 0)
        {
            logger.LogWarning("Dropped {SkippedCount} unrecoverable record(s) from {Identifier}", batch.SkippedCount, identifier);
        }
        await stager.StageAsync(batch, tenantId, scanManifestId, fileSeq, cancellationToken);

        logger.LogInformation(
            "Extract+stage complete for file {FileLocation} (scan manifest {ScanManifestId}): {AccountsStaged} account(s)/{GroupsStaged} group(s)/{AssetsStaged} asset(s)/{EntitlementsStaged} entitlement(s) staged",
            fileLocation, scanManifestId, batch.Accounts.Count, batch.Groups.Count, batch.Assets.Count, batch.Entitlements.Count);

        return new ExtractAndStageFileResult(fileLocation, batch.Accounts.Count, batch.Groups.Count, batch.Assets.Count, batch.Entitlements.Count);
    }
}
