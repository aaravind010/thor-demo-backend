using Thor.Workflows.Atre.Models;
using Thor.Workflows.Atre.Steps;
using Xunit;

namespace Thor.Workflows.Atre.Tests.Steps;

/// <summary>
/// Drives the step through the JSON boundary rather than calling the planner directly, because the
/// boundary is the part that has to agree with <c>asl/atre.asl.json</c>: the state machine hands this
/// step a payload assembled from the execution's own state, and only the optional fields a caller
/// omitted can be missing from it.
/// </summary>
public sealed class AtreNextWaveStepTests
{
    private const int AccountsPerChunk = 10;
    private const int ChunksPerWave = 3;

    private static async Task<AtreWave> RunAsync(string payloadJson)
    {
        var result = await new AtreNextWaveStep(AccountsPerChunk, ChunksPerWave)
            .ExecuteAsync(payloadJson, CancellationToken.None);
        return Assert.IsType<AtreWave>(result.Value);
    }

    /// <summary>
    /// The exact shape ingestion's chaining produces: it starts ATRE with a tenant and a manifest and
    /// nothing else, so <c>RunId</c> is simply absent from the payload all the way down. Nothing may
    /// require it to be there — which is why the Map builds its items here instead of in an
    /// <c>ItemSelector</c>, where an absent field is a <c>States.Runtime</c> failure.
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_PayloadWithNoRunId_StampsTheIdentityItDoesHaveOntoTheNextWave()
    {
        var tenantId = Guid.NewGuid();
        var manifestId = Guid.NewGuid();

        var wave = await RunAsync($$"""
            {
              "Request": { "TenantId": "{{tenantId}}", "ScanManifestId": "{{manifestId}}" },
              "WaveIndex": 0,
              "Results": [
                { "RunId": "{{Guid.NewGuid()}}", "Offset": 0,  "AccountsScanned": 10, "AccountsAssigned": 10, "AccountsWithNoFiringRule": 0, "NewAssignments": 10, "Flushes": 1 },
                { "RunId": "{{Guid.NewGuid()}}", "Offset": 10, "AccountsScanned": 10, "AccountsAssigned": 10, "AccountsWithNoFiringRule": 0, "NewAssignments": 10, "Flushes": 1 },
                { "RunId": "{{Guid.NewGuid()}}", "Offset": 20, "AccountsScanned": 10, "AccountsAssigned": 10, "AccountsWithNoFiringRule": 0, "NewAssignments": 10, "Flushes": 1 }
              ]
            }
            """);

        Assert.True(wave.HasMore);
        Assert.Equal(1, wave.WaveIndex);
        Assert.Equal(
            [
                new AtreChunkRequest(tenantId, manifestId, null, 30, AccountsPerChunk),
                new AtreChunkRequest(tenantId, manifestId, null, 40, AccountsPerChunk),
                new AtreChunkRequest(tenantId, manifestId, null, 50, AccountsPerChunk),
            ],
            wave.Chunks);
    }

    /// <summary>The other caller's shape: an explicit run id and no manifest, meaning a full-scan run.</summary>
    [Fact]
    public async Task ExecuteAsync_PayloadWithNoScanManifestId_CarriesTheRunIdInstead()
    {
        var tenantId = Guid.NewGuid();
        var runId = Guid.NewGuid();

        var wave = await RunAsync($$"""
            {
              "Request": { "TenantId": "{{tenantId}}", "RunId": "{{runId}}" },
              "WaveIndex": 4,
              "Results": [
                { "RunId": "{{runId}}", "Offset": 120, "AccountsScanned": 10, "AccountsAssigned": 10, "AccountsWithNoFiringRule": 0, "NewAssignments": 10, "Flushes": 1 },
                { "RunId": "{{runId}}", "Offset": 130, "AccountsScanned": 10, "AccountsAssigned": 10, "AccountsWithNoFiringRule": 0, "NewAssignments": 10, "Flushes": 1 },
                { "RunId": "{{runId}}", "Offset": 140, "AccountsScanned": 10, "AccountsAssigned": 10, "AccountsWithNoFiringRule": 0, "NewAssignments": 10, "Flushes": 1 }
              ]
            }
            """);

        Assert.All(wave.Chunks, chunk =>
        {
            Assert.Equal(tenantId, chunk.TenantId);
            Assert.Null(chunk.ScanManifestId);
            Assert.Equal(runId, chunk.RunId);
        });
    }

    [Fact]
    public async Task ExecuteAsync_AShortWave_EndsTheLoop()
    {
        var wave = await RunAsync($$"""
            {
              "Request": { "TenantId": "{{Guid.NewGuid()}}", "ScanManifestId": "{{Guid.NewGuid()}}" },
              "WaveIndex": 2,
              "Results": [
                { "RunId": "{{Guid.NewGuid()}}", "Offset": 60, "AccountsScanned": 10, "AccountsAssigned": 10, "AccountsWithNoFiringRule": 0, "NewAssignments": 10, "Flushes": 1 },
                { "RunId": "{{Guid.NewGuid()}}", "Offset": 70, "AccountsScanned": 3,  "AccountsAssigned": 3,  "AccountsWithNoFiringRule": 0, "NewAssignments": 3,  "Flushes": 1 },
                { "RunId": "{{Guid.NewGuid()}}", "Offset": 80, "AccountsScanned": 0,  "AccountsAssigned": 0,  "AccountsWithNoFiringRule": 0, "NewAssignments": 0,  "Flushes": 1 }
              ]
            }
            """);

        Assert.False(wave.HasMore);
        Assert.Empty(wave.Chunks);
    }
}
