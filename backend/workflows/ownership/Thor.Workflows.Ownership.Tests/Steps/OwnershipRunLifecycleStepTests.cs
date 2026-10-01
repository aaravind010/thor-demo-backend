using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Thor.DataLayer.Models.Tenants;
using Thor.Workflows.Abstractions;
using Thor.Workflows.Ownership.Constants;
using Thor.Workflows.Ownership.Models;
using Thor.Workflows.Ownership.Persistence;
using Thor.Rules.Ownership;
using Thor.Workflows.Ownership.Steps;
using Thor.Workflows.Ownership.Tests.Fixtures;
using Xunit;

namespace Thor.Workflows.Ownership.Tests.Steps;

/// <summary>
/// The once-per-run steps around the waves — start-run, walk, finalize and the two failure
/// recorders — and the workflow row they share.
/// </summary>
public sealed class OwnershipRunLifecycleStepTests(TenantDatabaseFixture db) : IClassFixture<TenantDatabaseFixture>
{
    private FakeTenantConnectionManager Connections => new(_ => db.NewContext());

    private OwnershipStartRunStep StartRun() => new(NullLoggerFactory.Instance, Connections, entitiesPerChunk: 10, chunksPerWave: 2);

    private Task<WorkflowEntity> OwnershipRowAsync(Guid manifestId) =>
        db.Context.Workflows.AsNoTracking().SingleAsync(w => w.ScanManifestId == manifestId && w.WorkflowType == WorkflowTypes.Ownership);

    [Fact]
    public async Task StartRun_OpensAndLinksTheRow_SeedsTheRules_AndHandsBackTheFirstWave()
    {
        var manifestId = await db.SeedManifestAsync();
        var ingestion = await db.SeedIngestionWorkflowAsync(manifestId);

        var wave = await StartRun().RunAsync(db.NewContext(), new OwnershipRequest(Guid.NewGuid(), manifestId));

        var row = await OwnershipRowAsync(manifestId);
        Assert.Equal("started", row.Status);
        Assert.Equal(WorkflowTriggers.StepFunctions, row.Trigger);
        Assert.True(await db.Context.WorkflowGraphs.AnyAsync(g => g.ParentWorkflowId == ingestion.Id && g.ChildWorkflowId == row.Id));
        Assert.True(await db.Context.OwnershipRules.AnyAsync(r => r.RuleName == OwnershipRuleDefaults.EmailExactMatch));
        Assert.Equal("account", wave.EntityType);
        Assert.True(wave.PhaseStart);
        Assert.Equal(2, wave.Chunks.Count);
    }

    [Fact]
    public async Task StartRun_OnARetry_ClearsWhatTheLastAttemptRecorded()
    {
        var manifestId = await db.SeedManifestAsync();
        var request = new OwnershipRequest(Guid.NewGuid(), manifestId);
        await StartRun().RunAsync(db.NewContext(), request);
        await new OwnershipRecordFailureStep(NullLoggerFactory.Instance, Connections)
            .RunAsync(db.NewContext(), new OwnershipFailureRequest(request, new OwnershipFailureCause("States.Timeout", null)));

        await StartRun().RunAsync(db.NewContext(), request);

        var row = await OwnershipRowAsync(manifestId);
        Assert.Equal("started", row.Status);
        Assert.Null(row.Error);
    }

    [Fact]
    public async Task StartRun_WithNoManifestAndNoRunId_ThrowsBeforeOpeningARow()
    {
        var before = await db.Context.Workflows.CountAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            StartRun().RunAsync(db.NewContext(), new OwnershipRequest(Guid.NewGuid())));

