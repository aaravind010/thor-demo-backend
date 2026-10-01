namespace Thor.Workflows.Ownership.Models;

/// <summary>
/// What one <see cref="Steps.OwnershipVoteStep"/> invocation did — one chunk of one phase.
///
/// <para><see cref="EntitiesScanned"/> is part of the state machine's contract: it is the number of
/// in-scope entities the window held, matched or not, and <see cref="Steps.OwnershipNextWaveStep"/>
/// sums it to decide whether the phase has more. It cannot be derived from the candidates, because
/// an entity no rule matches produces no candidate row at all.</para>
///
/// <para>A chunk that failed is tolerated by the Map and reports only <see cref="Failed"/>,
/// <see cref="EntityType"/> and <see cref="Offset"/>, leaving every count at zero — which is why
/// <see cref="Failed"/> exists: a zero <see cref="EntitiesScanned"/> otherwise reads as "ran off the
/// end of the phase".</para>
/// </summary>
public sealed record OwnershipChunkSummary(
    Guid RunId,
    string? EntityType,
    int Offset,
    int EntitiesScanned,
    int EntitiesMatched,
    int NewAssignments,
    int NewRankOneAssignments,
    int Flushes,
    bool Failed = false);
