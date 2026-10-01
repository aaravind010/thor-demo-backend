using Thor.Workflows.Ownership.Models;
using Thor.Workflows.Ownership.Steps;
using Xunit;

namespace Thor.Workflows.Ownership.Tests;

/// <summary>
/// The planner's phase arithmetic, and the next-wave step's JSON boundary — the part that has to
/// agree with <c>asl/ownership.asl.json</c>, including the tolerated-chunk shape the Map's Pass state
/// writes.
/// </summary>
public sealed class OwnershipWavePlannerTests
{
    private const int PerChunk = 10;
    private const int PerWave = 3;

    private static readonly OwnershipRequest ScanRun = new(Guid.NewGuid(), ScanManifestId: Guid.NewGuid());

    private static OwnershipChunkSummary Ran(string entityType, int offset, int scanned) =>
        new(Guid.NewGuid(), entityType, offset, scanned, scanned, scanned, scanned, 1);

    private static OwnershipChunkSummary Failed(string entityType, int offset) =>
        new(Guid.Empty, entityType, offset, 0, 0, 0, 0, 0, Failed: true);

    [Fact]
    public void FirstWave_IsAccountWaveZero_OpeningItsPhase_WithTheScopeResolved()
    {
        var wave = OwnershipWavePlanner.FirstWave(ScanRun, PerChunk, PerWave);

        Assert.Equal("account", wave.EntityType);
        Assert.Equal(0, wave.WaveIndex);
        Assert.True(wave.PhaseStart);
        Assert.True(wave.HasMore);
        Assert.Equal([0, 10, 20], wave.Chunks.Select(c => c.Offset));
        Assert.All(wave.Chunks, c => Assert.Equal("unassigned_and_confirmed", c.Scope));
    }

    [Fact]
    public void FullWave_ContinuesTheSamePhase()
    {
        var next = OwnershipWavePlanner.NextWaveAfter(
            ScanRun, "account", 0, [Ran("account", 0, 10), Ran("account", 10, 10), Ran("account", 20, 10)], PerChunk, PerWave);

        Assert.Equal("account", next.EntityType);
        Assert.Equal(1, next.WaveIndex);
        Assert.False(next.PhaseStart);
        Assert.Equal([30, 40, 50], next.Chunks.Select(c => c.Offset));
    }

    [Fact]
    public void ShortWave_MovesToTheNextPhasesWaveZero()
    {
        var next = OwnershipWavePlanner.NextWaveAfter(
            ScanRun, "account", 4, [Ran("account", 120, 10), Ran("account", 130, 3), Ran("account", 140, 0)], PerChunk, PerWave);

        Assert.Equal("grp", next.EntityType);
        Assert.Equal(0, next.WaveIndex);
        Assert.True(next.PhaseStart);
        Assert.True(next.HasMore);
        Assert.Equal(0, next.Chunks[0].Offset);
    }

    [Fact]
    public void ShortWaveOfTheLastPhase_EndsTheRun()
    {
        var next = OwnershipWavePlanner.NextWaveAfter(ScanRun, "asset", 0, [Ran("asset", 0, 2)], PerChunk, PerWave);

        Assert.False(next.HasMore);
        Assert.Empty(next.Chunks);
    }

    [Fact]
    public void FailedChunk_CountsAsFull_SoThePhaseIsNotCutShort()
    {
        var next = OwnershipWavePlanner.NextWaveAfter(
            ScanRun, "grp", 0, [Ran("grp", 0, 10), Failed("grp", 10), Ran("grp", 20, 10)], PerChunk, PerWave);

        Assert.Equal("grp", next.EntityType);
        Assert.Equal(1, next.WaveIndex);
    }

    [Fact]
    public void WholeWaveFailed_EndsThatPhase_ButNotTheRun()
    {
        var next = OwnershipWavePlanner.NextWaveAfter(
            ScanRun, "account", 2, [Failed("account", 60), Failed("account", 70), Failed("account", 80)], PerChunk, PerWave);

        Assert.Equal("grp", next.EntityType);
        Assert.True(next.HasMore);
    }

    [Fact]
    public void TenantWideRun_DefaultsToUnassignedOnly_AndKeepsItsRunId()
    {
        var runId = Guid.NewGuid();
        var wave = OwnershipWavePlanner.FirstWave(new OwnershipRequest(Guid.NewGuid(), RunId: runId), PerChunk, PerWave);

        Assert.All(wave.Chunks, c =>
        {
            Assert.Equal("unassigned", c.Scope);
            Assert.Equal(runId, c.RunId);
            Assert.Null(c.ScanManifestId);
        });
    }

    /// <summary>
    /// The payload the state machine assembles: <c>$.Request</c> as ingestion sent it (no RunId, no
    /// Scope), the wave's phase and index, and the Map's results — one of them the tolerated-chunk
    /// Pass state's bare <c>{Failed, Offset, EntityType}</c>.
    /// </summary>
    [Fact]
    public async Task NextWaveStep_ReadsTheStateMachinesPayload_IncludingATolerated_Chunk()
    {
        var tenantId = Guid.NewGuid();
        var manifestId = Guid.NewGuid();

        var result = await new OwnershipNextWaveStep(PerChunk, PerWave).ExecuteAsync($$"""
            {
              "Request": { "TenantId": "{{tenantId}}", "ScanManifestId": "{{manifestId}}" },
              "EntityType": "account",
              "WaveIndex": 0,
              "Results": [
                { "RunId": "{{Guid.NewGuid()}}", "EntityType": "account", "Offset": 0, "EntitiesScanned": 10, "EntitiesMatched": 4, "NewAssignments": 5, "NewRankOneAssignments": 4, "Flushes": 1 },
                { "Failed": true, "Offset": 10, "EntityType": "account" },
                { "RunId": "{{Guid.NewGuid()}}", "EntityType": "account", "Offset": 20, "EntitiesScanned": 10, "EntitiesMatched": 0, "NewAssignments": 0, "NewRankOneAssignments": 0, "Flushes": 1 }
              ]
            }
            """, CancellationToken.None);

        var wave = Assert.IsType<OwnershipWave>(result.Value);
        Assert.Equal("account", wave.EntityType);
        Assert.Equal(1, wave.WaveIndex);
        Assert.Equal(
            new OwnershipChunkRequest(tenantId, manifestId, null, "unassigned_and_confirmed", "account", 30, PerChunk),
            wave.Chunks[0]);
    }
}
