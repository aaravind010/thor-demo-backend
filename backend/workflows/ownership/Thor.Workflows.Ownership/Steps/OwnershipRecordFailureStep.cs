using Microsoft.Extensions.Logging;
using Thor.DataConnectionManager;
using Thor.DataLayer.Data;
using Thor.DataLayer.Repositories;
using Thor.Workflows.Abstractions;
using Thor.Workflows.Hosting;
using Thor.Workflows.Hosting.Composition;
using Thor.Workflows.Ownership.Models;

namespace Thor.Workflows.Ownership.Steps;

/// <summary>
/// Closes the run's <c>workflow</c> row as failed, from the definition's Catch path. There rather than
/// in each step's own <c>try</c>/<c>catch</c> for the reason <c>AtreRecordFailureStep</c> gives: a
/// Lambda killed by its timeout or by running out of memory runs no catch block, and only the state
/// machine sees every way a run can die.
///
/// <para>The definition catches this state straight to <c>WorkflowFailed</c>, so failing to record
/// the failure never swallows it.</para>
/// </summary>
public sealed class OwnershipRecordFailureStep : WorkflowStep<OwnershipFailureRequest>
{
    private const int MaxErrorLength = 2000;

    private readonly ILoggerFactory _loggerFactory;
    private readonly ITenantConnectionManager _tenantConnectionManager;

    /// <summary>Test seam — lets tests substitute the tenant-connection dependency the parameterless constructor builds for real.</summary>
    public OwnershipRecordFailureStep(ILoggerFactory loggerFactory, ITenantConnectionManager tenantConnectionManager)
    {
        _loggerFactory = loggerFactory;
        _tenantConnectionManager = tenantConnectionManager;
    }

    public OwnershipRecordFailureStep()
        : this(
            LoggerFactory.Create(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Information)),
            TenantConnectionManagerFactory.Build())
    {
    }

    protected override async Task<StepResult> ExecuteAsync(OwnershipFailureRequest request, CancellationToken cancellationToken)
    {
        await using var db = await _tenantConnectionManager.GetTenantDbContextAsync(request.Request.TenantId, cancellationToken);
        await RunAsync(db, request, cancellationToken);
        return StepResult.None;
    }

    public async Task RunAsync(TenantDbContext db, OwnershipFailureRequest request, CancellationToken cancellationToken = default)
    {
        var run = request.Request;
        var error = Describe(request.Error);

        // MarkFailedAsync no-ops when it finds no row — StartRun failed before opening one, and
        // there is genuinely nothing to close.
        await new WorkflowLifecycle(new WorkflowRepository(db))
            .MarkFailedAsync(run.ScanManifestId, WorkflowTypes.Ownership, error, run.RunId, cancellationToken: cancellationToken);

        _loggerFactory.CreateLogger<OwnershipRecordFailureStep>().LogError(
            "Ownership run failed for manifest {ScanManifestId}, run {RunId}: {Error}",
            run.ScanManifestId, run.RunId, error);
    }

    /// <summary>Both halves of the Catch payload, truncated: a Lambda failure's Cause carries a serialized stack trace, and the full one is in the execution history.</summary>
    private static string Describe(OwnershipFailureCause? cause)
    {
        if (cause is null)
        {
            return "Unknown failure: the state machine's Catch supplied no error.";
        }

        var text = $"{cause.Error ?? "Unknown"}: {cause.Cause ?? "(no cause)"}";
        return text.Length <= MaxErrorLength ? text : text[..MaxErrorLength];
    }
}
