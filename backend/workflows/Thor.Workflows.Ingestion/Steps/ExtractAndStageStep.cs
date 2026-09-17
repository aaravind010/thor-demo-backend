using System.Text.Json;
using Amazon.S3;
using Microsoft.Extensions.Logging;
using Thor.DataConnectionManager;
using Thor.DataLayer.Data;
using Thor.DataLayer.Models.Tenants;
using Thor.DataLayer.Repositories;
using Thor.S3;
using Thor.Workflows.Abstractions;
using Thor.Workflows.Ingestion.AttributeMapping;
using Thor.Workflows.Ingestion.Composition;
using Thor.Workflows.Ingestion.Extraction.Sources;
using Thor.Workflows.Ingestion.Normalization;
using Thor.Workflows.Ingestion.Staging;

namespace Thor.Workflows.Ingestion.Steps;

public sealed record ExtractAndStageResult(int FilesProcessed, int AccountsStaged, int GroupsStaged, int AssetsStaged, int EntitlementsStaged);

/// <summary>
/// Extract + normalize + stage: looks up the <see cref="ScanManifest"/> for the given
/// ScanManifestId and reads exactly the files it lists in
/// <see cref="ScanManifest.FileLocations"/> (each entry a full S3 key; <c>ExportLocation</c>
/// supplies only the bucket, e.g. <c>s3://thor-uploads-dev</c> — no discovery/listing happens),
/// bulk-loading the normalized rows into the transient staging tables. First of the ingestion
/// steps split out of the former monolithic pipeline — reads no state <see cref="PromoteStep"/>
/// depends on other than the staging rows it writes.
///
/// Which connector's <see cref="IConnectorNormalizer"/> handles a file is resolved **per file**,
/// not once for the whole run: <see cref="Thor.S3.UploadKeyParser"/> parses each file's own
/// <c>TenantId</c>/<c>SourceId</c>/<c>ScanId</c> out of its <see cref="ScanManifest.FileLocations"/>
/// entry, since a manifest can span more than one connector (a scan can run several sources at
/// once) even though it's always scoped to exactly one tenant and one scan — the parsed
/// <c>TenantId</c>/<c>ScanId</c> are validated against the run's own tenant and the loaded
/// manifest's <c>ScanId</c> as a data-integrity check. <c>SourceId</c> isn't carried on
/// <see cref="IngestionRequest"/> at all for this reason.
/// </summary>
public sealed class ExtractAndStageStep : IWorkflowStep
{
    private readonly ILoggerFactory _loggerFactory;
    private readonly IExportSource _exportSource;
    private readonly ITenantConnectionManager _tenantConnectionManager;

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

    public async Task ExecuteAsync(string inputJson, CancellationToken cancellationToken)
    {
        var request = JsonSerializer.Deserialize<IngestionRequest>(inputJson, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new InvalidOperationException($"{WorkflowHost.InputEnvVar} did not deserialize to a valid IngestionRequest.");

        await using var db = await _tenantConnectionManager.GetTenantDbContextAsync(request.TenantId);
        await RunAsync(db, request.TenantId, request.ScanManifestId, request.ExportLocation, request.BatchSeq);
    }

    public async Task<ExtractAndStageResult> RunAsync(
        TenantDbContext db, Guid tenantId, Guid scanManifestId, string exportLocation, int batchSeq)
    {
        var logger = _loggerFactory.CreateLogger<ExtractAndStageStep>();

        // Built once: real file I/O in its ctor (loads every attribute-map config file) and
        // connector-agnostic (holds mappings for every connector type already), so it must not be
        // rebuilt per file below.
        var attributeMapProvider = new AttributeMapProvider();
        var stager = new Stager(new StagingAccountRepository(db), new StagingGrpRepository(db), new StagingAssetRepository(db), new StagingEntitlementRepository(db), _loggerFactory.CreateLogger<Stager>());

        var bucket = S3Location.Parse(exportLocation).Bucket;
        var manifest = await new ScanManifestRepository(db).GetByIdAsync(scanManifestId)
            ?? throw new InvalidOperationException($"No scan manifest found for ScanManifestId {scanManifestId}.");
        var fileLocations = manifest.FileLocations ?? [];
        logger.LogInformation(
            "Extracting {FileCount} file(s) for scan manifest {ScanManifestId} from bucket {Bucket}",
            fileLocations.Length, scanManifestId, bucket);

        var totalAccounts = 0;
        var totalGroups = 0;
        var totalAssets = 0;
        var totalEntitlements = 0;
        for (var i = 0; i < fileLocations.Length; i++)
        {
            var fileLocation = fileLocations[i];
            var parsed = UploadKeyParser.TryParse(fileLocation)
                ?? throw new InvalidOperationException($"Could not parse tenant/source/scan from file location '{fileLocation}'.");
            if (parsed.TenantId != tenantId)
            {
                throw new InvalidOperationException($"File location '{fileLocation}' belongs to tenant {parsed.TenantId}, expected {tenantId}.");
            }
            if (parsed.ScanId != manifest.ScanId)
            {
                throw new InvalidOperationException($"File location '{fileLocation}' belongs to scan {parsed.ScanId}, expected {manifest.ScanId}.");
            }

            var source = await new SourceRepository(db).GetByIdAsync(parsed.SourceId)
                ?? throw new InvalidOperationException($"No source found for SourceId {parsed.SourceId}.");
            var normalizer = ConnectorNormalizerFactory.Create(source.ConnectorType, _loggerFactory, attributeMapProvider);

            var identifier = $"s3://{bucket}/{fileLocation}";
            var zipBytes = await _exportSource.ReadAsync(identifier);
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
            await stager.StageAsync(batch, tenantId, scanManifestId, batchSeq + i);
            totalAccounts += batch.Accounts.Count;
            totalGroups += batch.Groups.Count;
            totalAssets += batch.Assets.Count;
            totalEntitlements += batch.Entitlements.Count;
        }

        logger.LogInformation(
            "Extract+stage complete for scan manifest {ScanManifestId}: {FileCount} file(s), {AccountsStaged} account(s)/{GroupsStaged} group(s)/{AssetsStaged} asset(s)/{EntitlementsStaged} entitlement(s) staged",
            scanManifestId, fileLocations.Length, totalAccounts, totalGroups, totalAssets, totalEntitlements);

        return new ExtractAndStageResult(fileLocations.Length, totalAccounts, totalGroups, totalAssets, totalEntitlements);
    }
}
