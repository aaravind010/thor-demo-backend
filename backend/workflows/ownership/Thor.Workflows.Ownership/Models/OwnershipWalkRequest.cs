namespace Thor.Workflows.Ownership.Models;

/// <summary>
/// What <see cref="Steps.OwnershipWalkStep"/> is invoked with: the run's identity and the phase whose
/// walk rules to propagate. The state machine builds it from <c>$.Request</c> and the wave's
/// <see cref="OwnershipWave.EntityType"/>, both of which always resolve.
/// </summary>
public sealed record OwnershipWalkRequest(OwnershipRequest Request, string EntityType);

/// <summary>
/// The walk step's answer. <see cref="IsInProgress"/> is read by the state machine's Choice straight
/// off the Lambda response: true means the time budget ran out with levels still to go, and the
/// state machine invokes the step again to carry on from the staged depth.
/// </summary>
public sealed record OwnershipWalkResult(string EntityType, int RulesWalked, int LevelsRun, int RowsStaged, bool IsInProgress);
