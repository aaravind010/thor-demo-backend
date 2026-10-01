using Thor.Workflows.Ownership.Constants;
using Thor.Workflows.Ownership.Models;

namespace Thor.Workflows.Ownership;

/// <summary>
/// Turns (phase, wave index) into the Map items that wave covers, and reads a finished wave's counts
/// to say what follows it: another wave of the same phase, the first wave of the next phase, or
/// nothing. Pure arithmetic over numbers the run already has, like <c>AtreWavePlanner</c> — the run
/// never counts its own scope in advance.
///
/// <para>Phases run in <see cref="ScopeConstants.EntityTypesInPhaseOrder"/> and never overlap: a
/// wave's chunks all finish before the planner is asked about the next one, so the last account chunk
/// has flushed before the first group chunk runs. That ordering is what lets a group rule read the
/// owners this run just gave its manager accounts.</para>
/// </summary>
public static class OwnershipWavePlanner
{
    /// <summary>Wave <paramref name="waveIndex"/> of the <paramref name="entityType"/> phase, each item carrying the run's identity and resolved scope.</summary>
    public static OwnershipWave WaveAt(
        OwnershipRequest request, string entityType, int waveIndex, int entitiesPerChunk, int chunksPerWave)
    {
        var firstOffset = waveIndex * chunksPerWave * entitiesPerChunk;
        var scope = OwnershipScopes.Resolve(request);

        return new OwnershipWave(
            EntityType: entityType,
            WaveIndex: waveIndex,
            Chunks: [.. Enumerable.Range(0, chunksPerWave).Select(chunk => new OwnershipChunkRequest(
                request.TenantId,
                request.ScanManifestId,
                request.RunId,
                scope,
                entityType,
                Offset: firstOffset + (chunk * entitiesPerChunk),
                Limit: entitiesPerChunk))],
            HasMore: true,
            PhaseStart: waveIndex == 0);
    }

    /// <summary>The run's first wave: wave zero of the first phase.</summary>
    public static OwnershipWave FirstWave(OwnershipRequest request, int entitiesPerChunk, int chunksPerWave) =>
        WaveAt(request, ScopeConstants.EntityTypesInPhaseOrder[0], waveIndex: 0, entitiesPerChunk, chunksPerWave);

    /// <summary>
    /// What follows the wave that just finished.
    ///
    /// <para>A wave that read every row it asked for might be followed by more of the same phase. A
    /// short one means the phase ran off its end, so the run moves on to the next phase's wave zero —
    /// or ends, after the last phase.</para>
    ///
    /// <para>Failed chunks follow ATRE's rule: one counts as full, because taking its zero at face
    /// value would end the phase early and skip every later window; a wave where every chunk failed
    /// ends the phase, because assuming it full would loop a phase that can no longer read anything
    /// until the execution-history cap. Here that moves the run on to the next phase rather than
    /// stopping it — a phase that cannot be read says nothing about the others, and the lost windows
    /// are already in <c>workflow.error</c>.</para>
    /// </summary>
    public static OwnershipWave NextWaveAfter(
        OwnershipRequest request,
        string entityType,
        int finishedWaveIndex,
        IReadOnlyList<OwnershipChunkSummary> results,
        int entitiesPerChunk,
        int chunksPerWave)
    {
        var entitiesRead = results.Sum(r => r.Failed ? entitiesPerChunk : r.EntitiesScanned);
        var waveWasFull = entitiesRead == chunksPerWave * entitiesPerChunk;
        var anyChunkRan = results.Any(r => !r.Failed);

        if (waveWasFull && anyChunkRan)
        {
            return WaveAt(request, entityType, finishedWaveIndex + 1, entitiesPerChunk, chunksPerWave);
        }

        var phases = ScopeConstants.EntityTypesInPhaseOrder;
        var nextPhase = IndexOfPhase(entityType) + 1;

        return nextPhase < phases.Count
            ? WaveAt(request, phases[nextPhase], waveIndex: 0, entitiesPerChunk, chunksPerWave)
            : new OwnershipWave(entityType, finishedWaveIndex + 1, Chunks: [], HasMore: false, PhaseStart: false);
    }

    private static int IndexOfPhase(string entityType)
    {
        var index = ScopeConstants.EntityTypesInPhaseOrder.ToList().IndexOf(entityType);
        return index >= 0
            ? index
            : throw new ArgumentOutOfRangeException(nameof(entityType), entityType, "Unknown Ownership phase.");
    }
}
