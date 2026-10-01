namespace Thor.Workflows.Atre.Models;

/// <summary>
/// One Distributed Map item: the run's identity plus the window of accounts this invocation reads for
/// itself. Only the window travels — a Map item cannot carry ten thousand account ids, because Step
/// Functions caps a state's payload at 256 KB.
///
/// <para>The item is built whole by the step that plans the wave rather than assembled in the state
/// machine by an <c>ItemSelector</c>. A reference path in an <c>ItemSelector</c> that resolves to
/// nothing fails the execution with <c>States.Runtime</c>, and both
/// <see cref="ScanManifestId"/> and <see cref="RunId"/> are genuinely optional — ingestion starts
/// ATRE with a manifest and no run id, an API caller with a run id and no manifest. Planning the item
/// in C#, where an absent field is simply <c>null</c>, keeps the state machine off that ground
/// entirely.</para>
/// </summary>
public sealed record AtreChunkRequest(
    Guid TenantId,
    Guid? ScanManifestId,
    Guid? RunId,
    int Offset,
    int Limit);

/// <summary>
/// What <see cref="Steps.AtreNextWaveStep"/> is invoked with: the run's identity (so it can build the
/// next wave's items in full), which wave just finished, and what that wave's chunks reported.
/// <see cref="Results"/> is the Map's own output, passed through unchanged.
/// </summary>
public sealed record AtreNextWaveRequest(
    AtreRequest Request,
    int WaveIndex,
    IReadOnlyList<AtreChunkSummary>? Results);

/// <summary>
/// One wave's worth of work. Part of the state machine's contract: the Map's <c>ItemsPath</c> reads
/// <see cref="Chunks"/> and the Choice that closes the loop reads <see cref="HasMore"/>.
/// </summary>
public sealed record AtreWave(
    int WaveIndex,
    IReadOnlyList<AtreChunkRequest> Chunks,
    bool HasMore);
