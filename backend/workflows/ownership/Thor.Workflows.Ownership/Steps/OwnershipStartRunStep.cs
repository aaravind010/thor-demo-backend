using Microsoft.Extensions.Logging;
using Thor.DataConnectionManager;
using Thor.DataLayer.Data;
using Thor.DataLayer.Models.Tenants;
using Thor.DataLayer.Repositories;
using Thor.Workflows.Abstractions;
using Thor.Workflows.Hosting;
using Thor.Workflows.Hosting.Composition;
using Thor.Workflows.Ownership.Models;
using Thor.Workflows.Ownership.Rules;

namespace Thor.Workflows.Ownership.Steps;

/// <summary>
/// Runs once, before any matching: validates the request, opens the run's <c>workflow</c> row, links
/// it to the ingestion run that produced the manifest, seeds the tenant's rule catalog, and hands back
/// the first wave.
///
/// <para>Everything here is once-per-run work that must not run once per Map item — the same split
/// as <c>AtreStartRunStep</c>, for the same reasons: concurrent get-or-creates would each insert a
/// workflow row, and no vote chunk knows whether it is the first. Seeding moved here from the vote
/// path for the second reason: it is idempotent, but there is no point running it hundreds of times
/// a run.</para>
/// </summary>
public sealed class OwnershipStartRunStep : WorkflowStep<OwnershipRequest>
{
    private readonly ILoggerFactory _loggerFactory;
    private readonly ITenantConnectionManager _tenantConnectionManager;
    private readonly int _entitiesPerChunk;
    private readonly int _chunksPerWave;

    /// <summary>Test seam — lets tests substitute the tenant-connection dependency and the two dials the parameterless constructor resolves for real.</summary>
    public OwnershipStartRunStep(
        ILoggerFactory loggerFactory, ITenantConnectionManager tenantConnectionManager, int entitiesPerChunk, int chunksPerWave)
    {
        _loggerFactory = loggerFactory;
        _tenantConnectionManager = tenantConnectionManager;
        _entitiesPerChunk = entitiesPerChunk;
        _chunksPerWave = chunksPerWave;
    }

    public OwnershipStartRunStep()
        : this(
            LoggerFactory.Create(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Information)),
            TenantConnectionManagerFactory.Build(),
            OwnershipWaves.EntitiesPerChunkFromEnvironment(),
            OwnershipWaves.ChunksPerWaveFromEnvironment())
    {
    }

    protected override async Task<StepResult> ExecuteAsync(OwnershipRequest request, CancellationToken cancellationToken)
    {
        await using var db = await _tenantConnectionManager.GetTenantDbContextAsync(request.TenantId, cancellationToken);
        return StepResult.Completed(await RunAsync(db, request, cancellationToken));
    }

    public async Task<OwnershipWave> RunAsync(TenantDbContext db, OwnershipRequest request, CancellationToken cancellationToken = default)
    {
        // Before the row is opened: a request no run can be started from fails with nothing to close.
        OwnershipScopes.Validate(request);

        var logger = _loggerFactory.CreateLogger<OwnershipStartRunStep>();
        var workflowRepository = new WorkflowRepository(db);
        var trigger = request.ScanManifestId is not null ? WorkflowTriggers.StepFunctions : WorkflowTriggers.Api;

        var workflowId = await new WorkflowLifecycle(workflowRepository).EnsureStartedAsync(
            scanId: null, request.ScanManifestId, WorkflowTypes.Ownership, trigger, request.RunId, cancellationToken);

        // A retry reuses this row, still carrying whatever the last attempt recorded — so a clean
        // retry would inherit its lost windows and close completed_with_errors on the strength of
        // them. Ownership opens its row exactly once per execution, which is what makes resetting
        // it here correct (see IWorkflowRepository.ReopenAsync).
        await workflowRepository.ReopenAsync(workflowId, cancellationToken);

        if (request.ScanManifestId is not null)
        {
            await LinkToIngestionParentAsync(
                db, workflowRepository, request.ScanManifestId.Value, workflowId, logger, cancellationToken);
        }

        await OwnershipRuleSeeder.EnsureSeededAsync(db, cancellationToken);

        var wave = OwnershipWavePlanner.FirstWave(request, _entitiesPerChunk, _chunksPerWave);

        logger.LogInformation(
            "Ownership run started for manifest {ScanManifestId} (scope {Scope}): waves of {ChunksPerWave} x {EntitiesPerChunk} entities.",
            request.ScanManifestId, OwnershipScopes.Resolve(request), _chunksPerWave, _entitiesPerChunk);

        return wave;
    }

    /// <summary>
    /// Links this run as a child of the ingestion run for the same scan manifest, via a
    /// <c>workflow_graph</c> row. Idempotent. A missing ingestion row is logged and skipped —
    /// lineage is observability, not something the run's own correctness depends on.
    /// </summary>
    private static async Task LinkToIngestionParentAsync(
        TenantDbContext db,
        WorkflowRepository workflowRepository,
        Guid scanManifestId,
        Guid workflowId,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        var siblings = await workflowRepository.GetByScanManifestIdAsync(scanManifestId, cancellationToken);
        var ingestionWorkflow = siblings.SingleOrDefault(w => w.WorkflowType == WorkflowTypes.Ingestion);
        if (ingestionWorkflow is null)
        {
            logger.LogWarning(
                "No ingestion workflow found for scan manifest {ScanManifestId}; skipping workflow_graph link for Ownership run {WorkflowId}.",
                scanManifestId, workflowId);
            return;
        }

        var workflowGraphRepository = new WorkflowGraphRepository(db);
        if (await workflowGraphRepository.GetByIdAsync(ingestionWorkflow.Id, workflowId, cancellationToken) is not null)
        {
            return;
        }

        await workflowGraphRepository.AddAsync(
            new WorkflowGraph { ParentWorkflowId = ingestionWorkflow.Id, ChildWorkflowId = workflowId },
            cancellationToken);
        await workflowGraphRepository.SaveChangesAsync(cancellationToken);
    }
}
