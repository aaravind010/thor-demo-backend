using Thor.Workflows.Atre.Voting;
using Xunit;

namespace Thor.Workflows.Atre.Tests.Voting;

public sealed class VoteAccumulatorTests
{
    [Fact]
    public void Decide_SingleVote_WinsOutright()
    {
        var typeId = Guid.NewGuid();
        var ruleId = Guid.NewGuid();
        var decision = VoteAccumulator.Decide([new AccountVote(ruleId, typeId, 0.9m)]);

        Assert.Equal(typeId, decision.WinningAccountTypeId);
        Assert.Equal(0.9m, decision.VoteDistribution[typeId]);
        Assert.Equal([ruleId], decision.ContributingRuleIds);
    }

    [Fact]
    public void Decide_HighestWeightSingleRuleWins_OverLowerWeightRule()
    {
        var computer = Guid.NewGuid();
        var admSuffix = Guid.NewGuid();
        var decision = VoteAccumulator.Decide([
            new AccountVote(Guid.NewGuid(), computer, 0.99m),
            new AccountVote(Guid.NewGuid(), admSuffix, 0.75m),
        ]);

        Assert.Equal(computer, decision.WinningAccountTypeId);
    }

    [Fact]
    public void Decide_VotesForSameTypeAccumulateAdditively()
    {
        var serviceAccount = Guid.NewGuid();
        var human = Guid.NewGuid();
        var decision = VoteAccumulator.Decide([
            new AccountVote(Guid.NewGuid(), serviceAccount, 0.92m),
            new AccountVote(Guid.NewGuid(), serviceAccount, 0.88m),
            new AccountVote(Guid.NewGuid(), human, 0.65m),
        ]);

        Assert.Equal(serviceAccount, decision.WinningAccountTypeId);
        Assert.Equal(1.80m, decision.VoteDistribution[serviceAccount]);
    }

    [Fact]
    public void Decide_ExactTotalTie_BrokenByHighestSingleContributingWeight()
    {
        var typeA = Guid.NewGuid();
        var typeB = Guid.NewGuid();
        // Both types total 0.90: typeA from one 0.90 vote; typeB from two votes (0.50 + 0.40)
        // whose individual weights are each lower than typeA's single best vote.
        var decision = VoteAccumulator.Decide([
            new AccountVote(Guid.NewGuid(), typeA, 0.90m),
            new AccountVote(Guid.NewGuid(), typeB, 0.50m),
            new AccountVote(Guid.NewGuid(), typeB, 0.40m),
        ]);

        Assert.Equal(typeA, decision.WinningAccountTypeId);
    }

    [Fact]
    public void Decide_TieOnBestWeightToo_BrokenByLowestRuleIdOrdinalText()
    {
        var typeA = Guid.NewGuid();
        var typeB = Guid.NewGuid();
        var lowerRuleId = Guid.Parse("00000000-0000-0000-0000-000000000001");
        var higherRuleId = Guid.Parse("ffffffff-ffff-ffff-ffff-ffffffffffff");

        var decision = VoteAccumulator.Decide([
            new AccountVote(higherRuleId, typeA, 0.70m),
            new AccountVote(lowerRuleId, typeB, 0.70m),
        ]);

        Assert.Equal(typeB, decision.WinningAccountTypeId);
    }

    [Fact]
    public void Decide_ZeroVotes_Throws()
    {
        Assert.Throws<ArgumentException>(() => VoteAccumulator.Decide([]));
    }
}
