using System.Text.Json;
using Microsoft.Extensions.Logging;
using Thor.DataConnectionManager;
using Thor.DataLayer.Data;
using Thor.DataLayer.Repositories;
using Thor.Workflows.Abstractions;
using Thor.Workflows.Ingestion.Composition;
using Thor.Workflows.Ingestion.Constants;
using Thor.Workflows.Ingestion.EdgeGate;
using Thor.Workflows.Ingestion.Orchestration;
using Thor.Workflows.Ingestion.Promotion;

namespace Thor.Workflows.Ingestion.Steps;

public sealed record PromoteResult(
    int AccountsInserted, int AccountsUpdated,
    int GroupsInserted, int GroupsUpdated,
    int AssetsInserted, int AssetsUpdated,
    int EntitlementsInserted, int EntitlementsUpdated);

/// <summary>
/// CDC promotion: diffs staged rows against their canonical tables by content hash and
/// upserts the changed set (see <see cref="Promoter"/>). Reads only what
/// <see cref="ExtractAndStageStep"/> staged for the given manifest — no state passed between
/// the two beyond the manifest/scan/tenant ids already in <see cref="IngestionRequest"/>.
/// </summary>
public sealed class PromoteStep : IWorkflowStep
{
    private readonly ILoggerFactory _loggerFactory;
    private readonly ITenantConnectionManager _tenantConnectionManager;

    /// <summary>Test seam — lets tests substitute a fake tenant-routing dependency the parameterless constructor builds for real.</summary>
    public PromoteStep(ILoggerFactory loggerFactory, ITenantConnectionManager tenantConnectionManager)
    {
        _loggerFactory = loggerFactory;
        _tenantConnectionManager = tenantConnectionManager;
    }

    public PromoteStep()
    {
        _loggerFactory = LoggerFactory.Create(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Information));
        _tenantConnectionManager = TenantConnectionManagerFactory.Build();
    }

    public async Task<(object? Result, bool IsInProgress)> ExecuteAsync(string inputJson, CancellationToken cancellationToken)
    {
        var request = JsonSerializer.Deserialize<IngestionRequest>(inputJson, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new InvalidOperationException($"{WorkflowHost.InputEnvVar} did not deserialize to a valid IngestionRequest.");

        await using var db = await _tenantConnectionManager.GetTenantDbContextAsync(request.TenantId, cancellationToken);
        var result = await RunAsync(db, request.ScanManifestId, request.ScanId, request.RunId, cancellationToken);
        return (result, false);
    }

    public async Task<PromoteResult> RunAsync(TenantDbContext db, Guid scanManifestId, Guid scanId, Guid? runId = null, CancellationToken cancellationToken = default)
    {
        var logger = _loggerFactory.CreateLogger<PromoteStep>();
        var promoter = new Promoter(new EntityAliasRepository(db), _loggerFactory.CreateLogger<Promoter>());
        var edgeRefDeltaComputer = new EdgeRefDeltaComputer();
        var scanLifecycle = new ScanLifecycle(new ScanRepository(db), new ScanManifestRepository(db));

        var workflowLifecycle = new WorkflowLifecycle(new WorkflowRepository(db));
        await workflowLifecycle.EnsureStartedAsync(scanId, scanManifestId, WorkflowTypes.Ingestion, WorkflowTriggers.StepFunctions, runId, cancellationToken);

        try
        {
            // The Distributed Map's per-file extract-stage items don't do their own workflow-row
            // bookkeeping (see ExtractAndStageStep), so this is the first point in application
            // code that reliably knows extraction/staging finished for the manifest — the ASL
            // Map itself already guarantees every file item completed before this step runs.
            await workflowLifecycle.MarkStatusAsync(scanManifestId, WorkflowTypes.Ingestion, IngestionStatuses.ExtractAndStageComplete, runId, cancellationToken);

            // Skipped on the initial scan: canonical is empty then, so every ref would look like an
            // "add", and EdgeResolver.ResolveFullAsync never reads edge_ref_delta anyway. Must run
            // before each Promoter call — it needs canonical's OLD row, one step ahead of the upsert
            // that overwrites it.
            var isInitial = await scanLifecycle.IsInitialScanAsync(scanId, cancellationToken);

            if (!isInitial)
            {
                await edgeRefDeltaComputer.ComputeAsync(db, scanManifestId, "tenant.account", "account", cancellationToken);
            }
            var accountResult = await promoter.PromoteAccountsAsync(db, scanManifestId, scanId, cancellationToken);
            if (!isInitial)
            {
                await edgeRefDeltaComputer.ComputeAsync(db, scanManifestId, "tenant.grp", "grp", cancellationToken);
            }
            var groupResult = await promoter.PromoteGroupsAsync(db, scanManifestId, scanId, cancellationToken);
            if (!isInitial)
            {
                await edgeRefDeltaComputer.ComputeAsync(db, scanManifestId, "tenant.asset", "asset", cancellationToken);
            }
            var assetResult = await promoter.PromoteAssetsAsync(db, scanManifestId, scanId, cancellationToken);
            if (!isInitial)
            {
                await edgeRefDeltaComputer.ComputeAsync(db, scanManifestId, "tenant.entitlement", "entitlement", cancellationToken);
            }
            var entitlementResult = await promoter.PromoteEntitlementsAsync(db, scanManifestId, scanId, cancellationToken);

            await workflowLifecycle.MarkStatusAsync(scanManifestId, WorkflowTypes.Ingestion, IngestionStatuses.PromoteComplete, runId, cancellationToken);

            logger.LogInformation(
                "Promote complete for scan manifest {ScanManifestId}: accounts +{AccountsInserted}/~{AccountsUpdated}, groups +{GroupsInserted}/~{GroupsUpdated}, assets +{AssetsInserted}/~{AssetsUpdated}, entitlements +{EntitlementsInserted}/~{EntitlementsUpdated}",
                scanManifestId, accountResult.Inserted, accountResult.Updated, groupResult.Inserted, groupResult.Updated,
                assetResult.Inserted, assetResult.Updated, entitlementResult.Inserted, entitlementResult.Updated);

            return new PromoteResult(
                accountResult.Inserted, accountResult.Updated,
                groupResult.Inserted, groupResult.Updated,
                assetResult.Inserted, assetResult.Updated,
                entitlementResult.Inserted, entitlementResult.Updated);
        }
        catch (Exception ex)
        {
            await workflowLifecycle.MarkFailedAsync(scanManifestId, WorkflowTypes.Ingestion, ex.Message, runId, IngestionStatuses.PromoteFailed, cancellationToken);
            throw;
        }
    }
}
