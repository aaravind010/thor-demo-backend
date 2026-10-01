using Microsoft.Extensions.Logging;
using Thor.DataConnectionManager;
using Thor.DataLayer.Data;
using Thor.DataLayer.Repositories;
using Thor.Workflows.Abstractions;
using Thor.Workflows.Atre.Models;
using Thor.Workflows.Hosting;
using Thor.Workflows.Hosting.Composition;

namespace Thor.Workflows.Atre.Steps;

/// <summary>
/// Closes the run's <c>workflow</c> row as failed. Sits on the definition's Catch path, between the
/// failing state and <c>SendToDeadLetterQueue</c>.
///
/// <para>A step marking its own failure from inside a <c>try</c>/<c>catch</c> — the shape every
/// ingestion step uses — cannot cover ATRE, because the failure ATRE is most likely to hit is a chunk
/// exceeding the Lambda timeout, and a killed invocation runs no catch block. The same goes for an
/// out-of-memory kill and for a Map item whose retries are exhausted. Only the state machine sees all
/// of those, so the row is closed from the Catch path instead of from four separate steps. It also
/// means <see cref="AtreNextWaveStep"/> stays pure arithmetic with no database of its own.</para>
///
/// <para>This is the run dying, not a chunk failing. A chunk is tolerated and recorded by
/// <see cref="AtreRecordChunkFailureStep"/>, and the run carries on without it.</para>
///
/// <para>Failure here must not swallow the failure, so the definition catches this state straight to
/// <c>WorkflowFailed</c>, discarding this error to keep the original one in the execution history. A
/// run whose row could not be closed is worse observability, not lost work.</para>
/// </summary>
public sealed class AtreRecordFailureStep : WorkflowStep<AtreFailureRequest>
{
    private readonly ILoggerFactory _loggerFactory;
    private readonly ITenantConnectionManager _tenantConnectionManager;

    /// <summary>Test seam — lets tests substitute the tenant-connection dependency the parameterless constructor builds for real.</summary>
    public AtreRecordFailureStep(ILoggerFactory loggerFactory, ITenantConnectionManager tenantConnectionManager)
    {
        _loggerFactory = loggerFactory;
        _tenantConnectionManager = tenantConnectionManager;
    }

    public AtreRecordFailureStep()
        : this(
            LoggerFactory.Create(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Information)),
            TenantConnectionManagerFactory.Build())
    {
    }

    protected override async Task<StepResult> ExecuteAsync(AtreFailureRequest request, CancellationToken cancellationToken)
    {
        await using var db = await _tenantConnectionManager.GetTenantDbContextAsync(request.Request.TenantId, cancellationToken);
        await RunAsync(db, request, cancellationToken);
        return StepResult.None;
    }

    public async Task RunAsync(TenantDbContext db, AtreFailureRequest request, CancellationToken cancellationToken = default)
    {
        var run = request.Request;

        // MarkFailedAsync no-ops when it finds no row, which is the StartRun-failed-before-
        // EnsureStartedAsync case — there is genuinely nothing to close.
        await new WorkflowLifecycle(new WorkflowRepository(db))
            .MarkFailedAsync(run.ScanManifestId, WorkflowTypes.Atre, Describe(request.Error), run.RunId, cancellationToken: cancellationToken);

        _loggerFactory.CreateLogger<AtreRecordFailureStep>().LogError(
            "ATRE run failed for manifest {ScanManifestId}, run {RunId}: {Error}",
            run.ScanManifestId, run.RunId, Describe(request.Error));
    }

    /// <summary>
    /// Both halves of the Catch payload, because neither alone identifies a failure: the error name is
    /// often just <c>Exception</c>, and the cause is where the message and stack live.
    ///
    /// <para>Truncated by choice, not by the schema — <c>workflow.error</c> is <c>text</c>. A Lambda
    /// failure's <c>Cause</c> carries a serialized stack trace running to several KB, and this column
    /// is read to find out what broke, not as a log; the full trace is in the execution history and in
    /// CloudWatch.</para>
    /// </summary>
    private static string Describe(AtreFailureCause? cause)
    {
        if (cause is null)
        {
            return "Unknown failure: the state machine's Catch supplied no error.";
        }

        var text = $"{cause.Error ?? "Unknown"}: {cause.Cause ?? "(no cause)"}";
        return text.Length <= MaxErrorLength ? text : text[..MaxErrorLength];
    }

    private const int MaxErrorLength = 2000;
}
