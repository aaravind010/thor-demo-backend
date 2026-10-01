using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Thor.Workflows.Abstractions;
using Thor.Workflows.Atre.Models;
using Thor.Workflows.Atre.Steps;
using Thor.Workflows.Atre.Tests.Fixtures;
using Xunit;

namespace Thor.Workflows.Atre.Tests.Steps;

/// <summary>
/// The Catch path's one job: a run that failed must not go on reading <c>started</c>. Nothing else
/// closes the row — a chunk killed by the Lambda timeout runs no catch block of its own.
/// </summary>
public sealed class AtreRecordFailureStepTests(TenantDatabaseFixture db) : IClassFixture<TenantDatabaseFixture>
{
    private AtreRecordFailureStep NewStep() =>
        new(NullLoggerFactory.Instance, new FakeTenantConnectionManager(_ => db.Context));

    private AtreStartRunStep NewStartRunStep() =>
        new(NullLoggerFactory.Instance, new FakeTenantConnectionManager(_ => db.Context),
            AtreWaves.DefaultAccountsPerChunk, AtreWaves.DefaultChunksPerWave);

    private async Task OpenRunAsync(AtreRequest request)
    {
        await using var context = db.NewContext();
        await NewStartRunStep().RunAsync(context, request, CancellationToken.None);
    }

    private async Task RecordAsync(AtreFailureRequest failure)
    {
        await using var context = db.NewContext();
        await NewStep().RunAsync(context, failure, CancellationToken.None);
    }

    [Fact]
    public async Task RunAsync_AfterTheRunWasOpened_ClosesTheRowAsFailedWithTheCause()
    {
        var manifestId = await db.SeedManifestAsync();
        var request = new AtreRequest(Guid.NewGuid(), ScanManifestId: manifestId);
        await OpenRunAsync(request);

        await RecordAsync(new AtreFailureRequest(
            request, new AtreFailureCause("States.Timeout", "Task timed out after 900.00 seconds")));

        var workflow = await db.Context.Workflows.AsNoTracking()
            .SingleAsync(w => w.WorkflowType == WorkflowTypes.Atre && w.ScanManifestId == manifestId);
        Assert.Equal("failed", workflow.Status);
        Assert.NotNull(workflow.CompletedAt);
        Assert.Contains("States.Timeout", workflow.Error);
        Assert.Contains("Task timed out", workflow.Error);
    }

    /// <summary>The API-triggered shape, where the row is found by run id rather than by manifest.</summary>
    [Fact]
    public async Task RunAsync_RunIdOnlyRequest_StillFindsAndClosesTheRow()
    {
        var request = new AtreRequest(Guid.NewGuid(), RunId: Guid.NewGuid());
        await OpenRunAsync(request);

        await RecordAsync(new AtreFailureRequest(request, new AtreFailureCause("Exception", "boom")));

        var workflow = await db.Context.Workflows.AsNoTracking().SingleAsync(w => w.RunId == request.RunId);
        Assert.Equal("failed", workflow.Status);
    }

    /// <summary>
    /// StartRun failing before it opened the row reaches this state with nothing to close. That is a
    /// no-op, not an error — throwing here would bury the real failure behind a second one.
    /// </summary>
    [Fact]
    public async Task RunAsync_WithNoWorkflowRow_IsANoOp()
    {
        var manifestId = await db.SeedManifestAsync();

        await RecordAsync(new AtreFailureRequest(
            new AtreRequest(Guid.NewGuid(), ScanManifestId: manifestId),
            new AtreFailureCause("Exception", "failed before the row existed")));

        Assert.False(await db.Context.Workflows.AsNoTracking()
            .AnyAsync(w => w.WorkflowType == WorkflowTypes.Atre && w.ScanManifestId == manifestId));
    }

    /// <summary>
    /// A Catch can fire with an error shape carrying neither field. The row still has to close —
    /// leaving it "started" is the bug this step exists to prevent.
    /// </summary>
    [Fact]
    public async Task RunAsync_WithNoCause_StillClosesTheRow()
    {
        var manifestId = await db.SeedManifestAsync();
        var request = new AtreRequest(Guid.NewGuid(), ScanManifestId: manifestId);
        await OpenRunAsync(request);

        await RecordAsync(new AtreFailureRequest(request, Error: null));

        var workflow = await db.Context.Workflows.AsNoTracking()
            .SingleAsync(w => w.WorkflowType == WorkflowTypes.Atre && w.ScanManifestId == manifestId);
        Assert.Equal("failed", workflow.Status);
        Assert.NotNull(workflow.Error);
    }

    /// <summary>
    /// Driven through the JSON boundary, because the payload is assembled by the state machine rather
    /// than by C#: <c>$.Request</c> and the <c>$.Error</c> that Catch wrote.
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_TheCatchPayloadShape_ClosesTheRow()
    {
        var manifestId = await db.SeedManifestAsync();
        var tenantId = Guid.NewGuid();
        await OpenRunAsync(new AtreRequest(tenantId, ScanManifestId: manifestId));

        // A fresh context per call, not the fixture's shared one: ExecuteAsync disposes the context it
        // opens, which is correct of it and fatal to a context the rest of the class still needs.
        var result = await new AtreRecordFailureStep(
                NullLoggerFactory.Instance, new FakeTenantConnectionManager(_ => db.NewContext()))
            .ExecuteAsync($$"""
                {
                  "Request": { "TenantId": "{{tenantId}}", "ScanManifestId": "{{manifestId}}" },
                  "Error": { "Error": "Lambda.Unknown", "Cause": "{\"errorType\":\"OutOfMemory\"}" }
                }
                """, CancellationToken.None);

        Assert.Null(result.Value);

        var workflow = await db.Context.Workflows.AsNoTracking()
            .SingleAsync(w => w.WorkflowType == WorkflowTypes.Atre && w.ScanManifestId == manifestId);
        Assert.Equal("failed", workflow.Status);
        Assert.Contains("Lambda.Unknown", workflow.Error);
    }
}
