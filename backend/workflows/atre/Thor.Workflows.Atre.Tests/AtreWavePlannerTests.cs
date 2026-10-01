using Thor.Workflows.Atre.Models;
using Xunit;

namespace Thor.Workflows.Atre.Tests;

/// <summary>
/// Pure arithmetic — no database, which is the whole point of the design: a run never needs to know
/// its own size in advance, so planning a wave costs nothing.
/// </summary>
public sealed class AtreWavePlannerTests
{
    private const int AccountsPerChunk = 10_000;
    private const int ChunksPerWave = 5;
    private const int WaveSize = AccountsPerChunk * ChunksPerWave;

    private static readonly AtreRequest Request = new(Guid.NewGuid(), ScanManifestId: Guid.NewGuid());

    private static AtreChunkSummary Read(int offset, int accountsScanned) =>
        new(Guid.NewGuid(), offset, accountsScanned, 0, 0, 0, 1);

    /// <summary>The shape the Map's ClassifyChunkTolerated Pass emits: a flag, an offset, no counts.</summary>
    private static AtreChunkSummary Lost(int offset) =>
        new(default, offset, 0, 0, 0, 0, 0, Failed: true);

    private static IReadOnlyList<AtreChunkSummary> FullWave(int waveIndex) =>
        [.. Enumerable.Range(0, ChunksPerWave)
            .Select(i => Read((waveIndex * WaveSize) + (i * AccountsPerChunk), AccountsPerChunk))];

    [Fact]
    public void WaveAt_Zero_StartsAtTheBeginningWithContiguousChunks()
    {
        var wave = AtreWavePlanner.WaveAt(Request, 0, AccountsPerChunk, ChunksPerWave);

        Assert.Equal(0, wave.WaveIndex);
        Assert.Equal([0, 10_000, 20_000, 30_000, 40_000], wave.Chunks.Select(c => c.Offset));
        Assert.All(wave.Chunks, c => Assert.Equal(AccountsPerChunk, c.Limit));
    }

    /// <summary>
    /// Each item is a complete classify request. The Map passes items through untouched — there is no
    /// ItemSelector to fill the identity in, precisely because the optional fields cannot be
    /// referenced by a JSONPath that is allowed to resolve to nothing.
    /// </summary>
    [Fact]
    public void WaveAt_EveryChunk_CarriesTheRunsIdentity()
    {
        var wave = AtreWavePlanner.WaveAt(Request, 3, AccountsPerChunk, ChunksPerWave);

        Assert.All(wave.Chunks, chunk =>
        {
            Assert.Equal(Request.TenantId, chunk.TenantId);
            Assert.Equal(Request.ScanManifestId, chunk.ScanManifestId);
            Assert.Equal(Request.RunId, chunk.RunId);
        });
    }

    /// <summary>Wave k picks up exactly where wave k-1 stopped — no gap, no overlap.</summary>
    [Fact]
    public void WaveAt_SuccessiveWaves_AreContiguous()
    {
        var first = AtreWavePlanner.WaveAt(Request, 0, AccountsPerChunk, ChunksPerWave);
        var second = AtreWavePlanner.WaveAt(Request, 1, AccountsPerChunk, ChunksPerWave);

        var endOfFirst = first.Chunks[^1].Offset + first.Chunks[^1].Limit;
        Assert.Equal(endOfFirst, second.Chunks[0].Offset);
    }

    [Fact]
    public void NextWaveAfter_AFullWave_GoesRoundAgain()
    {
        var next = AtreWavePlanner.NextWaveAfter(Request, 0, FullWave(0), AccountsPerChunk, ChunksPerWave);

        Assert.True(next.HasMore);
        Assert.Equal(1, next.WaveIndex);
        Assert.Equal(WaveSize, next.Chunks[0].Offset);
    }

    /// <summary>
    /// A short wave means a chunk ran off the end of the result set. This is the only signal the run
    /// has that it is finished — nothing counted the scope beforehand.
    /// </summary>
    [Fact]
    public void NextWaveAfter_AShortWave_EndsTheRun()
    {
        IReadOnlyList<AtreChunkSummary> shortWave =
        [
            Read(0, AccountsPerChunk),
            Read(10_000, AccountsPerChunk),
            Read(20_000, 431),
            Read(30_000, 0),
            Read(40_000, 0),
        ];

        var next = AtreWavePlanner.NextWaveAfter(Request, 0, shortWave, AccountsPerChunk, ChunksPerWave);

        Assert.False(next.HasMore);
        Assert.Empty(next.Chunks);
    }

