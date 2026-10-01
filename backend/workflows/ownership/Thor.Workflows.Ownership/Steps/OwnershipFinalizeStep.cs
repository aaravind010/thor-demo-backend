using Microsoft.Extensions.Logging;
using Thor.DataConnectionManager;
using Thor.DataLayer.Data;
using Thor.DataLayer.Repositories;
using Thor.Workflows.Abstractions;
using Thor.Workflows.Hosting;
using Thor.Workflows.Hosting.Composition;
using Thor.Workflows.Ownership.Constants;
using Thor.Workflows.Ownership.Persistence;

namespace Thor.Workflows.Ownership.Steps;

/// <summary>
/// Runs once, after the graph load: closes the run's <c>workflow</c> row, and drops the walk rows the
/// run staged.
///
/// <para>Closes it <see cref="OwnershipStatuses.CompletedWithErrors"/> when any chunk was tolerated
/// on the way past — a non-empty <c>workflow.error</c> is the record of which windows, appended by
/// <see cref="OwnershipRecordChunkFailureStep"/>, the same arrangement as <c>AtreFinalizeStep</c>.</para>
///
/// <para>The staging goes here because this is the first point no vote chunk can read it. A run
/// that fails never reaches this step and keeps its rows, which is what lets a retry of the same run
/// id resume its walk rather than redo it.</para>
/// </summary>
public sealed class OwnershipFinalizeStep : WorkflowStep<OwnershipRequest>
{
    private readonly ILoggerFactory _loggerFactory;
    private readonly ITenantConnectionManager _tenantConnectionManager;

    /// <summary>Test seam — lets tests substitute the tenant-connection dependency the parameterless constructor builds for real.</summary>
    public OwnershipFinalizeStep(ILoggerFactory loggerFactory, ITenantConnectionManager tenantConnectionManager)
    {
        _loggerFactory = loggerFactory;
        _tenantConnectionManager = tenantConnectionManager;
    }

    public OwnershipFinalizeStep()
        : this(
            LoggerFactory.Create(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Information)),
            TenantConnectionManagerFactory.Build())
    {
    }

    protected override async Task<StepResult> ExecuteAsync(OwnershipRequest request, CancellationToken cancellationToken)
    {
        await using var db = await _tenantConnectionManager.GetTenantDbContextAsync(request.TenantId, cancellationToken);
        await RunAsync(db, request, cancellationToken);
        return StepResult.None;
    }

    public async Task RunAsync(TenantDbContext db, OwnershipRequest request, CancellationToken cancellationToken = default)
    {
        var logger = _loggerFactory.CreateLogger<OwnershipFinalizeStep>();
        var repository = new WorkflowRepository(db);

        var workflow = request.RunId is { } explicitRunId
            ? await repository.GetByRunIdAsync(explicitRunId, cancellationToken)
            : (await repository.GetByScanManifestIdAsync(request.ScanManifestId!.Value, cancellationToken))
                .SingleOrDefault(w => w.WorkflowType == WorkflowTypes.Ownership);

        var lostChunks = !string.IsNullOrEmpty(workflow?.Error);
        var status = lostChunks ? OwnershipStatuses.CompletedWithErrors : "completed";

        await new WorkflowLifecycle(repository)
            .MarkCompletedAsync(request.ScanManifestId, WorkflowTypes.Ownership, request.RunId, status, cancellationToken);

        var runId = OwnershipRunIdentity.Derive(request.TenantId, request.ScanManifestId, request.RunId, logger);
        var staged = await new OwnershipWalkCandidateRepository(db).DeleteRunAsync(runId.ToString(), cancellationToken);

        if (lostChunks)
        {
            logger.LogError(
                "Ownership run {RunId} finished for manifest {ScanManifestId} with unmatched chunks: {Error}",
                runId, request.ScanManifestId, workflow!.Error);
        }
        else
        {
            logger.LogInformation(
                "Ownership run {RunId} complete for manifest {ScanManifestId}; dropped {Staged} walk row(s).",
                runId, request.ScanManifestId, staged);
        }
    }
}
