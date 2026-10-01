using Microsoft.Extensions.Logging;
using Thor.DataConnectionManager;
using Thor.DataLayer.Data;
using Thor.DataLayer.Repositories;
using Thor.Workflows.Abstractions;
using Thor.Workflows.Atre.Constants;
using Thor.Workflows.Atre.Models;
using Thor.Workflows.Atre.Persistence;
using Thor.Workflows.Atre.Rules;
using Thor.Workflows.Atre.Streaming;
using Thor.Workflows.Atre.Voting;
using Thor.Workflows.Hosting.Composition;

namespace Thor.Workflows.Atre.Steps;

/// <summary>
/// The Distributed Map body: ensemble weighted voting over active <c>account_type_rule</c> rows for
/// one chunk of the accounts in scope. The chunk is a window — an offset and a limit — and this step
/// reads its own rows for it; a Map item cannot be handed ten thousand account ids, because Step
/// Functions caps a state's payload at 256 KB.
///
/// <para>Reads via one <see cref="TenantDbContext"/> (streamed, no-tracking) and writes via a second,
/// independent one — <see cref="ITenantConnectionManager.GetTenantDbContextAsync"/> opens a fresh
/// physical connection on every call, so this mirrors the POC's dedicated-read-connection design
/// (committing writes must never disturb the read side's streaming snapshot) without needing a new
/// abstraction. Two connections per invocation is also why the Map's concurrency is capped: see
/// <c>map_max_concurrency</c> in <c>infra/src/workflow_definitions.tf</c>.</para>
///
/// <para>Does no workflow-row bookkeeping, deliberately. The row is opened by
/// <see cref="AtreStartRunStep"/> and closed by <see cref="AtreFinalizeStep"/>, because many of these
/// run concurrently and none of them knows whether it is the last — ingestion's per-file Map body is
/// split the same way, and for the same reason. A throw here fails only this Map item: the state
/// machine catches it to <see cref="AtreRecordChunkFailureStep"/>, which writes this window down, and
/// the run carries on classifying the rest. Recording it there rather than here is what covers the
/// case a catch block cannot — this invocation being killed outright by the Lambda timeout.</para>
///
/// <para>Chunks do not coordinate and do not need to: a decision reads one account's own votes and
/// nothing else, and both of <see cref="AtreResultWriter"/>'s upserts are
/// <c>ON CONFLICT DO NOTHING</c> keyed per account. Disjoint chunks never contend, and even
/// overlapping ones converge.</para>
/// </summary>
public sealed class AtreClassifyStep : WorkflowStep<AtreChunkRequest>
{
    private const int FlushBatchSize = 2000;

    private readonly ILoggerFactory _loggerFactory;
    private readonly ITenantConnectionManager _tenantConnectionManager;

    /// <summary>Test seam — lets tests substitute a fake tenant-connection dependency the parameterless constructor builds for real.</summary>
    public AtreClassifyStep(ILoggerFactory loggerFactory, ITenantConnectionManager tenantConnectionManager)
    {
        _loggerFactory = loggerFactory;
        _tenantConnectionManager = tenantConnectionManager;
    }

    public AtreClassifyStep()
        : this(
            LoggerFactory.Create(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Information)),
            TenantConnectionManagerFactory.Build())
    {
    }

    protected override async Task<StepResult> ExecuteAsync(AtreChunkRequest request, CancellationToken cancellationToken)
    {
        try
        {
            await using var readContext = await _tenantConnectionManager.GetTenantDbContextAsync(request.TenantId, cancellationToken);
            await using var writeContext = await _tenantConnectionManager.GetTenantDbContextAsync(request.TenantId, cancellationToken);
            return StepResult.Completed(await RunAsync(readContext, writeContext, request, cancellationToken));
        }
        catch (Exception ex)
        {
            _loggerFactory.CreateLogger<AtreClassifyStep>().LogError(
                ex, "ATRE chunk [{Offset}, {End}) failed for tenant {TenantId}.",
                request.Offset, request.Offset + request.Limit, request.TenantId);
            throw;
        }
    }

    public async Task<AtreChunkSummary> RunAsync(
        TenantDbContext readContext, TenantDbContext writeContext, AtreChunkRequest request, CancellationToken cancellationToken)
    {
        var logger = _loggerFactory.CreateLogger<AtreClassifyStep>();
        var runId = AtreRunIdentity.Derive(request.TenantId, request.ScanManifestId, request.RunId, logger);

        var activeRules = await new AccountTypeRuleRepository(readContext)
            .GetActiveAsync(ScopeConstants.AccountEntityType, cancellationToken);
        var parsedRules = RuleFiringEngine.Parse(activeRules, logger);

        var writer = new AtreResultWriter(writeContext, runId, logger);
        var reader = new AccountStreamReader(readContext);

        var scanned = 0;
        var noFiringRule = 0;
        var newAssignments = 0;
        var flushes = 0;

        var accounts = reader.StreamAsync(request.ScanManifestId, request.Offset, request.Limit);
        await foreach (var account in accounts.WithCancellation(cancellationToken))
        {
            scanned++;

            var votes = RuleFiringEngine.Evaluate(parsedRules, account, logger);
            if (votes.Count == 0)
            {
                // Matches the POC exactly: zero firing rules writes no row anywhere, just this counter.
                noFiringRule++;
                continue;
            }

            writer.Stage(account, VoteAccumulator.Decide(votes), votes, DateTimeOffset.UtcNow);

            if (writer.PendingCount >= FlushBatchSize)
            {
                newAssignments += (await writer.FlushAsync(cancellationToken)).NewAssignments;
                flushes++;
            }
        }

        newAssignments += (await writer.FlushAsync(cancellationToken)).NewAssignments;
        flushes++;

        var summary = new AtreChunkSummary(
            RunId: runId,
            Offset: request.Offset,
            AccountsScanned: scanned,
            AccountsAssigned: scanned - noFiringRule,
            AccountsWithNoFiringRule: noFiringRule,
            NewAssignments: newAssignments,
            Flushes: flushes);

        logger.LogInformation(
            "ATRE run {RunId} chunk [{Offset}, {End}) complete for manifest {ScanManifestId}: {@Summary}",
            runId, request.Offset, request.Offset + request.Limit, request.ScanManifestId, summary);

        return summary;
    }
}
