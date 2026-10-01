using Microsoft.Extensions.Logging;
using Thor.DataConnectionManager;
using Thor.DataLayer.Data;
using Thor.DataLayer.Models.Tenants;
using Thor.DataLayer.Repositories;
using Thor.Workflows.Abstractions;
using Thor.Workflows.Atre.Models;
using Thor.Workflows.Hosting;
using Thor.Workflows.Hosting.Composition;

namespace Thor.Workflows.Atre.Steps;

/// <summary>
/// Runs once, before any classification: opens the run's <c>workflow</c> row, links it to the
/// ingestion run that produced the manifest, and hands back the first wave.
///
/// <para>The workflow-row bookkeeping lives here rather than in <see cref="AtreClassifyStep"/>
/// precisely because the Map runs many classify invocations concurrently. If each one opened the
/// row, N racing get-or-creates would insert N rows — there is no unique index on
/// <c>(scan_manifest_id, workflow_type)</c> to collapse them — and none of them could tell whether
/// it was the last. Ingestion splits it the same way: its list invocation starts the row and its
/// per-file Map items do no bookkeeping at all.</para>
///
/// <para>It does not count the accounts in scope, and nothing else does either. How far a run has to
/// go is discovered by running: <see cref="AtreNextWaveStep"/> stops when a wave comes back short.
/// A <c>COUNT</c> over the manifest-scoped join would be a full pass over the same rows the run is
/// about to read anyway, to learn something only the last wave actually needs.</para>
/// </summary>
public sealed class AtreStartRunStep : WorkflowStep<AtreRequest>
{
    private readonly ILoggerFactory _loggerFactory;
    private readonly ITenantConnectionManager _tenantConnectionManager;
    private readonly int _accountsPerChunk;
    private readonly int _chunksPerWave;

    /// <summary>Test seam — lets tests substitute the tenant-connection dependency and the two dials the parameterless constructor resolves for real.</summary>
    public AtreStartRunStep(
        ILoggerFactory loggerFactory, ITenantConnectionManager tenantConnectionManager, int accountsPerChunk, int chunksPerWave)
    {
        _loggerFactory = loggerFactory;
        _tenantConnectionManager = tenantConnectionManager;
        _accountsPerChunk = accountsPerChunk;
        _chunksPerWave = chunksPerWave;
    }

    public AtreStartRunStep()
        : this(
            LoggerFactory.Create(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Information)),
            TenantConnectionManagerFactory.Build(),
            AtreWaves.AccountsPerChunkFromEnvironment(),
            AtreWaves.ChunksPerWaveFromEnvironment())
    {
    }

    protected override async Task<StepResult> ExecuteAsync(AtreRequest request, CancellationToken cancellationToken)
    {
        await using var db = await _tenantConnectionManager.GetTenantDbContextAsync(request.TenantId, cancellationToken);
        return StepResult.Completed(await RunAsync(db, request, cancellationToken));
    }

    public async Task<AtreWave> RunAsync(TenantDbContext db, AtreRequest request, CancellationToken cancellationToken = default)
    {
        var logger = _loggerFactory.CreateLogger<AtreStartRunStep>();
        var workflowRepository = new WorkflowRepository(db);
        var workflowLifecycle = new WorkflowLifecycle(workflowRepository);
        var trigger = request.ScanManifestId is not null ? WorkflowTriggers.StepFunctions : WorkflowTriggers.Api;

        var atreWorkflowId = await workflowLifecycle.EnsureStartedAsync(
            scanId: null, request.ScanManifestId, WorkflowTypes.Atre, trigger, request.RunId, cancellationToken);

        // A retry reuses this row: the run id is derived from (tenant, manifest), so a second
        // execution finds the first one's. EnsureStartedAsync returns it as-is, still carrying
        // whatever the last attempt recorded — so a clean retry would inherit the previous attempt's
        // lost windows and close completed_with_errors on the strength of them. ATRE opens its row
        // exactly once per execution, which is what makes resetting it here correct; a workflow that
        // records progress across several steps could not do this without erasing its own.
        await workflowRepository.ReopenAsync(atreWorkflowId, cancellationToken);

        if (request.ScanManifestId is not null)
        {
            await LinkToIngestionParentAsync(
                db, workflowRepository, request.ScanManifestId.Value, atreWorkflowId, logger, cancellationToken);
        }

        var wave = AtreWavePlanner.WaveAt(request, waveIndex: 0, _accountsPerChunk, _chunksPerWave);

        logger.LogInformation(
            "ATRE run started for manifest {ScanManifestId}: waves of {ChunksPerWave} x {AccountsPerChunk} accounts.",
            request.ScanManifestId, _chunksPerWave, _accountsPerChunk);

        return wave;
    }

    /// <summary>
    /// Links this ATRE run as a child of the ingestion run for the same scan manifest, via a
    /// <c>workflow_graph</c> row. Idempotent (skips if the edge already exists). If no ingestion row
    /// is found for the manifest, logs a warning and continues — lineage is observability, not
    /// correctness-critical for the classification run itself.
    /// </summary>
    private static async Task LinkToIngestionParentAsync(
        TenantDbContext db,
        WorkflowRepository workflowRepository,
        Guid scanManifestId,
        Guid atreWorkflowId,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        var siblings = await workflowRepository.GetByScanManifestIdAsync(scanManifestId, cancellationToken);
        var ingestionWorkflow = siblings.SingleOrDefault(w => w.WorkflowType == WorkflowTypes.Ingestion);
        if (ingestionWorkflow is null)
        {
            logger.LogWarning(
                "No ingestion workflow found for scan manifest {ScanManifestId}; skipping workflow_graph link for ATRE run {AtreWorkflowId}.",
                scanManifestId, atreWorkflowId);
            return;
        }

        var workflowGraphRepository = new WorkflowGraphRepository(db);
        if (await workflowGraphRepository.GetByIdAsync(ingestionWorkflow.Id, atreWorkflowId, cancellationToken) is not null)
        {
            return;
        }

        await workflowGraphRepository.AddAsync(
            new WorkflowGraph { ParentWorkflowId = ingestionWorkflow.Id, ChildWorkflowId = atreWorkflowId },
            cancellationToken);
        await workflowGraphRepository.SaveChangesAsync(cancellationToken);
    }
}