    [Fact]
    public void NextWaveAfter_AWaveThatReadNothing_EndsTheRun()
    {
        IReadOnlyList<AtreChunkSummary> empty =
            [.. Enumerable.Range(0, ChunksPerWave).Select(i => Read(i * AccountsPerChunk, 0))];

        Assert.False(AtreWavePlanner.NextWaveAfter(Request, 3, empty, AccountsPerChunk, ChunksPerWave).HasMore);
    }

    /// <summary>
    /// The cost of not counting up front: a scope that is an exact multiple of a wave spends one
    /// extra wave discovering there is nothing left. Cheaper than a COUNT over the whole scope on
    /// every run, but it should be a deliberate property rather than a surprise.
    /// </summary>
    [Fact]
    public void NextWaveAfter_AScopeEndingExactlyOnAWaveBoundary_CostsOneEmptyWave()
    {
        var afterFull = AtreWavePlanner.NextWaveAfter(Request, 0, FullWave(0), AccountsPerChunk, ChunksPerWave);
        Assert.True(afterFull.HasMore);

        // That extra wave reads nothing, and its emptiness is what ends the run.
        IReadOnlyList<AtreChunkSummary> nothing =
            [.. afterFull.Chunks.Select(c => Read(c.Offset, 0))];
        Assert.False(AtreWavePlanner.NextWaveAfter(Request, 1, nothing, AccountsPerChunk, ChunksPerWave).HasMore);
    }

    [Fact]
    public void NextWaveAfter_NoResultsAtAll_EndsTheRunRatherThanLoopingForever()
    {
        Assert.False(AtreWavePlanner.NextWaveAfter(Request, 0, [], AccountsPerChunk, ChunksPerWave).HasMore);
    }

    /// <summary>
    /// The trap the Failed flag exists to close. A tolerated chunk reports zero rows read, which is
    /// indistinguishable by count alone from a chunk that ran off the end of the accounts. Taking it
    /// at face value would end the run here and silently skip every account in every later wave — far
    /// more than the one chunk that actually failed.
    /// </summary>
    [Fact]
    public void NextWaveAfter_AFullWaveWithOneLostChunk_KeepsGoing()
    {
        IReadOnlyList<AtreChunkSummary> wave =
        [
            Read(0, AccountsPerChunk),
            Read(10_000, AccountsPerChunk),
            Lost(20_000),
            Read(30_000, AccountsPerChunk),
            Read(40_000, AccountsPerChunk),
        ];

        var next = AtreWavePlanner.NextWaveAfter(Request, 0, wave, AccountsPerChunk, ChunksPerWave);

        Assert.True(next.HasMore);
        Assert.Equal(WaveSize, next.Chunks[0].Offset);
    }

    /// <summary>A lost chunk does not prop up a wave that was genuinely short — the run still ends.</summary>
    [Fact]
    public void NextWaveAfter_AShortWaveWithALostChunk_StillEndsTheRun()
    {
        IReadOnlyList<AtreChunkSummary> wave =
        [
            Read(0, AccountsPerChunk),
            Lost(10_000),
            Read(20_000, 12),
            Read(30_000, 0),
            Read(40_000, 0),
        ];

        Assert.False(AtreWavePlanner.NextWaveAfter(Request, 0, wave, AccountsPerChunk, ChunksPerWave).HasMore);
    }

    /// <summary>
    /// A wave where nothing ran teaches the run nothing, so assuming it was full would loop until the
    /// 25,000-event execution-history cap. Stopping is not silent: every lost window is already in
    /// workflow.error and the run ends completed_with_errors.
    /// </summary>
    [Fact]
    public void NextWaveAfter_AWaveWhereEveryChunkFailed_StopsRatherThanLoopingForever()
    {
        IReadOnlyList<AtreChunkSummary> allLost =
            [.. Enumerable.Range(0, ChunksPerWave).Select(i => Lost(i * AccountsPerChunk))];

        Assert.False(AtreWavePlanner.NextWaveAfter(Request, 0, allLost, AccountsPerChunk, ChunksPerWave).HasMore);
    }
}
