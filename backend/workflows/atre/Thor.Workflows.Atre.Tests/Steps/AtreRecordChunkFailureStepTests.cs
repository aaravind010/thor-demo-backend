using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Thor.Workflows.Abstractions;
using Thor.Workflows.Atre.Models;
using Thor.Workflows.Atre.Steps;
using Thor.Workflows.Atre.Tests.Fixtures;
using Xunit;

namespace Thor.Workflows.Atre.Tests.Steps;

/// <summary>
/// Tolerating a failed chunk is only defensible if the window it skipped is written down. These pin
/// that it is, and that concurrent failures do not overwrite each other — a wave's chunks fail at the
/// same time, on separate invocations.
/// </summary>
public sealed class AtreRecordChunkFailureStepTests(TenantDatabaseFixture db) : IClassFixture<TenantDatabaseFixture>
{
    private AtreRecordChunkFailureStep NewStep() =>
        new(NullLoggerFactory.Instance, new FakeTenantConnectionManager(_ => db.Context));

    private async Task OpenRunAsync(AtreRequest request)
    {
        await using var context = db.NewContext();
        await new AtreStartRunStep(
                NullLoggerFactory.Instance, new FakeTenantConnectionManager(_ => db.Context),
                AtreWaves.DefaultAccountsPerChunk, AtreWaves.DefaultChunksPerWave)
            .RunAsync(context, request, CancellationToken.None);
    }

    private async Task RecordAsync(AtreChunkFailureRequest failure)
    {
        await using var context = db.NewContext();
        await NewStep().RunAsync(context, failure, CancellationToken.None);
    }

    private static AtreChunkFailureRequest Chunk(AtreRequest run, int offset, string cause) =>
        new(run.TenantId, run.ScanManifestId, run.RunId, offset, 10_000, new AtreFailureCause("States.Timeout", cause));

    [Fact]
    public async Task RunAsync_ALostChunk_RecordsItsWindowAndWhy()
    {
        var manifestId = await db.SeedManifestAsync();
        var request = new AtreRequest(Guid.NewGuid(), ScanManifestId: manifestId);
        await OpenRunAsync(request);

        await RecordAsync(Chunk(request, 20_000, "Task timed out after 900.00 seconds"));

        var workflow = await db.Context.Workflows.AsNoTracking()
            .SingleAsync(w => w.WorkflowType == WorkflowTypes.Atre && w.ScanManifestId == manifestId);
        Assert.Contains("[20000, 30000)", workflow.Error);
        Assert.Contains("Task timed out", workflow.Error);

        // Still in flight — the status is Finalize's call, once it knows whether more chunks failed.
        Assert.Equal("started", workflow.Status);
    }

    /// <summary>
    /// The reason the append is one UPDATE rather than a read-then-write. These run concurrently in
    /// production, on separate Lambda invocations, and a read-modify-write would drop all but one.
    /// </summary>
    [Fact]
    public async Task RunAsync_SeveralChunksFailingAtOnce_KeepsEveryWindow()
    {
        var manifestId = await db.SeedManifestAsync();
        var request = new AtreRequest(Guid.NewGuid(), ScanManifestId: manifestId);
        await OpenRunAsync(request);

        int[] offsets = [0, 10_000, 20_000, 30_000, 40_000];
        await Task.WhenAll(offsets.Select(offset => RecordAsync(Chunk(request, offset, "boom"))));

        var workflow = await db.Context.Workflows.AsNoTracking()
            .SingleAsync(w => w.WorkflowType == WorkflowTypes.Atre && w.ScanManifestId == manifestId);
        Assert.All(offsets, offset => Assert.Contains($"[{offset}, {offset + 10_000})", workflow.Error));
    }

    /// <summary>The API-triggered shape, where the row is found by run id rather than by manifest.</summary>
    [Fact]
    public async Task RunAsync_RunIdOnlyRequest_StillRecordsAgainstTheRow()
    {
        var request = new AtreRequest(Guid.NewGuid(), RunId: Guid.NewGuid());
        await OpenRunAsync(request);

        await RecordAsync(Chunk(request, 50_000, "boom"));

        var workflow = await db.Context.Workflows.AsNoTracking().SingleAsync(w => w.RunId == request.RunId);
        Assert.Contains("[50000, 60000)", workflow.Error);
    }

    /// <summary>
    /// Driven through the JSON boundary, because the payload is the Map item with the Catch's error
    /// merged into it rather than anything C# assembled.
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_TheMapItemCatchShape_RecordsTheWindow()
    {
        var manifestId = await db.SeedManifestAsync();
        var tenantId = Guid.NewGuid();
        await OpenRunAsync(new AtreRequest(tenantId, ScanManifestId: manifestId));

        await new AtreRecordChunkFailureStep(
                NullLoggerFactory.Instance, new FakeTenantConnectionManager(_ => db.NewContext()))
            .ExecuteAsync($$"""
                {
                  "TenantId": "{{tenantId}}",
                  "ScanManifestId": "{{manifestId}}",
                  "Offset": 70000,
                  "Limit": 10000,
                  "Error": { "Error": "Lambda.Unknown", "Cause": "{\"errorType\":\"OutOfMemory\"}" }
                }
                """, CancellationToken.None);

        var workflow = await db.Context.Workflows.AsNoTracking()
            .SingleAsync(w => w.WorkflowType == WorkflowTypes.Atre && w.ScanManifestId == manifestId);
        Assert.Contains("[70000, 80000)", workflow.Error);
        Assert.Contains("Lambda.Unknown", workflow.Error);
    }

    /// <summary>
    /// No row to append to is logged loudly and swallowed. Throwing would fail the Map item, which
    /// fails the Map, which undoes the tolerating this step exists to support.
    /// </summary>
    [Fact]
    public async Task RunAsync_WithNoWorkflowRow_DoesNotThrow()
    {
        var manifestId = await db.SeedManifestAsync();

        await RecordAsync(Chunk(new AtreRequest(Guid.NewGuid(), ScanManifestId: manifestId), 0, "boom"));

        Assert.False(await db.Context.Workflows.AsNoTracking()
            .AnyAsync(w => w.WorkflowType == WorkflowTypes.Atre && w.ScanManifestId == manifestId));
    }
}
