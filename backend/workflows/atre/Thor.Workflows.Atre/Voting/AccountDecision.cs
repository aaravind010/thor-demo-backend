namespace Thor.Workflows.Atre.Voting;

/// <summary>The winning account type for one account plus the voting context that produced it.</summary>
internal sealed record AccountDecision(
    Guid WinningAccountTypeId,
    IReadOnlyDictionary<Guid, decimal> VoteDistribution,
    IReadOnlyList<Guid> ContributingRuleIds,
    IReadOnlyDictionary<Guid, decimal> PrecisionScoreSnapshot);
