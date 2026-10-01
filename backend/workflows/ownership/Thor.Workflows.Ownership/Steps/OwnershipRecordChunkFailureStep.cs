using Microsoft.Extensions.Logging;
using Thor.DataConnectionManager;
using Thor.DataLayer.Data;
using Thor.DataLayer.Repositories;
using Thor.Workflows.Abstractions;
using Thor.Workflows.Hosting.Composition;
using Thor.Workflows.Ownership.Models;

namespace Thor.Workflows.Ownership.Steps;

/// <summary>
/// Records one chunk's failure against the run, on the Map item's Catch path; the run then carries
/// on. The window is written down the moment it is skipped, so the run can end
/// <see cref="Constants.OwnershipStatuses.CompletedWithErrors"/> with every lost window listed in
/// <c>workflow.error</c> — the same trade <c>AtreRecordChunkFailureStep</c> makes.
///
/// <para>A single-statement append (<see cref="IWorkflowRepository.AppendErrorAsync"/>) because a
/// wave's chunks fail concurrently, and it leaves <c>status</c> alone: the run is still in flight, and
/// <see cref="OwnershipFinalizeStep"/> reads the error column to decide which terminal status it
/// earned.</para>
/// </summary>
public sealed class OwnershipRecordChunkFailureStep : WorkflowStep<OwnershipChunkFailureRequest>
{
    private const int MaxCauseLength = 500;

    private readonly ILoggerFactory _loggerFactory;
    private readonly ITenantConnectionManager _tenantConnectionManager;

    /// <summary>Test seam — lets tests substitute the tenant-connection dependency the parameterless constructor builds for real.</summary>
    public OwnershipRecordChunkFailureStep(ILoggerFactory loggerFactory, ITenantConnectionManager tenantConnectionManager)
    {
        _loggerFactory = loggerFactory;
        _tenantConnectionManager = tenantConnectionManager;
    }

    public OwnershipRecordChunkFailureStep()
        : this(
            LoggerFactory.Create(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Information)),
            TenantConnectionManagerFactory.Build())
    {
    }

    protected override async Task<StepResult> ExecuteAsync(OwnershipChunkFailureRequest request, CancellationToken cancellationToken)
    {
        await using var db = await _tenantConnectionManager.GetTenantDbContextAsync(request.TenantId, cancellationToken);
        await RunAsync(db, request, cancellationToken);
        return StepResult.None;
    }

    public async Task RunAsync(TenantDbContext db, OwnershipChunkFailureRequest request, CancellationToken cancellationToken = default)
    {
        var logger = _loggerFactory.CreateLogger<OwnershipRecordChunkFailureStep>();
        var end = request.Offset + request.Limit;
        var message = $"Chunk {request.EntityType} [{request.Offset}, {end}) was not matched: {Describe(request.Error)}";

        var repository = new WorkflowRepository(db);
        var workflow = request.RunId is { } runId
            ? await repository.GetByRunIdAsync(runId, cancellationToken)
            : (await repository.GetByScanManifestIdAsync(request.ScanManifestId!.Value, cancellationToken))
                .SingleOrDefault(w => w.WorkflowType == WorkflowTypes.Ownership);

        if (workflow is null)
        {
            // Nothing to append to. Loud, because the window is now lost with no record of it.
            logger.LogError(
                "No Ownership workflow row for manifest {ScanManifestId} / run {RunId}; {Message}",
                request.ScanManifestId, request.RunId, message);
            return;
        }

        await repository.AppendErrorAsync(workflow.Id, message, cancellationToken);

        logger.LogError("Ownership run {WorkflowId}: {Message}", workflow.Id, message);
    }

    private static string Describe(OwnershipFailureCause? cause)
    {
        if (cause is null)
        {
            return "no error detail was supplied";
        }

        var text = $"{cause.Error ?? "Unknown"}: {cause.Cause ?? "(no cause)"}";
        return text.Length <= MaxCauseLength ? text : text[..MaxCauseLength];
    }
}