        Assert.Equal(before, await db.Context.Workflows.CountAsync());
    }

    [Fact]
    public async Task StartRun_WithAnUnknownScope_Throws()
    {
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            StartRun().RunAsync(db.NewContext(), new OwnershipRequest(Guid.NewGuid(), RunId: Guid.NewGuid(), Scope: "everything")));
    }

    [Fact]
    public async Task StartRun_ForATenantWideRun_RecordsTheApiTrigger_UnderTheCallersRunId()
    {
        var runId = Guid.NewGuid();

        await StartRun().RunAsync(db.NewContext(), new OwnershipRequest(Guid.NewGuid(), RunId: runId, Scope: OwnershipAssignmentScope.All));

        var row = await db.Context.Workflows.AsNoTracking().SingleAsync(w => w.RunId == runId);
        Assert.Equal(WorkflowTriggers.Api, row.Trigger);
        Assert.Equal(WorkflowTypes.Ownership, row.WorkflowType);
    }

    [Fact]
    public async Task Walk_WithABudgetOfZero_ReportsInProgress_UntilTheHierarchyIsExhausted()
    {
        await db.SeedRulesAsync();
        var identity = await db.AddIdentityAsync("LW1", "Nest Owner", "nest-owner@corp.com");
        var groups = new List<Grp>();
        for (var i = 0; i < 4; i++)
        {
            groups.Add(await db.AddGroupAsync($"lifecycle-nest-{i}"));
            if (i > 0)
            {
                await db.AddEdgeAsync(groups[i].Id, "grp", groups[i - 1].Id, "grp", "MEMBER_OF");
            }
        }
        await db.AddPriorOwnerAsync("grp", groups[0].Id, identity.Id);

        var request = new OwnershipWalkRequest(new OwnershipRequest(Guid.NewGuid(), RunId: Guid.NewGuid()), "grp");
        var step = new OwnershipWalkStep(NullLoggerFactory.Instance, Connections, TimeSpan.Zero);

        var results = new List<OwnershipWalkResult>();
        do
        {
            var stepResult = await step.ExecuteAsync(System.Text.Json.JsonSerializer.Serialize(request), CancellationToken.None);
            results.Add(Assert.IsType<OwnershipWalkResult>(stepResult.Value));
            Assert.Equal(results[^1].IsInProgress, stepResult.IsInProgress);
        }
        while (results[^1].IsInProgress && results.Count < 20);

        Assert.True(results.Count > 1);
        Assert.False(results[^1].IsInProgress);
        var nestRule = await db.Context.OwnershipRules.SingleAsync(r => r.RuleName == OwnershipRuleDefaults.GroupNestingInheritance);
        Assert.Equal(3, await db.Context.OwnershipWalkCandidates.CountAsync(
            c => c.RunId == request.Request.RunId!.Value.ToString() && c.RuleId == nestRule.Id
                && groups.Select(g => g.Id).Contains(c.EntityId)));
    }

    [Fact]
    public async Task Walk_ForAPhaseWithNoWalkRules_IsDoneImmediately()
    {
        await db.SeedRulesAsync();

        var result = await new OwnershipWalkStep(NullLoggerFactory.Instance, Connections, TimeSpan.Zero)
            .RunAsync(db.NewContext(), new OwnershipWalkRequest(new OwnershipRequest(Guid.NewGuid(), RunId: Guid.NewGuid()), "account"));

        Assert.False(result.IsInProgress);
        Assert.Equal(0, result.RulesWalked);
    }

    [Fact]
    public async Task Finalize_ClosesTheRowCompleted_AndDropsTheRunsWalkStaging()
    {
        var manifestId = await db.SeedManifestAsync();
        var tenantId = Guid.NewGuid();
        var request = new OwnershipRequest(tenantId, manifestId);
        await StartRun().RunAsync(db.NewContext(), request);
        var runId = OwnershipRunIdentity.Derive(tenantId, manifestId, null, NullLogger.Instance).ToString();
        db.Context.OwnershipWalkCandidates.Add(new OwnershipWalkCandidate
        {
            RunId = runId, RuleId = Guid.NewGuid(), EntityId = Guid.NewGuid(), IdentityId = Guid.NewGuid(), Depth = 1,
        });
        await db.Context.SaveChangesAsync();

        await new OwnershipFinalizeStep(NullLoggerFactory.Instance, Connections).RunAsync(db.NewContext(), request);

        Assert.Equal("completed", (await OwnershipRowAsync(manifestId)).Status);
        Assert.False(await db.Context.OwnershipWalkCandidates.AnyAsync(c => c.RunId == runId));
    }

    [Fact]
    public async Task Finalize_AfterAToleratedChunk_ClosesTheRowCompletedWithErrors()
    {
        var manifestId = await db.SeedManifestAsync();
        var tenantId = Guid.NewGuid();
        var request = new OwnershipRequest(tenantId, manifestId);
        await StartRun().RunAsync(db.NewContext(), request);

        await new OwnershipRecordChunkFailureStep(NullLoggerFactory.Instance, Connections).RunAsync(
            db.NewContext(),
            new OwnershipChunkFailureRequest(tenantId, manifestId, null, "grp", 20, 10, new OwnershipFailureCause("Lambda.Timeout", "timed out")));
        await new OwnershipFinalizeStep(NullLoggerFactory.Instance, Connections).RunAsync(db.NewContext(), request);

        var row = await OwnershipRowAsync(manifestId);
        Assert.Equal(OwnershipStatuses.CompletedWithErrors, row.Status);
        Assert.Contains("Chunk grp [20, 30) was not matched", row.Error);
    }

    [Fact]
    public async Task RecordFailure_ClosesTheRowFailed_WithTheCatchPayload()
    {
        var manifestId = await db.SeedManifestAsync();
        var request = new OwnershipRequest(Guid.NewGuid(), manifestId);
        await StartRun().RunAsync(db.NewContext(), request);

        await new OwnershipRecordFailureStep(NullLoggerFactory.Instance, Connections).RunAsync(
            db.NewContext(), new OwnershipFailureRequest(request, new OwnershipFailureCause("InvalidOperationException", "Neptune bulk load failed")));

        var row = await OwnershipRowAsync(manifestId);
        Assert.Equal("failed", row.Status);
        Assert.Equal("InvalidOperationException: Neptune bulk load failed", row.Error);
    }
}
