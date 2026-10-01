namespace Thor.Workflows.Ownership.Models;

/// <summary>
/// One Distributed Map item: the run's identity, the phase it belongs to, and the window of that
/// phase's in-scope entities this invocation reads for itself. Only the window travels — a Map item
/// cannot carry ten thousand entity ids, because Step Functions caps a state's payload at 256 KB.
///
/// <para>Built whole by the step that plans the wave, never assembled by an <c>ItemSelector</c>:
/// <see cref="ScanManifestId"/> and <see cref="RunId"/> are genuinely optional, and a reference path
/// that resolves to nothing fails the execution with <c>States.Runtime</c>. <see cref="Scope"/> is
/// already resolved (see <see cref="OwnershipScopes.Resolve"/>), so no item re-decides it.</para>
/// </summary>
public sealed record OwnershipChunkRequest(
    Guid TenantId,
    Guid? ScanManifestId,
    Guid? RunId,
    string Scope,
    string EntityType,
    int Offset,
    int Limit);

/// <summary>
/// What <see cref="Steps.OwnershipNextWaveStep"/> is invoked with: the run's identity, which phase
/// and wave just finished, and what that wave's chunks reported. <see cref="Results"/> is the Map's
/// own output, passed through unchanged.
/// </summary>
public sealed record OwnershipNextWaveRequest(
    OwnershipRequest Request,
    string EntityType,
    int WaveIndex,
    IReadOnlyList<OwnershipChunkSummary>? Results);

/// <summary>
/// One wave's worth of work. Part of the state machine's contract: the Map's <c>ItemsPath</c> reads
/// <see cref="Chunks"/>, and the Choice that closes the loop reads <see cref="HasMore"/> and
/// <see cref="PhaseStart"/> — a wave that opens a phase goes through the walk step first, so the
/// phase's walk rules have their candidates staged before any chunk of it votes.
/// </summary>
public sealed record OwnershipWave(
    string EntityType,
    int WaveIndex,
    IReadOnlyList<OwnershipChunkRequest> Chunks,
    bool HasMore,
    bool PhaseStart);
