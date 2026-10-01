using Microsoft.Extensions.Logging;
using Thor.DataConnectionManager;
using Thor.DataLayer.Data;
using Thor.Workflows.Abstractions;
using Thor.Workflows.Hosting.Composition;
using Thor.Workflows.Ownership.Models;
using Thor.Workflows.Ownership.Persistence;
using Thor.Workflows.Ownership.Rules;
using Thor.Workflows.Ownership.Walk;

namespace Thor.Workflows.Ownership.Steps;

/// <summary>
/// Runs at the start of each phase, before its first vote wave: stages every walk rule's inherited
/// owners for that phase via <see cref="OwnershipWalkPropagator"/>. The vote chunks then read those
/// rows as the walk rules' candidates, so the traversal happens once per phase rather than once per
/// chunk.
///
/// <para>Shaped like a poll step. A hierarchy can be deeper than one invocation can finish, so the
/// step stops starting levels when its time budget is spent and answers
/// <see cref="OwnershipWalkResult.IsInProgress"/>; the state machine invokes it again and it carries
/// on from the deepest staged level. A phase with no walk rules answers done immediately.</para>
/// </summary>
public sealed class OwnershipWalkStep : WorkflowStep<OwnershipWalkRequest>
{
    private readonly ILoggerFactory _loggerFactory;
    private readonly ITenantConnectionManager _tenantConnectionManager;
    private readonly TimeSpan _budget;

    /// <summary>Test seam — lets tests substitute the tenant-connection dependency and pin the time budget.</summary>
    public OwnershipWalkStep(ILoggerFactory loggerFactory, ITenantConnectionManager tenantConnectionManager, TimeSpan budget)
    {
        _loggerFactory = loggerFactory;
        _tenantConnectionManager = tenantConnectionManager;
        _budget = budget;
    }

    public OwnershipWalkStep()
        : this(
            LoggerFactory.Create(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Information)),
            TenantConnectionManagerFactory.Build(),
            OwnershipWaves.WalkBudgetFromEnvironment())
    {
    }

    protected override async Task<StepResult> ExecuteAsync(OwnershipWalkRequest request, CancellationToken cancellationToken)
    {
        await using var db = await _tenantConnectionManager.GetTenantDbContextAsync(request.Request.TenantId, cancellationToken);
        var result = await RunAsync(db, request, cancellationToken);

        // IsInProgress rides on the result as well as the envelope: this step always runs on Lambda,
        // where the state machine's Choice reads it straight off the serialized response.
        return result.IsInProgress ? StepResult.InProgress(result) : StepResult.Completed(result);
    }

    public async Task<OwnershipWalkResult> RunAsync(TenantDbContext db, OwnershipWalkRequest request, CancellationToken cancellationToken = default)
    {
        var logger = _loggerFactory.CreateLogger<OwnershipWalkStep>();
        var run = request.Request;
        var runId = OwnershipRunIdentity.Derive(run.TenantId, run.ScanManifestId, run.RunId, logger);

        var rules = await OwnershipRuleLoader.LoadActiveAsync(db, request.EntityType, logger, cancellationToken);
        var walkRules = rules.Where(OwnershipWalkPropagator.IsWalkRule).ToList();
        if (walkRules.Count == 0)
        {
            return new OwnershipWalkResult(request.EntityType, 0, 0, 0, IsInProgress: false);
        }

        var progress = await new OwnershipWalkPropagator(db, runId.ToString(), logger)
            .RunAsync(walkRules, _budget, cancellationToken);

        logger.LogInformation(
            "Ownership walk for run {RunId}, phase {EntityType}: {Levels} level(s), {Rows} row(s) staged, complete: {IsComplete}.",
            runId, request.EntityType, progress.LevelsRun, progress.RowsStaged, progress.IsComplete);

        return new OwnershipWalkResult(
            request.EntityType, walkRules.Count, progress.LevelsRun, progress.RowsStaged, IsInProgress: !progress.IsComplete);
    }
}
