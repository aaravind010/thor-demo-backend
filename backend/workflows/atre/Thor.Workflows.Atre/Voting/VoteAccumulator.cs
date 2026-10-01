namespace Thor.Workflows.Atre.Voting;

/// <summary>
/// Deterministic winner selection over a set of firing votes: additive weighted accumulation per
/// target type, highest total wins (matches the POC's algorithm). Ties — two or more types with
/// the exact same total — are broken by each candidate's own highest-weight contributing vote,
/// then by that vote's rule id compared as its canonical hyphenated text form (ordinal) — NOT
/// .NET's internal <see cref="Guid"/> byte ordering, which doesn't match what
/// <c>ORDER BY id::text</c> would show in Postgres and isn't reproducible outside .NET. A given
/// rule id can only ever be the "best vote" for the one target type it's defined against, so two
/// candidates can never share the same (Weight, RuleId) pair — this tie-break always fully
/// resolves, and depends only on the input vote set's own values, never on database row order or
/// dictionary iteration order (the actual source of the POC's non-determinism).
/// </summary>
internal static class VoteAccumulator
{
    public static AccountDecision Decide(IReadOnlyList<AccountVote> votes)
    {
        if (votes.Count == 0)
        {
            throw new ArgumentException("Cannot decide a winner with zero votes.", nameof(votes));
        }

        var totals = new Dictionary<Guid, decimal>();
        var bestVotePerType = new Dictionary<Guid, (decimal Weight, Guid RuleId)>();
        var precisionSnapshot = new Dictionary<Guid, decimal>();
        var contributingRuleIds = new List<Guid>();

        foreach (var vote in votes)
        {
            totals[vote.TargetAccountTypeId] = totals.GetValueOrDefault(vote.TargetAccountTypeId) + vote.Weight;
            precisionSnapshot[vote.RuleId] = vote.Weight;
            contributingRuleIds.Add(vote.RuleId);

            if (!bestVotePerType.TryGetValue(vote.TargetAccountTypeId, out var best) ||
                IsBetterTieBreakVote(vote.Weight, vote.RuleId, best.Weight, best.RuleId))
            {
                bestVotePerType[vote.TargetAccountTypeId] = (vote.Weight, vote.RuleId);
            }
        }

        var maxTotal = totals.Values.Max();
        var candidates = totals.Where(kvp => kvp.Value == maxTotal).Select(kvp => kvp.Key).ToList();

        var winner = candidates.Count == 1
            ? candidates[0]
            : candidates
                .OrderByDescending(typeId => bestVotePerType[typeId].Weight)
                .ThenBy(typeId => bestVotePerType[typeId].RuleId.ToString("D"), StringComparer.Ordinal)
                .First();

        return new AccountDecision(winner, totals, contributingRuleIds, precisionSnapshot);
    }

    private static bool IsBetterTieBreakVote(decimal candidateWeight, Guid candidateRuleId, decimal currentWeight, Guid currentRuleId)
    {
        if (candidateWeight != currentWeight)
        {
            return candidateWeight > currentWeight;
        }

        return string.CompareOrdinal(candidateRuleId.ToString("D"), currentRuleId.ToString("D")) < 0;
    }
}
