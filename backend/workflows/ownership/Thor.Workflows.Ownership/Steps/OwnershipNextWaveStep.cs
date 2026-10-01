using Thor.Workflows.Abstractions;
using Thor.Workflows.Ownership.Models;

namespace Thor.Workflows.Ownership.Steps;

/// <summary>
/// What the state machine invokes at the bottom of the loop: reads the counts the wave just returned
/// and builds the next wave — of the same phase, or the next phase's first — or says the run is done.
/// Touches no database, like <c>AtreNextWaveStep</c>; a step rather than a <c>Pass</c> state because
/// Amazon States Language cannot sum an array.
/// </summary>
public sealed class OwnershipNextWaveStep : WorkflowStep<OwnershipNextWaveRequest>
{
    private readonly int _entitiesPerChunk;
    private readonly int _chunksPerWave;

    /// <summary>Test seam — lets tests pin the two dials the parameterless constructor reads from the environment.</summary>
    public OwnershipNextWaveStep(int entitiesPerChunk, int chunksPerWave)
    {
        _entitiesPerChunk = entitiesPerChunk;
        _chunksPerWave = chunksPerWave;
    }

    public OwnershipNextWaveStep()
        : this(OwnershipWaves.EntitiesPerChunkFromEnvironment(), OwnershipWaves.ChunksPerWaveFromEnvironment())
    {
    }

    protected override Task<StepResult> ExecuteAsync(OwnershipNextWaveRequest request, CancellationToken cancellationToken) =>
        Task.FromResult(StepResult.Completed(OwnershipWavePlanner.NextWaveAfter(
            request.Request, request.EntityType, request.WaveIndex, request.Results ?? [], _entitiesPerChunk, _chunksPerWave)));
}
