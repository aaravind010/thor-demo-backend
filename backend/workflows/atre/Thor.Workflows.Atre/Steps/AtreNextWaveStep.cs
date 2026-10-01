using Thor.Workflows.Abstractions;
using Thor.Workflows.Atre.Models;

namespace Thor.Workflows.Atre.Steps;

/// <summary>
/// What the state machine invokes at the bottom of the loop: reads the counts the wave just returned
/// and either builds the next wave or says the run is done.
///
/// <para>Touches no database. It runs after the Map rather than before it precisely so it always has
/// a finished wave to read — there is no first-wave special case, because
/// <see cref="AtreStartRunStep"/> hands back wave zero.</para>
///
/// <para>A step rather than a <c>Pass</c> state because Amazon States Language cannot sum an array.
/// It is also where the dials are read, from the same environment every step in this image gets.</para>
/// </summary>
public sealed class AtreNextWaveStep : WorkflowStep<AtreNextWaveRequest>
{
    private readonly int _accountsPerChunk;
    private readonly int _chunksPerWave;

    /// <summary>Test seam — lets tests pin the two dials the parameterless constructor reads from the environment.</summary>
    public AtreNextWaveStep(int accountsPerChunk, int chunksPerWave)
    {
        _accountsPerChunk = accountsPerChunk;
        _chunksPerWave = chunksPerWave;
    }

    public AtreNextWaveStep()
        : this(AtreWaves.AccountsPerChunkFromEnvironment(), AtreWaves.ChunksPerWaveFromEnvironment())
    {
    }

    protected override Task<StepResult> ExecuteAsync(AtreNextWaveRequest request, CancellationToken cancellationToken) =>
        Task.FromResult(StepResult.Completed(AtreWavePlanner.NextWaveAfter(
            request.Request, request.WaveIndex, request.Results ?? [], _accountsPerChunk, _chunksPerWave)));
}
