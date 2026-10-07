using Amazon.S3;
using Microsoft.Extensions.Logging;
using Thor.DataConnectionManager;
using Thor.DataLayer.Data;
using Thor.DataLayer.Models.Tenants;
using Thor.DataLayer.Repositories;
using Thor.S3;
using Thor.Workflows.Abstractions;
using Thor.Workflows.Hosting;
using Thor.Workflows.Hosting.Composition;
using Thor.Workflows.Ingestion.AttributeMapping;
using Thor.Workflows.Ingestion.Constants;
using Thor.Workflows.Ingestion.Extraction.Sources;
using Thor.Workflows.Ingestion.Models;
using Thor.Workflows.Ingestion.Normalization;
using Thor.Workflows.Ingestion.Staging;

namespace Thor.Workflows.Ingestion.Steps;

public sealed record ExtractAndStageFileResult(string FileLocation, int AccountsStaged, int GroupsStaged, int AssetsStaged, int EntitlementsStaged, int IdentitiesStaged);

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
/// A thrown exception from the per-file path never touches the workflow row: that row is
/// manifest-level, shared by every file being processed concurrently, and the pipeline's
/// partial-failure policy is "mark this file failed and continue with the rest" — so one file's
/// failure must never flip the whole manifest's status. Tolerating it is the Step Functions ASL
/// layer's job (Catch on the per-file Task state reshapes the error into a non-throwing, tolerated
/// result); recording *which* file failed and why is this step's, via the
/// <see cref="Thor.DataLayer.Models.Tenants.ScanFile"/> row it stamps before rethrowing. The list
/// path keeps the usual try/catch-then-MarkFailedAsync shape, since a failure there (e.g. manifest
/// not found) is a genuine whole-manifest failure.
///
/// Reads no state <see cref="PromoteStep"/> depends on other than the staging rows it writes.
/// </summary>
public sealed class ExtractAndStageStep : WorkflowStep<IngestionRequest>
{
    private readonly ILoggerFactory _loggerFactory;
    private readonly IExportSource _exportSource;
    private readonly ITenantConnectionManager _tenantConnectionManager;

    // Built once per Lambda container (or process): real file I/O in its ctor (loads every
    // attribute-map config file) and connector-agnostic (holds mappings for every connector type
    // already). LambdaHost constructs this step once per container and reuses the same instance
    // across every warm invocation, and the per-file path below now runs once per *file* rather
    // than once per *manifest* — rebuilding this per call would re-read every connector's
    // attribute-map config off disk on every single file.
    private readonly AttributeMapProvider _attributeMapProvider = new();

    // Same one-per-container lifetime as _attributeMapProvider, for the same reason: resolved
    // once from the Master DB's connector_types table rather than re-queried per file. Lazy so a
    // race across concurrent warm invocations never issues the query twice.
    private readonly Lazy<Task<ConnectorTypeCatalog>> _connectorTypes;

    /// <summary>Test seam — lets tests substitute fakes for the S3/tenant-routing/connector-type dependencies the parameterless constructor builds for real.</summary>
    public ExtractAndStageStep(ILoggerFactory loggerFactory, IExportSource exportSource, ITenantConnectionManager tenantConnectionManager, ConnectorTypeCatalog connectorTypes)
    {
        _loggerFactory = loggerFactory;
        _exportSource = exportSource;
        _tenantConnectionManager = tenantConnectionManager;
        _connectorTypes = new Lazy<Task<ConnectorTypeCatalog>>(() => Task.FromResult(connectorTypes));
    }

    public ExtractAndStageStep()
    {
        _loggerFactory = LoggerFactory.Create(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Information));
        _exportSource = new S3ExportSource(new S3ObjectStore(new AmazonS3Client()));
        _tenantConnectionManager = TenantConnectionManagerFactory.Build();

