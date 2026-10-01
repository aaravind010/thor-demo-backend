using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Thor.Workflows.Abstractions;
using Thor.Workflows.Atre.Constants;
using Thor.Workflows.Atre.Models;
using Thor.Workflows.Atre.Steps;
using Thor.Workflows.Atre.Tests.Fixtures;
using Xunit;

namespace Thor.Workflows.Atre.Tests.Steps;

public sealed class AtreFinalizeStepTests(TenantDatabaseFixture db) : IClassFixture<TenantDatabaseFixture>
{
    private AtreFinalizeStep NewStep() =>
        new(NullLoggerFactory.Instance, new FakeTenantConnectionManager(_ => db.Context));

    private AtreStartRunStep NewStartRunStep() =>
        new(NullLoggerFactory.Instance, new FakeTenantConnectionManager(_ => db.Context),
            AtreWaves.DefaultAccountsPerChunk, AtreWaves.DefaultChunksPerWave);

    [Fact]
    public async Task RunAsync_AfterTheRunWasOpened_MarksItCompleted()
    {
        var manifestId = await db.SeedManifestAsync();
        var request = new AtreRequest(Guid.NewGuid(), ScanManifestId: manifestId);

        await using (var startContext = db.NewContext())
        {
            await NewStartRunStep().RunAsync(startContext, request, CancellationToken.None);
        }

        await using (var finalizeContext = db.NewContext())
        {
            await NewStep().RunAsync(finalizeContext, request, CancellationToken.None);
        }

        var workflow = await db.Context.Workflows.AsNoTracking()
            .SingleAsync(w => w.WorkflowType == WorkflowTypes.Atre && w.ScanManifestId == manifestId);
        Assert.Equal("completed", workflow.Status);
        Assert.NotNull(workflow.CompletedAt);
    }

    /// <summary>
    /// A run that reached the end having lost chunks did not complete, and must not say it did. The
    /// error column is the signal — <see cref="AtreRecordChunkFailureStep"/> appended to it as each
    /// chunk was tolerated, and finalize reads it to pick the terminal status.
    /// </summary>
    [Fact]
    public async Task RunAsync_WhenChunksWereLost_ClosesTheRunAsCompletedWithErrors()
    {
        var manifestId = await db.SeedManifestAsync();
        var request = new AtreRequest(Guid.NewGuid(), ScanManifestId: manifestId);

        await using (var startContext = db.NewContext())
        {
            await NewStartRunStep().RunAsync(startContext, request, CancellationToken.None);
        }

        await using (var failureContext = db.NewContext())
        {
            await new AtreRecordChunkFailureStep(
                    NullLoggerFactory.Instance, new FakeTenantConnectionManager(_ => db.Context))
                .RunAsync(
                    failureContext,
                    new AtreChunkFailureRequest(request.TenantId, manifestId, null, 20_000, 10_000,
                        new AtreFailureCause("States.Timeout", "boom")),
                    CancellationToken.None);
        }

        await using (var finalizeContext = db.NewContext())
        {
            await NewStep().RunAsync(finalizeContext, request, CancellationToken.None);
        }

        var workflow = await db.Context.Workflows.AsNoTracking()
            .SingleAsync(w => w.WorkflowType == WorkflowTypes.Atre && w.ScanManifestId == manifestId);
        Assert.Equal(AtreStatuses.CompletedWithErrors, workflow.Status);
        Assert.NotNull(workflow.CompletedAt);

        // The record of what was skipped survives the status change — it is the only way back to it.
        Assert.Contains("[20000, 30000)", workflow.Error);
    }

    /// <summary>
    /// Retrying a failed run reuses the same workflow row — the run id is derived from
    /// (tenant, manifest), so a second execution finds the first one's row. A clean retry must be
    /// able to report clean: the previous attempt's lost windows are not this attempt's.
    /// </summary>
    [Fact]
    public async Task RunAsync_ARetryThatLosesNothing_ClosesCleanDespiteTheLastAttemptsErrors()
    {
        var manifestId = await db.SeedManifestAsync();
        var request = new AtreRequest(Guid.NewGuid(), ScanManifestId: manifestId);

        // Attempt 1: a chunk is lost, so the run closes completed_with_errors.
        await using (var context = db.NewContext())
        {
            await NewStartRunStep().RunAsync(context, request, CancellationToken.None);
        }
        await using (var context = db.NewContext())
        {
            await new AtreRecordChunkFailureStep(
                    NullLoggerFactory.Instance, new FakeTenantConnectionManager(_ => db.Context))
                .RunAsync(
                    context,
                    new AtreChunkFailureRequest(request.TenantId, manifestId, null, 20_000, 10_000,
                        new AtreFailureCause("States.Timeout", "boom")),
                    CancellationToken.None);
        }
        await using (var context = db.NewContext())
        {
            await NewStep().RunAsync(context, request, CancellationToken.None);
        }

        // Attempt 2: same manifest, nothing lost this time.
        await using (var context = db.NewContext())
        {
            await NewStartRunStep().RunAsync(context, request, CancellationToken.None);
        }
        await using (var context = db.NewContext())
        {
            await NewStep().RunAsync(context, request, CancellationToken.None);
        }

        var workflow = await db.Context.Workflows.AsNoTracking()
            .SingleAsync(w => w.WorkflowType == WorkflowTypes.Atre && w.ScanManifestId == manifestId);
        Assert.Equal("completed", workflow.Status);
        Assert.True(string.IsNullOrEmpty(workflow.Error), $"stale error from the previous attempt: {workflow.Error}");
    }

    /// <summary>
    /// Fails loudly rather than quietly doing nothing. Reaching this state without a row means the
    /// waves ran without the start-run step that was supposed to precede them, which is a broken
    /// state machine, not a tolerable condition.
    /// </summary>
    [Fact]
    public async Task RunAsync_WithNoWorkflowRow_Throws()
    {
        var manifestId = await db.SeedManifestAsync();

        await using var context = db.NewContext();
        await Assert.ThrowsAsync<InvalidOperationException>(() => NewStep().RunAsync(
            context, new AtreRequest(Guid.NewGuid(), ScanManifestId: manifestId), CancellationToken.None));
    }
}
