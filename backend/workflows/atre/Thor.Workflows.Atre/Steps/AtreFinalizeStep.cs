using Microsoft.Extensions.Logging;
using Thor.DataConnectionManager;
using Thor.DataLayer.Data;
using Thor.DataLayer.Repositories;
using Thor.Workflows.Abstractions;
using Thor.Workflows.Atre.Constants;
using Thor.Workflows.Hosting;
using Thor.Workflows.Hosting.Composition;

namespace Thor.Workflows.Atre.Steps;

/// <summary>
/// Runs once, after the last wave: closes the run's <c>workflow</c> row.
///
/// <para>A separate step for one call because there is nowhere else it can correctly go. A wave's
/// chunks run concurrently, so any of them marking the run complete would be marking it on behalf of
/// chunks still in flight; and no chunk knows whether its wave is the last one. Ingestion has no
/// equivalent step only because its last serial step (graph-load-poll) already had real work to do
/// and could close the row on its way out.</para>
///
/// <para>It does not aggregate the per-chunk <see cref="Models.AtreChunkSummary"/> objects. Each wave
/// hands its own back to <see cref="AtreNextWaveStep"/>, which uses them and drops them; keeping a
/// running total across every wave of a run would mean carrying it through the loop's state to
/// produce a number nothing consumes.</para>
///
/// <para>It does read one thing off the row: whether any chunk was tolerated on the way past. That is
/// why <see cref="AtreRecordChunkFailureStep"/> appends to <c>workflow.error</c> and leaves
/// <c>status</c> alone — a non-empty error column is how a run that reached the end without
/// classifying everything is told apart from one that did, and it is the only piece of per-chunk
/// state that survives the loop.</para>
/// </summary>
public sealed class AtreFinalizeStep : WorkflowStep<AtreRequest>
{
    private readonly ILoggerFactory _loggerFactory;
    private readonly ITenantConnectionManager _tenantConnectionManager;

    /// <summary>Test seam — lets tests substitute the tenant-connection dependency the parameterless constructor builds for real.</summary>
    public AtreFinalizeStep(ILoggerFactory loggerFactory, ITenantConnectionManager tenantConnectionManager)
    {
        _loggerFactory = loggerFactory;
        _tenantConnectionManager = tenantConnectionManager;
    }

    public AtreFinalizeStep()
        : this(
            LoggerFactory.Create(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Information)),
            TenantConnectionManagerFactory.Build())
    {
    }

    protected override async Task<StepResult> ExecuteAsync(AtreRequest request, CancellationToken cancellationToken)
    {
        await using var db = await _tenantConnectionManager.GetTenantDbContextAsync(request.TenantId, cancellationToken);
        await RunAsync(db, request, cancellationToken);
        return StepResult.None;
    }

    public async Task RunAsync(TenantDbContext db, AtreRequest request, CancellationToken cancellationToken = default)
    {
        var repository = new WorkflowRepository(db);

        // A non-empty error column means at least one chunk was tolerated on its way past, so the run
        // reached the end without classifying everything. The column is the record of which windows;
        // the status is what makes that visible without reading it.
        var workflow = request.RunId is { } runId
            ? await repository.GetByRunIdAsync(runId, cancellationToken)
            : (await repository.GetByScanManifestIdAsync(request.ScanManifestId!.Value, cancellationToken))
                .SingleOrDefault(w => w.WorkflowType == WorkflowTypes.Atre);

        var lostChunks = !string.IsNullOrEmpty(workflow?.Error);
        var status = lostChunks ? AtreStatuses.CompletedWithErrors : "completed";

        await new WorkflowLifecycle(repository)
            .MarkCompletedAsync(request.ScanManifestId, WorkflowTypes.Atre, request.RunId, status, cancellationToken);

        var logger = _loggerFactory.CreateLogger<AtreFinalizeStep>();
        if (lostChunks)
        {
            logger.LogError(
                "ATRE run finished for manifest {ScanManifestId} with unclassified chunks: {Error}",
                request.ScanManifestId, workflow!.Error);
        }
        else
        {
            logger.LogInformation("ATRE run complete for manifest {ScanManifestId}.", request.ScanManifestId);
        }
    }
}
