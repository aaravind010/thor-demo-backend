using Microsoft.Extensions.Logging;
using Thor.DataConnectionManager;
using Thor.DataLayer.Data;
using Thor.DataLayer.Repositories;
using Thor.Workflows.Abstractions;
using Thor.Workflows.Atre.Models;
using Thor.Workflows.Hosting.Composition;

namespace Thor.Workflows.Atre.Steps;

/// <summary>
/// Records one chunk's failure against the run, on the Map item's Catch path. The run then carries
/// on: the other chunks' accounts still get classified, and later waves still go out.
///
/// <para>This is the trade the tolerating design makes. Failing the whole run on one bad chunk is
/// simple and loses nothing quietly; tolerating it classifies far more accounts but would lose track
/// of which ones were skipped — so the window has to be written down the moment it is skipped. The
/// run ends <see cref="Constants.AtreStatuses.CompletedWithErrors"/> with every lost window listed in
/// <c>workflow.error</c>, which is enough to re-run those offsets on their own.</para>
///
/// <para>The append is a single UPDATE rather than a read-then-write because a wave's chunks fail
/// concurrently — see <see cref="IWorkflowRepository.AppendErrorAsync"/>. It deliberately does not
/// touch <c>status</c>: the run is still in flight, and <see cref="AtreFinalizeStep"/> reads the
/// error column at the end to decide which terminal status the run earned.</para>
/// </summary>
public sealed class AtreRecordChunkFailureStep : WorkflowStep<AtreChunkFailureRequest>
{
    private readonly ILoggerFactory _loggerFactory;
    private readonly ITenantConnectionManager _tenantConnectionManager;

    /// <summary>Test seam — lets tests substitute the tenant-connection dependency the parameterless constructor builds for real.</summary>
    public AtreRecordChunkFailureStep(ILoggerFactory loggerFactory, ITenantConnectionManager tenantConnectionManager)
    {
        _loggerFactory = loggerFactory;
        _tenantConnectionManager = tenantConnectionManager;
    }

    public AtreRecordChunkFailureStep()
        : this(
            LoggerFactory.Create(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Information)),
            TenantConnectionManagerFactory.Build())
    {
    }

    protected override async Task<StepResult> ExecuteAsync(AtreChunkFailureRequest request, CancellationToken cancellationToken)
    {
        await using var db = await _tenantConnectionManager.GetTenantDbContextAsync(request.TenantId, cancellationToken);
        await RunAsync(db, request, cancellationToken);
        return StepResult.None;
    }

    public async Task RunAsync(TenantDbContext db, AtreChunkFailureRequest request, CancellationToken cancellationToken = default)
    {
        var logger = _loggerFactory.CreateLogger<AtreRecordChunkFailureStep>();
        var end = request.Offset + request.Limit;
        var message = $"Chunk [{request.Offset}, {end}) was not classified: {Describe(request.Error)}";

        var repository = new WorkflowRepository(db);
        var workflow = request.RunId is { } runId
            ? await repository.GetByRunIdAsync(runId, cancellationToken)
            : (await repository.GetByScanManifestIdAsync(request.ScanManifestId!.Value, cancellationToken))
                .SingleOrDefault(w => w.WorkflowType == WorkflowTypes.Atre);

        if (workflow is null)
        {
            // Nothing to append to. Loud, because the window is now lost with no record of it.
            logger.LogError(
                "No ATRE workflow row for manifest {ScanManifestId} / run {RunId}; {Message}",
                request.ScanManifestId, request.RunId, message);
            return;
        }

        await repository.AppendErrorAsync(workflow.Id, message, cancellationToken);

        logger.LogError("ATRE run {WorkflowId}: {Message}", workflow.Id, message);
    }

    /// <summary>Truncated for the same reason <see cref="AtreRecordFailureStep"/> truncates: a Cause carries a whole stack trace, and several of these can land on one run.</summary>
    private static string Describe(AtreFailureCause? cause)
    {
        if (cause is null)
        {
            return "no error detail was supplied";
        }

        var text = $"{cause.Error ?? "Unknown"}: {cause.Cause ?? "(no cause)"}";
        return text.Length <= MaxCauseLength ? text : text[..MaxCauseLength];
    }

    private const int MaxCauseLength = 500;
}
