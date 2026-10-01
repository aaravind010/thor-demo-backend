namespace Thor.Api.Models;

/// <param name="VoteDistribution">JSON of per-identity vote totals behind this assignment.</param>
/// <param name="PrecisionScoreSnapshot">JSON of each contributing rule's weight at assignment time.</param>
public sealed record PartyAssignmentResponse(
    Guid Id,
    string EntityType,
    Guid EntityId,
    Guid IdentityId,
    int Rank,
    bool IsActive,
    bool IsOverride,
    string VoteDistribution,
    IReadOnlyList<Guid> ContributingRuleIds,
    string PrecisionScoreSnapshot,
    string RunId,
    DateTimeOffset AssignedAt,
    DateTimeOffset? OverriddenAt);
