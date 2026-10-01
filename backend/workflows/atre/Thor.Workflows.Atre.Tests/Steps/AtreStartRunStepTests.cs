using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Thor.Workflows.Abstractions;
using Thor.Workflows.Atre.Models;
using Thor.Workflows.Atre.Steps;
using Thor.Workflows.Atre.Tests.Fixtures;
using Xunit;

namespace Thor.Workflows.Atre.Tests.Steps;

/// <summary>
/// Covers the once-per-run bookkeeping that cannot live in the Map body: opening the <c>workflow</c>
/// row and linking it to the ingestion run. Also pins the thing this step deliberately does not do —
/// count.
/// </summary>
public sealed class AtreStartRunStepTests(TenantDatabaseFixture db) : IClassFixture<TenantDatabaseFixture>
{
    private const int AccountsPerChunk = 10;
    private const int ChunksPerWave = 3;

    private async Task<AtreWave> RunAsync(AtreRequest request)
    {
        var step = new AtreStartRunStep(
            NullLoggerFactory.Instance, new FakeTenantConnectionManager(_ => db.Context), AccountsPerChunk, ChunksPerWave);
        await using var context = db.NewContext();
        return await step.RunAsync(context, request, CancellationToken.None);
    }

    [Fact]
    public async Task RunAsync_ScanDrivenRequest_OpensTheWorkflowRowAsStepFunctionsTriggered()
    {
        var manifestId = await db.SeedManifestAsync();

        await RunAsync(new AtreRequest(Guid.NewGuid(), ScanManifestId: manifestId));

        var workflow = await db.Context.Workflows.AsNoTracking()
            .SingleAsync(w => w.WorkflowType == WorkflowTypes.Atre && w.ScanManifestId == manifestId);
        Assert.Equal(WorkflowTriggers.StepFunctions, workflow.Trigger);
        Assert.Equal("started", workflow.Status);

        // ATRE never carries a ScanId: AtreRequest has none, and looking the manifest up purely to
        // fill an optional column is not worth the dependency.
        Assert.Null(workflow.ScanId);
    }

    [Fact]
    public async Task RunAsync_StandaloneRequest_OpensTheWorkflowRowAsApiTriggeredAndLinksNothing()
    {
        var runId = Guid.NewGuid();

        await RunAsync(new AtreRequest(Guid.NewGuid(), RunId: runId));

        var workflow = await db.Context.Workflows.AsNoTracking().SingleAsync(w => w.RunId == runId);
        Assert.Equal(WorkflowTriggers.Api, workflow.Trigger);
        Assert.False(await db.Context.WorkflowGraphs.AnyAsync(g => g.ChildWorkflowId == workflow.Id));
    }

    [Fact]
    public async Task RunAsync_WithIngestionParent_LinksWorkflowGraphIdempotently()
    {
        var manifestId = await db.SeedManifestAsync();
        var ingestionWorkflow = await db.SeedIngestionWorkflowAsync(manifestId);
        var request = new AtreRequest(Guid.NewGuid(), ScanManifestId: manifestId);

        await RunAsync(request);

        var atreWorkflow = await db.Context.Workflows.AsNoTracking()
            .SingleAsync(w => w.WorkflowType == WorkflowTypes.Atre && w.ScanManifestId == manifestId);
        Assert.True(await db.Context.WorkflowGraphs.AnyAsync(
            g => g.ParentWorkflowId == ingestionWorkflow.Id && g.ChildWorkflowId == atreWorkflow.Id));

        // A retry must not attempt a duplicate insert against the composite primary key.
        await RunAsync(request);
        Assert.Equal(1, await db.Context.WorkflowGraphs.CountAsync(g => g.ChildWorkflowId == atreWorkflow.Id));
    }

    /// <summary>No ingestion row for the manifest is a warning, not a failure — lineage is observability.</summary>
    [Fact]
    public async Task RunAsync_WithNoIngestionParent_StillSucceeds()
    {
        var manifestId = await db.SeedManifestAsync();

        var wave = await RunAsync(new AtreRequest(Guid.NewGuid(), ScanManifestId: manifestId));

        Assert.Equal(0, wave.WaveIndex);
    }

    /// <summary>
    /// The first wave is the same arithmetic as every other one, and is produced without touching the
    /// accounts at all — no COUNT, so a run of any size starts at the same cost. Each chunk comes out
    /// as a complete classify request, because the Map has no ItemSelector to add anything to it.
    /// </summary>
    [Fact]
    public async Task RunAsync_ReturnsTheFirstWaveWithoutCountingTheScope()
    {
        var manifestId = await db.SeedManifestAsync();
        var accounts = await db.SeedAccountsAsync([.. Enumerable.Range(0, 25).Select(i => ($"first-wave-{i}", "user"))]);
        await db.MarkChangedInManifestAsync(manifestId, accounts);
        var tenantId = Guid.NewGuid();

        var wave = await RunAsync(new AtreRequest(tenantId, ScanManifestId: manifestId));

        Assert.Equal(0, wave.WaveIndex);
        Assert.True(wave.HasMore);
        Assert.Equal(
            [
                new AtreChunkRequest(tenantId, manifestId, null, 0, AccountsPerChunk),
                new AtreChunkRequest(tenantId, manifestId, null, 10, AccountsPerChunk),
                new AtreChunkRequest(tenantId, manifestId, null, 20, AccountsPerChunk),
            ],
            wave.Chunks);
    }

    /// <summary>
    /// Seeding no accounts changes nothing about the first wave — proof the plan is arithmetic, not a
    /// measurement. The empty chunks simply read nothing and the run stops after one wave.
    /// </summary>
    [Fact]
    public async Task RunAsync_EmptyScope_StillReturnsAFullFirstWave()
    {
        var manifestId = await db.SeedManifestAsync();

        var wave = await RunAsync(new AtreRequest(Guid.NewGuid(), ScanManifestId: manifestId));

        Assert.Equal(ChunksPerWave, wave.Chunks.Count);
    }
}