        var (masterDbContextFactory, masterConnectionInfo) = MasterConnectionManagerFactory.Build();
        _connectorTypes = new Lazy<Task<ConnectorTypeCatalog>>(() => LoadConnectorTypesAsync(masterDbContextFactory, masterConnectionInfo));
    }

    private static async Task<ConnectorTypeCatalog> LoadConnectorTypesAsync(IMasterDbContextFactory masterDbContextFactory, MasterConnectionInfo masterConnectionInfo)
    {
        using var masterDb = masterDbContextFactory.Create(masterConnectionInfo);
        return await ConnectorTypeCatalog.LoadAsync(new ConnectorTypeRepository(masterDb));
    }

    protected override async Task<StepResult> ExecuteAsync(IngestionRequest request, CancellationToken cancellationToken)
    {
        await using var db = await _tenantConnectionManager.GetTenantDbContextAsync(request.TenantId, cancellationToken);

        if (request.FileLocation is null)
        {
            var listResult = await ListFilesAsync(db, request.TenantId, request.ExportLocation, request.ScanId, request.ScanManifestId, request.RunId, cancellationToken);
            return StepResult.Completed(listResult);
        }

        var fileResult = await RunAsync(
            db, request.TenantId, request.ScanId, request.ScanManifestId, request.ExportLocation,
            request.FileLocation, request.BatchSeq, cancellationToken);
        return StepResult.Completed(fileResult);
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

            await RecordFilesReceivedAsync(db, scanId, fileLocations, logger, cancellationToken);

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

        // Parsed before the try so the failure path knows which task to attribute a failure to. A
        // key this step cannot parse has no task to attribute it to either way, so that throw stays
        // outside.
        var parsed = UploadKeyParser.TryParse(fileLocation)
            ?? throw new InvalidOperationException($"Could not parse tenant/source/scan from file location '{fileLocation}'.");

        try
        {
            var result = await ExtractAndStageFileAsync(
                db, parsed, tenantId, scanId, scanManifestId, exportLocation, fileLocation, fileSeq, logger, cancellationToken);

            await RecordFileStatusAsync(db, scanId, parsed.TaskId, fileLocation, ScanFileStatus.Ingested, error: null, logger, cancellationToken);
            return result;
        }
        catch (Exception ex)
        {
            // Recorded, then rethrown: the ASL Catch on this Map item still routes the error to the
            // DLQ and tolerates it, so the rest of the manifest carries on — but which file failed
            // and why is now a row rather than only a DLQ message.
            //
            // Its own try/catch because this runs on the failure path: a throw from here would
            // replace `ex` and lose the reason the file actually failed, trading the real cause for
            // a bookkeeping error. The original always propagates.
            try
            {
                await RecordFileStatusAsync(db, scanId, parsed.TaskId, fileLocation, ScanFileStatus.Failed, ex.Message, logger, cancellationToken);
            }
            catch (Exception recordEx)
            {
                logger.LogError(
                    recordEx, "Could not record file {FileLocation} as failed; the original failure follows.", fileLocation);
            }

            throw;
        }
    }

    private async Task<ExtractAndStageFileResult> ExtractAndStageFileAsync(
        TenantDbContext db, (Guid TenantId, Guid SourceId, Guid ScanId, Guid? TaskId) parsed,
        Guid tenantId, Guid scanId, Guid scanManifestId, string exportLocation, string fileLocation, int fileSeq,
        ILogger logger, CancellationToken cancellationToken)
    {
        var stager = new Stager(new StagingAccountRepository(db), new StagingGrpRepository(db), new StagingAssetRepository(db), new StagingEntitlementRepository(db), new StagingIdentityRepository(db), _loggerFactory.CreateLogger<Stager>());

        var bucket = S3Location.Parse(exportLocation).Bucket;

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
        var connectorTypes = await _connectorTypes.Value;
        var normalizer = ConnectorNormalizerFactory.Create(source.ConnectorType, _loggerFactory, _attributeMapProvider, connectorTypes);

        var identifier = $"s3://{bucket}/{fileLocation}";
        var zipBytes = await _exportSource.ReadAsync(identifier, cancellationToken);
        IEnumerable<IngestBatch> batches = normalizer is IStreamingConnectorNormalizer streaming
            ? streaming.NormalizeBatches(() => ZipExportReader.OpenFirstEntry(zipBytes), parsed.SourceId)
            : new[] { normalizer.Normalize(ZipExportReader.ReadFirstEntry(zipBytes), parsed.SourceId) };

        int accounts = 0, groups = 0, assets = 0, entitlements = 0, identities = 0, repaired = 0, skipped = 0;
        foreach (var batch in batches)
        {
            await stager.StageAsync(batch, tenantId, scanManifestId, fileSeq, cancellationToken);
            accounts += batch.Accounts.Count;
            groups += batch.Groups.Count;
            assets += batch.Assets.Count;
            entitlements += batch.Entitlements.Count;
            identities += batch.Identities.Count;
            repaired += batch.RepairedCount;
            skipped += batch.SkippedCount;
        }

        if (repaired > 0)
        {
            logger.LogInformation("Recovered {RepairedCount} malformed/oddly-shaped record(s) from {Identifier}", repaired, identifier);
        }
        if (skipped > 0)
        {
            logger.LogWarning("Dropped {SkippedCount} unrecoverable record(s) from {Identifier}", skipped, identifier);
        }

        logger.LogInformation(
            "Extract+stage complete for file {FileLocation} (scan manifest {ScanManifestId}): {AccountsStaged} account(s)/{GroupsStaged} group(s)/{AssetsStaged} asset(s)/{EntitlementsStaged} entitlement(s)/{IdentitiesStaged} identity(s) staged",
            fileLocation, scanManifestId, accounts, groups, assets, entitlements, identities);

        return new ExtractAndStageFileResult(fileLocation, accounts, groups, assets, entitlements, identities);
    }

    /// <summary>
    /// Seeds one <see cref="ScanFileStatus.Received"/> row per listed file, so a file that is never
    /// reached at all — the execution dies before its Map item runs — is still distinguishable from
    /// one that was never uploaded.
    /// </summary>
    private static async Task RecordFilesReceivedAsync(
        TenantDbContext db, Guid scanId, IReadOnlyList<string> fileLocations, ILogger logger, CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var rows = new List<ScanFile>(fileLocations.Count);

        foreach (var fileLocation in fileLocations)
        {
            if (UploadKeyParser.TryParse(fileLocation)?.TaskId is not { } taskId)
            {
                logger.LogWarning(
                    "No scan task id in file location '{FileLocation}' — not tracking its ingestion status.", fileLocation);
                continue;
            }

            rows.Add(new ScanFile
            {
                Id = Guid.NewGuid(),
                ScanId = scanId,
                ScanTaskId = taskId,
                FileLocation = fileLocation,
                Status = ScanFileStatus.Received,
                CreatedAt = now,
                UpdatedAt = now,
            });
        }

        await new ScanFileRepository(db).MarkReceivedAsync(rows, cancellationToken);
    }

    private static async Task RecordFileStatusAsync(
        TenantDbContext db, Guid scanId, Guid? scanTaskId, string fileLocation, string status, string? error,
        ILogger logger, CancellationToken cancellationToken)
    {
        if (scanTaskId is not { } taskId)
        {
            logger.LogWarning(
                "No scan task id in file location '{FileLocation}' — not recording status {Status}.", fileLocation, status);
            return;
        }

        var now = DateTimeOffset.UtcNow;
        await new ScanFileRepository(db).MarkStatusAsync(
            new ScanFile
            {
                Id = Guid.NewGuid(),
                ScanId = scanId,
                ScanTaskId = taskId,
                FileLocation = fileLocation,
                Status = status,
                Error = error,
                CreatedAt = now,
                UpdatedAt = now,
            },
            cancellationToken);
    }
}
