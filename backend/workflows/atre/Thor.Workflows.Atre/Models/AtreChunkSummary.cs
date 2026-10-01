namespace Thor.Workflows.Atre.Models;

/// <summary>
/// What one <see cref="Steps.AtreClassifyStep"/> invocation did — one chunk's worth, not the whole
/// run.
///
/// <para><see cref="AccountsScanned"/> is part of the state machine's contract, not just
/// observability: the Map hands these back inline and <see cref="Steps.AtreNextWaveStep"/> sums them
/// to decide whether another wave follows. It is how the run learns it has reached the end without
/// anyone having counted the accounts up front.</para>
///
/// <para>Two shapes arrive under this one type. A chunk that ran produces all of it. A chunk that
/// failed is tolerated by the Map and reports only <see cref="Failed"/> and <see cref="Offset"/>,
/// leaving every count at zero — which is exactly why <see cref="Failed"/> has to exist. A zero
/// <see cref="AccountsScanned"/> otherwise reads as "this chunk ran off the end of the accounts", and
/// the planner would end the run early on the strength of a chunk that never ran at all.</para>
/// </summary>
public sealed record AtreChunkSummary(
    Guid RunId,
    int Offset,
    int AccountsScanned,
    int AccountsAssigned,
    int AccountsWithNoFiringRule,
    int NewAssignments,
    int Flushes,
    bool Failed = false);
