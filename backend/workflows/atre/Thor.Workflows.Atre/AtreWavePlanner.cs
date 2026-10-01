using Thor.Workflows.Atre.Models;

namespace Thor.Workflows.Atre;

/// <summary>
/// Turns a wave index into the Map items that wave covers, and reads a finished wave's counts to say
/// whether another follows. Pure arithmetic over numbers the run already has — no database access,
/// which is the point: the run never needs to know its own size in advance.
/// </summary>
public static class AtreWavePlanner
{
    /// <summary>
    /// The windows of wave <paramref name="waveIndex"/>, each carrying the run's identity so the Map
    /// can pass items straight through to the classify invocations untouched.
    /// </summary>
    public static AtreWave WaveAt(AtreRequest request, int waveIndex, int accountsPerChunk, int chunksPerWave)
    {
        var firstOffset = waveIndex * chunksPerWave * accountsPerChunk;

        return new AtreWave(
            WaveIndex: waveIndex,
            Chunks: [.. Enumerable.Range(0, chunksPerWave).Select(chunk => new AtreChunkRequest(
                request.TenantId,
                request.ScanManifestId,
                request.RunId,
                Offset: firstOffset + (chunk * accountsPerChunk),
                Limit: accountsPerChunk))],
            HasMore: true);
    }

    /// <summary>
    /// The next wave, given what the one just finished actually read.
    ///
    /// <para>A wave that read every row it asked for might have been followed by more, so another
    /// wave goes out. A short one means the last chunk ran off the end of the result set, and the
    /// run is done. This is the whole reason no step counts the accounts in scope — how far to go is
    /// discovered by going.</para>
    ///
    /// <para>One consequence worth knowing: a run whose account count is an exact multiple of a
    /// wave's size spends one extra wave learning there is nothing left. That costs a few invocations
    /// that read zero rows, which is cheaper than a <c>COUNT</c> over the whole scope on every
    /// run.</para>
    ///
    /// <para>A failed chunk counts as a <em>full</em> one. It is tolerated rather than fatal — the
    /// other chunks' accounts still get classified — but it reports zero rows read, and taking that
    /// at face value would end the run early and skip every account in every later wave, costing far
    /// more than the chunk that actually failed. Nobody knows how many rows it would have read, so
    /// the planner assumes the most that keeps the run going.</para>
    ///
    /// <para>Unless the <em>whole</em> wave failed, in which case the run stops. Assuming a full wave
    /// there would keep a run that can no longer read anything looping until it hits the 25,000-event
    /// execution-history cap. Stopping is recorded, not silent: every failed window is already in
    /// <c>workflow.error</c> and the run ends
    /// <see cref="Constants.AtreStatuses.CompletedWithErrors"/>.</para>
    /// </summary>
    public static AtreWave NextWaveAfter(
        AtreRequest request,
        int finishedWaveIndex,
        IReadOnlyList<AtreChunkSummary> results,
        int accountsPerChunk,
        int chunksPerWave)
    {
        var accountsRead = results.Sum(r => r.Failed ? accountsPerChunk : r.AccountsScanned);
        var waveWasFull = accountsRead == chunksPerWave * accountsPerChunk;
        var anyChunkRan = results.Any(r => !r.Failed);

        return waveWasFull && anyChunkRan
            ? WaveAt(request, finishedWaveIndex + 1, accountsPerChunk, chunksPerWave)
            : new AtreWave(WaveIndex: finishedWaveIndex + 1, Chunks: [], HasMore: false);
    }
}
