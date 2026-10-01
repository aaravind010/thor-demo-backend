using Microsoft.Extensions.Logging;
using Thor.DataConnectionManager;
using Thor.DataLayer.Data;
using Thor.Workflows.Abstractions;
using Thor.Workflows.Hosting.Composition;
using Thor.Workflows.Ownership.Matching;
using Thor.Workflows.Ownership.Models;
using Thor.Workflows.Ownership.Persistence;
using Thor.Workflows.Ownership.Rules;

namespace Thor.Workflows.Ownership.Steps;

/// <summary>
/// The Distributed Map body: set-based candidate matching over the phase's active
/// <c>ownership_rule</c> rows for one window of that phase's in-scope entities, ranked and written as
/// votes and proposed assignments.
///
/// <para>Reads via one <see cref="TenantDbContext"/> (streamed, raw SQL) and writes via a second,
/// independent one, so committing writes never disturbs the read side's streaming snapshot — the
/// same two-connection design as <c>AtreClassifyStep</c>, and why the Map's concurrency is capped
/// (see <c>map_max_concurrency</c> in <c>infra/src/workflow_definitions.tf</c>).</para>
///
/// <para>Does no workflow-row bookkeeping: the row is opened by <see cref="OwnershipStartRunStep"/>
/// and closed by <see cref="OwnershipFinalizeStep"/>, because many of these run concurrently and none
/// knows whether it is the last. A throw fails only this Map item; the state machine records the lost
/// window through <see cref="OwnershipRecordChunkFailureStep"/> and the run carries on.</para>
///
/// <para>Chunks of a phase do not coordinate and need not: windows are disjoint and stable for the
/// whole run (see <see cref="OwnershipCandidateSqlBuilder"/>), rules see only owners from before the
/// phase began, and every write is <c>ON CONFLICT DO NOTHING</c> keyed on the run.</para>
/// </summary>
public sealed class OwnershipVoteStep : WorkflowStep<OwnershipChunkRequest>
{
    private const int FlushBatchSize = 2000;

    private readonly ILoggerFactory _loggerFactory;
    private readonly ITenantConnectionManager _tenantConnectionManager;

    /// <summary>Test seam — lets tests substitute a fake tenant-connection dependency the parameterless constructor builds for real.</summary>
    public OwnershipVoteStep(ILoggerFactory loggerFactory, ITenantConnectionManager tenantConnectionManager)
    {
        _loggerFactory = loggerFactory;
        _tenantConnectionManager = tenantConnectionManager;
    }

    public OwnershipVoteStep()
        : this(
            LoggerFactory.Create(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Information)),
            TenantConnectionManagerFactory.Build())
    {
    }

    protected override async Task<StepResult> ExecuteAsync(OwnershipChunkRequest request, CancellationToken cancellationToken)
    {
        try
        {
            await using var readContext = await _tenantConnectionManager.GetTenantDbContextAsync(request.TenantId, cancellationToken);
            await using var writeContext = await _tenantConnectionManager.GetTenantDbContextAsync(request.TenantId, cancellationToken);
            return StepResult.Completed(await RunAsync(readContext, writeContext, request, cancellationToken));
        }
        catch (Exception ex)
        {
            _loggerFactory.CreateLogger<OwnershipVoteStep>().LogError(
                ex, "Ownership chunk {EntityType} [{Offset}, {End}) failed for tenant {TenantId}.",
                request.EntityType, request.Offset, request.Offset + request.Limit, request.TenantId);
            throw;
        }
    }

    public async Task<OwnershipChunkSummary> RunAsync(
        TenantDbContext readContext, TenantDbContext writeContext, OwnershipChunkRequest request, CancellationToken cancellationToken)
    {
        var logger = _loggerFactory.CreateLogger<OwnershipVoteStep>();
        var runId = OwnershipRunIdentity.Derive(request.TenantId, request.ScanManifestId, request.RunId, logger);
        var runIdText = runId.ToString();

        var rules = await OwnershipRuleLoader.LoadActiveAsync(readContext, request.EntityType, logger, cancellationToken);
        if (rules.Count == 0)
        {
            // Nothing can match, so the window's size is irrelevant: reporting zero ends the phase
            // after this wave instead of paging through entities no rule will ever look at.
            logger.LogInformation("Ownership phase '{EntityType}' for run {RunId}: no active rules.", request.EntityType, runId);
            return new OwnershipChunkSummary(runId, request.EntityType, request.Offset, 0, 0, 0, 0, 0);
        }

        var inWindow = await OwnershipCandidateReader.CountWindowAsync(
            readContext, request.EntityType, request.ScanManifestId, request.Scope, runIdText,
            request.Offset, request.Limit, cancellationToken);

        var writer = new OwnershipResultWriter(writeContext, runId, logger);
        var matched = 0;
        var newAssignments = 0;
        var newRankOne = 0;
        var flushes = 0;

        async Task FlushAsync()
        {
            var flushed = await writer.FlushAsync(cancellationToken);
            newAssignments += flushed.NewAssignments;
            newRankOne += flushed.NewRankOneAssignments;
            flushes++;
        }

        Guid? currentEntityId = null;
        var currentCandidates = new List<OwnershipCandidateRow>();

        async Task CloseEntityGroupAsync()
        {
            if (currentEntityId is not { } entityId || currentCandidates.Count == 0)
            {
                return;
            }

            matched++;
            writer.Stage(request.EntityType, entityId, currentCandidates, DateTimeOffset.UtcNow);
            currentCandidates = [];

            if (writer.PendingCount >= FlushBatchSize)
            {
                await FlushAsync();
            }
        }

        var candidates = OwnershipCandidateReader.StreamAsync(
            readContext, request.EntityType, rules, request.ScanManifestId, request.Scope, runIdText,
            request.Offset, request.Limit, cancellationToken);

        await foreach (var row in candidates)
        {
            if (row.EntityId != currentEntityId)
            {
                await CloseEntityGroupAsync();
                currentEntityId = row.EntityId;
            }

            currentCandidates.Add(row);
        }

        await CloseEntityGroupAsync();
        await FlushAsync();

        var summary = new OwnershipChunkSummary(
            RunId: runId,
            EntityType: request.EntityType,
            Offset: request.Offset,
            EntitiesScanned: inWindow,
            EntitiesMatched: matched,
            NewAssignments: newAssignments,
            NewRankOneAssignments: newRankOne,
            Flushes: flushes);

        logger.LogInformation(
            "Ownership run {RunId} chunk {EntityType} [{Offset}, {End}) complete for manifest {ScanManifestId}: {@Summary}",
            runId, request.EntityType, request.Offset, request.Offset + request.Limit, request.ScanManifestId, summary);

        return summary;
    }
}
